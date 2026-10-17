using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using MicControlNG.Controls;
using MicControlNG.Diagnostics;
using MicControlNG.Installation;
using MicControlNG.Platform;
using MicControlNG.Services;
using MicControlNG.Settings;
using MicControlNG.ViewModels;
using MicControlNG.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MicControlNG;

/// <summary>How a platform head (the executable) configures the shared app.</summary>
public sealed class AppOptions
{
    /// <summary>Registers the platform's implementations of the MicControlNG.Platform contracts.</summary>
    public required Action<IServiceCollection> RegisterPlatform { get; init; }

    /// <summary>Started from the OS at sign-in: don't show the window.</summary>
    public bool IsAutostart { get; init; }

    /// <summary>Receives a callback to invoke when another instance asks this one to show itself.</summary>
    public Action<Action>? RegisterShowRequest { get; init; }

    /// <summary>Receives a callback to invoke when the installer asks this copy to exit (to replace or remove it).</summary>
    public Action<Action>? RegisterExitRequest { get; init; }

    /// <summary>Run as the installer or uninstaller instead of the app (see <see cref="SetupCommand"/>).</summary>
    public SetupCommand Setup { get; init; } = new(SetupMode.None);

    /// <summary>Starts a new copy of the app after this one ends (used when the UI thread stops responding).</summary>
    public Action? Relaunch { get; init; }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Disposed on lifetime exit; Application has no Dispose.")]
public partial class App : Application
{
    private static readonly Version? AppVersion = typeof(App).Assembly.GetName().Version;
    private static readonly OperatingSystem OsVersion = Environment.OSVersion;
    private readonly AppOptions? options;
    private IHost? host;
    private TrayIcon? trayIcon;
    private ILogger? appLogger;
    private FileLoggerProvider? fileLogger;
    private AppShutdown? appShutdown;
    private Action? cleanup;
    private Timer? watchdogTimer;

    /// <summary>Used by the XAML previewer and headless tests; nothing is composed.</summary>
    public App()
    {
    }

    public App(AppOptions options) => this.options = options;

    public static void ApplyTheme(AppTheme theme)
    {
        if (Current is { } app)
        {
            app.RequestedThemeVariant = theme switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }

    /// <summary>Registers the platform-independent services shared by every platform head.</summary>
    public static void RegisterCommonServices(IServiceCollection services, string settingsDirectory)
    {
        services
            .AddSingleton(new SettingsService(new SettingsStore(settingsDirectory, SettingsStore.DefaultLegacyDirectory)))
            .AddSingleton<AppShutdown>()
            .AddSingleton<IUiDispatcher, AvaloniaDispatcher>()
            .AddSingleton<IDialogService, DialogService>()
            .AddSingleton(sp => new NotificationSounds(
                sp.GetRequiredService<ISoundPlayer>(),
                Path.Combine(AppContext.BaseDirectory, "Resources", "Notifications"),
                Path.Combine(settingsDirectory, "Resources", "Notifications")))
            .AddSingleton<MainViewModel>()
            .AddSingleton<MainWindow>();
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override async void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (options is null || ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        var logLevels = new LogLevelSwitch();
        var fileLogs = new FileLoggerProvider(Path.Combine(SettingsStore.DefaultDirectory, "logs"), logLevels);
        // Everything reaches the providers; the file logger filters by the user's "Log detail" setting.
        builder.Logging.SetMinimumLevel(LogLevel.Trace).AddDebug().AddProvider(fileLogs);
        builder.Services.AddSingleton(logLevels);

        if (options.Setup.Mode != SetupMode.None)
        {
            // Setup never loads or imports settings: only the installer and logging are composed.
            options.RegisterPlatform(builder.Services);
            builder.Services.AddSingleton(options.Setup).AddSingleton<InstallerViewModel>();
            host = builder.Build();
            desktop.Exit += (_, _) => host.Dispose();
            await RunSetupAsync(desktop, host.Services, options.Setup);
            return;
        }

        RegisterCommonServices(builder.Services, SettingsStore.DefaultDirectory);
        options.RegisterPlatform(builder.Services);
        host = builder.Build();

        var services = host.Services;
        var settings = services.GetRequiredService<SettingsService>();
        var capabilities = services.GetRequiredService<PlatformCapabilities>();
        var logger = services.GetRequiredService<ILogger<App>>();
        var shutdown = services.GetRequiredService<AppShutdown>();
        (appLogger, fileLogger, appShutdown) = (logger, fileLogs, shutdown);
        logLevels.Minimum = (LogLevel)settings.Current.LogLevel;
        LogUnhandledExceptions(logger, fileLogs);
        if (logger.IsEnabled(LogLevel.Information))
        {
            LogStarting(logger, AppVersion, OsVersion, capabilities.LimitationNotice is null, settings.Directory, options.IsAutostart);
        }

        LogLogging(logger, logLevels.Minimum, fileLogs.FilePath);
        LogSettingsLoaded(logger, settings.Current.Version, settings.Current.SetupCompleted, settings.Current.Window.AppMode, settings.MigratedFromLegacy);
        ApplyTheme(settings.Current.Window.Theme);

        if (settings.MigratedFromLegacy)
        {
            try
            {
                services.GetRequiredService<IStartupRegistration>().MigrateLegacyEntries();
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                LogAutostartMigrationFailed(logger, ex);
            }
        }

        var hotkeys = services.GetRequiredService<IGlobalHotkeys>();
        HotkeyBox.CapturingChanged += (_, capturing) => hotkeys.IsPaused = capturing;

        var viewModel = services.GetRequiredService<MainViewModel>();
        viewModel.ApplyFirstRunDefaults();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.LogDetail))
            {
                logLevels.Minimum = (LogLevel)viewModel.LogDetail;
            }
        };

        var mainWindow = services.GetRequiredService<MainWindow>();
        mainWindow.ExitApplication = reason => Exit(desktop, mainWindow, logger, reason);
        viewModel.ExitRequested += (_, _) => Exit(desktop, mainWindow, logger, "installing an update");

        // The desktop lifetime shows its MainWindow when startup finishes, which would undo starting hidden in
        // Tray mode. So the window only becomes the MainWindow (used as the owner of file dialogs) once it is shown.
        void ShowMainWindow()
        {
            desktop.MainWindow ??= mainWindow;
            mainWindow.ShowAndActivate();
        }

        // Windows ends the process soon after WM_ENDSESSION returns, so write the log out before returning.
        var guard = new WindowGuard(services.GetRequiredService<IWindowInterop>(), shutdown, logger, () =>
        {
            Exit(desktop, mainWindow, logger, "the session is ending");
            fileLogs.Dispose();
        });
        guard.Watch(mainWindow, "main window");

        // The overlay and tray popup refuse to close, but a window can still be destroyed without being asked (e.g.
        // with its owner). A destroyed window can't be shown again, so a new one replaces it.
        OverlayWindow CreateOverlay()
        {
            var overlay = ActivatorUtilities.CreateInstance<OverlayWindow>(services);
            overlay.Closed += (_, _) => ReplaceWhenDestroyed("overlay", () => CreateOverlay().SyncVisibility());
            guard.Watch(overlay, "overlay");
            return overlay;
        }

        TrayPopupWindow? popup = null;
        TrayPopupWindow CreatePopup()
        {
            var created = new TrayPopupWindow(viewModel, ShowMainWindow, services.GetRequiredService<ILogger<TrayPopupWindow>>(), shutdown);
            created.Closed += (_, _) => ReplaceWhenDestroyed("tray popup", () => popup = CreatePopup());
            guard.Watch(created, "tray popup");
            return created;
        }

        void ReplaceWhenDestroyed(string window, Action replace)
        {
            if (!shutdown.IsExiting)
            {
                LogWindowReplaced(logger, window);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!shutdown.IsExiting)
                    {
                        replace();
                    }
                });
            }
        }

        popup = CreatePopup();
        trayIcon = new TrayIcon(
            this, viewModel, () => popup.Toggle(), ShowMainWindow, () => Exit(desktop, mainWindow, logger, "tray menu"),
            services.GetRequiredService<ILogger<TrayIcon>>());
        CreateOverlay().SyncVisibility();
        guard.ProtectHelperWindows();
        options.RegisterShowRequest?.Invoke(() =>
        {
            LogShowRequested(logger);
            ShowMainWindow();
        });
        options.RegisterExitRequest?.Invoke(() => Exit(desktop, mainWindow, logger, "asked to close by setup"));
        // A session end (shutdown, restart, sign-out, or an installer's Restart Manager request) exits the app.
        // Without this, the hidden main window cancels its close in Tray mode and that vetoes the shutdown. On Windows
        // the guard answers the query and exits on WM_ENDSESSION instead; this covers platforms without it.
        desktop.ShutdownRequested += (_, _) =>
        {
            if (shutdown.BeginExit("the session is ending"))
            {
                LogSessionEnding(logger);
            }

            mainWindow.IsExiting = true;
        };
        cleanup = () =>
        {
            watchdogTimer?.Dispose();
            guard.Dispose();
            trayIcon.Dispose();
            settings.Flush();
            host.Dispose();
        };
        desktop.Exit += (_, _) =>
        {
            LogExited(logger, shutdown.Reason ?? "unknown");
            shutdown.CleanUpOnce(cleanup);
        };
        StartWatchdog(logger, fileLogs, services.GetRequiredService<IGlobalHotkeys>());

        // Tray mode starts hidden (also at sign-in); Window mode is a normal app. First run always shows setup.
        if (!settings.Current.SetupCompleted || settings.Current.Window.AppMode == AppMode.Window)
        {
            ShowMainWindow();
        }
        else
        {
            LogStartedInTray(logger);
        }

        await viewModel.CheckForUpdatesOnStartupAsync();
    }

    /// <summary>Setup or uninstall: the installer window, or with --silent no window and an exit code.</summary>
    private static async Task RunSetupAsync(IClassicDesktopStyleApplicationLifetime desktop, IServiceProvider services, SetupCommand setup)
    {
        var viewModel = services.GetRequiredService<InstallerViewModel>();
        if (setup.Silent)
        {
            var succeeded = await viewModel.RunSilentAsync();
            // Queued: the work can finish before the dispatcher loop has started, and shutting down then crashes it.
            Avalonia.Threading.Dispatcher.UIThread.Post(() => desktop.Shutdown(succeeded ? 0 : 1));
            return;
        }

        var window = new InstallerWindow(viewModel);
        desktop.MainWindow = window;
        window.Closed += (_, _) => desktop.Shutdown();
        window.Show();
        await viewModel.StartAsync();
    }

    /// <summary>
    /// Called by the platform head after the UI loop has returned. If it ended without going through our exit (for
    /// example a stray WM_QUIT), clean up here so the tray icon, keyboard hook and settings don't outlive the UI.
    /// </summary>
    public void OnMainLoopEnded(int exitCode)
    {
        if (appLogger is null || appShutdown is null || cleanup is null)
        {
            return;
        }

        if (appShutdown.CleanUpOnce(cleanup))
        {
            LogMainLoopEndedUnexpectedly(appLogger, exitCode);
        }
        else
        {
            LogMainLoopEnded(appLogger, exitCode, appShutdown.Reason ?? "unknown");
        }

        fileLogger?.Dispose();
    }

    /// <summary>
    /// Checks every 20 seconds that the UI thread still runs posted work. After a minute without a response the app
    /// is a zombie (hung, or its dispatcher window is gone): release the keyboard hook so keys aren't swallowed,
    /// write the log, start a fresh copy and end this one.
    /// </summary>
    private void StartWatchdog(ILogger logger, FileLoggerProvider fileLogs, IGlobalHotkeys hotkeys)
    {
        var watchdog = new UiWatchdog(ping => Avalonia.Threading.Dispatcher.UIThread.Post(ping), missesBeforeFailure: 3);
        var interval = TimeSpan.FromSeconds(20);
        watchdogTimer = new Timer(_ =>
        {
            switch (watchdog.Check())
            {
                case UiWatchdogState.Late:
                    LogUiLate(logger, watchdog.Misses * (int)interval.TotalSeconds);
                    break;
                case UiWatchdogState.Failed:
                    watchdogTimer?.Dispose();
                    LogUiStopped(logger, watchdog.Misses * (int)interval.TotalSeconds, options?.Relaunch is not null);
                    (hotkeys as IDisposable)?.Dispose();
                    fileLogs.Dispose();
                    options?.Relaunch?.Invoke();
                    Environment.Exit(3);
                    break;
            }
        }, null, interval, interval);
    }

    private void Exit(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window, ILogger logger, string reason)
    {
        appShutdown?.BeginExit(reason);
        LogExiting(logger, reason);
        window.IsExiting = true;
        window.Close();
        desktop.Shutdown();
    }

    /// <summary>Gets crashes into the log file: the writer is flushed before the process goes down.</summary>
    private static void LogUnhandledExceptions(ILogger logger, FileLoggerProvider fileLogs)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            LogCrash(logger, e.ExceptionObject as Exception, e.IsTerminating);
            if (e.IsTerminating)
            {
                fileLogs.Dispose();
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) => LogUnobservedTaskException(logger, e.Exception);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not replace MicSwitch autostart entries")]
    private static partial void LogAutostartMigrationFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting MicControl {Version} on {Os}; full hotkey access: {FullAccess}; settings: {SettingsDirectory}; autostart: {IsAutostart}")]
    private static partial void LogStarting(ILogger logger, Version? version, OperatingSystem os, bool fullAccess, string settingsDirectory, bool isAutostart);

    [LoggerMessage(Level = LogLevel.Information, Message = "Log detail {Level}; writing to {File}")]
    private static partial void LogLogging(ILogger logger, LogLevel level, string file);

    [LoggerMessage(Level = LogLevel.Information, Message = "Settings loaded: version {Version}, setup completed {SetupCompleted}, app mode {AppMode}, imported from MicSwitch {Migrated}")]
    private static partial void LogSettingsLoaded(ILogger logger, int version, bool setupCompleted, AppMode appMode, bool migrated);

    [LoggerMessage(Level = LogLevel.Information, Message = "Started hidden in the tray")]
    private static partial void LogStartedInTray(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Another instance asked this one to show its window")]
    private static partial void LogShowRequested(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Exiting ({Reason})")]
    private static partial void LogExiting(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Exited ({Reason})")]
    private static partial void LogExited(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Session ending (shutdown, restart, sign-out or an installer request): exiting instead of hiding to the tray")]
    private static partial void LogSessionEnding(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {Window} was destroyed while the app is running; replacing it with a new one")]
    private static partial void LogWindowReplaced(ILogger logger, string window);

    [LoggerMessage(Level = LogLevel.Warning, Message = "UI thread hasn't processed work for about {Seconds} s")]
    private static partial void LogUiLate(ILogger logger, int seconds);

    [LoggerMessage(Level = LogLevel.Critical, Message = "UI thread stopped processing work for about {Seconds} s: releasing the keyboard hook and ending this copy (restarting: {Restarting})")]
    private static partial void LogUiStopped(ILogger logger, int seconds, bool restarting);

    [LoggerMessage(Level = LogLevel.Information, Message = "UI loop ended (exit code {ExitCode}, reason: {Reason})")]
    private static partial void LogMainLoopEnded(ILogger logger, int exitCode, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "UI loop ended without an exit request (exit code {ExitCode}); cleaned up and ending the process")]
    private static partial void LogMainLoopEndedUnexpectedly(ILogger logger, int exitCode);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception (terminating: {IsTerminating})")]
    private static partial void LogCrash(ILogger logger, Exception? exception, bool isTerminating);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unobserved task exception")]
    private static partial void LogUnobservedTaskException(ILogger logger, Exception exception);
}
