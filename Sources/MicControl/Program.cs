using System.Diagnostics;
using Avalonia;
using MicControl.Installation;
using MicControl.Platform;
using MicControl.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace MicControl;

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

    private const string RelaunchedFromArgument = "--relaunched-from=";

    private static int RunApp(string[] args)
    {
        WaitForRelaunchingCopy(args);
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
            // The UI watchdog starts a fresh copy before ending this one when the UI thread stops responding.
            Relaunch = () => Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                ArgumentList = { $"{RelaunchedFromArgument}{Environment.ProcessId}" },
                UseShellExecute = false,
            })?.Dispose(),
        };

        App? app = null;
        var exitCode = AppBuilder.Configure(() => app = new App(options))
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);

        // The main loop can also end without our Exit (a WM_QUIT from outside): clean up, then end the process
        // so nothing left behind keeps it alive without a UI.
        app?.OnMainLoopEnded(exitCode);
        showRegistration?.Unregister(null);
        exitRegistration?.Unregister(null);
        mutex.ReleaseMutex();
        Environment.Exit(exitCode);
        return exitCode;
    }

    // A copy started by the watchdog waits for the stuck copy to end, so it can take the single-instance mutex.
    private static void WaitForRelaunchingCopy(string[] args)
    {
        var argument = args.FirstOrDefault(a => a.StartsWith(RelaunchedFromArgument, StringComparison.OrdinalIgnoreCase));
        if (argument is null || !int.TryParse(argument.AsSpan(RelaunchedFromArgument.Length), out var pid))
        {
            return;
        }

        try
        {
            using var previous = Process.GetProcessById(pid);
            previous.WaitForExit(TimeSpan.FromSeconds(15));
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
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
