using Avalonia;
using MicControlNG.Installation;
using MicControlNG.Platform;
using MicControlNG.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace MicControlNG;

internal static class Program
{
    // STA is required: Core Audio COM objects are created on the UI thread.
    [STAThread]
    private static int Main(string[] args)
    {
        // The setup download is this same program: under its setup name, or with --install/--uninstall, it shows the
        // installer instead of the app (see SetupCommand).
        var setup = SetupCommand.Parse(args, Environment.ProcessPath);
        return setup.Mode == SetupMode.None ? RunApp(args) : RunSetup(args, setup);
    }

    private static int RunApp(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, WindowsPlatform.MutexName, out var isFirstInstance);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, WindowsPlatform.ShowSignalName);
        if (!isFirstInstance)
        {
            // Ask the running instance to show its window.
            showSignal.Set();
            return 0;
        }

        using var exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, WindowsPlatform.ExitSignalName);
        RegisteredWaitHandle? showRegistration = null;
        RegisteredWaitHandle? exitRegistration = null;
        var options = new AppOptions
        {
            IsAutostart = args.Contains(StartupService.AutostartArgument, StringComparer.OrdinalIgnoreCase),
            RegisterPlatform = RegisterWindowsServices,
            RegisterShowRequest = show => showRegistration = OnSignal(showSignal, show),
            RegisterExitRequest = exit => exitRegistration = OnSignal(exitSignal, exit),
        };

        var exitCode = AppBuilder.Configure(() => new App(options))
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);

        showRegistration?.Unregister(null);
        exitRegistration?.Unregister(null);
        GC.KeepAlive(mutex);
        return exitCode;
    }

    private static int RunSetup(string[] args, SetupCommand setup)
    {
        using var mutex = new Mutex(initiallyOwned: true, WindowsPlatform.SetupMutexName, out var isFirstSetup);
        if (!isFirstSetup)
        {
            return 1;
        }

        var options = new AppOptions { RegisterPlatform = RegisterWindowsServices, Setup = setup };
        var exitCode = AppBuilder.Configure(() => new App(options))
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        GC.KeepAlive(mutex);
        return exitCode;
    }

    private static RegisteredWaitHandle OnSignal(WaitHandle signal, Action action) => ThreadPool.RegisterWaitForSingleObject(
        signal, (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(action), null, Timeout.Infinite, executeOnlyOnce: false);

    // Used by the Avalonia XAML previewer.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();

    private static void RegisterWindowsServices(IServiceCollection services) => services
        .AddSingleton(WindowsPlatform.Capabilities)
        .AddSingleton<IAudioDevices, AudioDeviceService>()
        .AddSingleton<IGlobalHotkeys, GlobalHotkeyService>()
        .AddSingleton<IInputLevelMonitor, InputLevelMonitor>()
        .AddSingleton<ISoundPlayer, WasapiSoundPlayer>()
        .AddSingleton<IStartupRegistration, StartupService>()
        .AddSingleton<IWindowInterop, WindowInterop>()
        .AddSingleton<IInstaller, WindowsInstaller>()
        .AddSingleton<IUpdateService, UpdateService>();
}
