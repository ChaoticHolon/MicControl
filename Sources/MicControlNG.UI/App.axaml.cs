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
        builder.Logging.AddDebug().AddProvider(new FileLoggerProvider(Path.Combine(SettingsStore.DefaultDirectory, "logs")));
        RegisterCommonServices(builder.Services, SettingsStore.DefaultDirectory);
        options.RegisterPlatform(builder.Services);
        host = builder.Build();

        var services = host.Services;
        var settings = services.GetRequiredService<SettingsService>();
        var capabilities = services.GetRequiredService<PlatformCapabilities>();
        var logger = services.GetRequiredService<ILogger<App>>();
        if (logger.IsEnabled(LogLevel.Information))
        {
            LogStarting(logger, AppVersion, OsVersion, capabilities.LimitationNotice is null, settings.Directory, options.IsAutostart);
        }
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
        var mainWindow = services.GetRequiredService<MainWindow>();
        desktop.MainWindow = mainWindow;
        mainWindow.ExitApplication = () => Exit(desktop, mainWindow);
        var popup = new TrayPopupWindow(viewModel, mainWindow.ShowAndActivate);
        trayIcon = new TrayIcon(this, viewModel, popup.Toggle, mainWindow.ShowAndActivate, () => Exit(desktop, mainWindow));
        services.GetRequiredService<OverlayWindow>().SyncVisibility();
        options.RegisterShowRequest?.Invoke(mainWindow.ShowAndActivate);
        desktop.Exit += (_, _) =>
        {
            trayIcon.Dispose();
            settings.Flush();
            host.Dispose();
        };

        // Tray mode starts hidden (also at sign-in); Window mode is a normal app. First run always shows setup.
        if (!settings.Current.SetupCompleted || settings.Current.Window.AppMode == AppMode.Window)
        {
            mainWindow.ShowAndActivate();
        }

        await viewModel.CheckForUpdatesOnStartupAsync();
    }

    private static void Exit(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window)
    {
        window.IsExiting = true;
        window.Close();
        desktop.Shutdown();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not replace MicSwitch autostart entries")]
    private static partial void LogAutostartMigrationFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting MicControl {Version} on {Os}; full hotkey access: {FullAccess}; settings: {SettingsDirectory}; autostart: {IsAutostart}")]
    private static partial void LogStarting(ILogger logger, Version? version, OperatingSystem os, bool fullAccess, string settingsDirectory, bool isAutostart);
}
