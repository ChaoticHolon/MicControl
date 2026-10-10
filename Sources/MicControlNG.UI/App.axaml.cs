using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using MicControlNG.Controls;
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
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Disposed on lifetime exit; Application has no Dispose.")]
public partial class App : Application
{
    private static readonly Version? AppVersion = typeof(App).Assembly.GetName().Version;
    private static readonly OperatingSystem OsVersion = Environment.OSVersion;
    private readonly AppOptions? options;
    private IHost? host;
    private TrayIcon? trayIcon;

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
            .AddSingleton<IUiDispatcher, AvaloniaDispatcher>()
            .AddSingleton<IDialogService, DialogService>()
            .AddSingleton(sp => new NotificationSounds(
                sp.GetRequiredService<ISoundPlayer>(),
                Path.Combine(AppContext.BaseDirectory, "Resources", "Notifications"),
                Path.Combine(settingsDirectory, "Resources", "Notifications")))
            .AddSingleton<MainViewModel>()
            .AddSingleton<MainWindow>()
            .AddSingleton<OverlayWindow>();
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
        RegisterCommonServices(builder.Services, SettingsStore.DefaultDirectory);
        options.RegisterPlatform(builder.Services);
        host = builder.Build();

        var services = host.Services;
        var settings = services.GetRequiredService<SettingsService>();
        var capabilities = services.GetRequiredService<PlatformCapabilities>();
        var logger = services.GetRequiredService<ILogger<App>>();
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
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.LogDetail))
            {
                logLevels.Minimum = (LogLevel)viewModel.LogDetail;
            }
        };

        var mainWindow = services.GetRequiredService<MainWindow>();
        mainWindow.ExitApplication = () => Exit(desktop, mainWindow, logger, "window closed in Window mode");

        // The desktop lifetime shows its MainWindow when startup finishes, which would undo starting hidden in
        // Tray mode. So the window only becomes the MainWindow (used as the owner of file dialogs) once it is shown.
        void ShowMainWindow()
        {
            desktop.MainWindow ??= mainWindow;
            mainWindow.ShowAndActivate();
        }

        var popup = new TrayPopupWindow(viewModel, ShowMainWindow, services.GetRequiredService<ILogger<TrayPopupWindow>>());
        trayIcon = new TrayIcon(
            this, viewModel, popup.Toggle, ShowMainWindow, () => Exit(desktop, mainWindow, logger, "tray menu"),
            services.GetRequiredService<ILogger<TrayIcon>>());
        services.GetRequiredService<OverlayWindow>().SyncVisibility();
        options.RegisterShowRequest?.Invoke(() =>
        {
            LogShowRequested(logger);
            ShowMainWindow();
        });
        desktop.Exit += (_, _) =>
        {
            LogExited(logger);
            trayIcon.Dispose();
            settings.Flush();
            host.Dispose();
        };

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

    private static void Exit(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window, ILogger logger, string reason)
    {
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Exited")]
    private static partial void LogExited(ILogger logger);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception (terminating: {IsTerminating})")]
    private static partial void LogCrash(ILogger logger, Exception? exception, bool isTerminating);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unobserved task exception")]
    private static partial void LogUnobservedTaskException(ILogger logger, Exception exception);
}
