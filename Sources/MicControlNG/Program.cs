using Avalonia;
using MicControlNG.Platform;
using MicControlNG.Windows;
using Microsoft.Extensions.DependencyInjection;
using Velopack;

namespace MicControlNG;

internal static class Program
{
    private const string InstanceId = "MicControlNG-{567EBFFF-E391-4B38-AC85-469978EB37C4}";

    // STA is required: Core Audio COM objects are created on the UI thread.
    [STAThread]
    private static void Main(string[] args)
    {
        // Must run first: handles install/uninstall/update hooks and may exit the process.
        VelopackApp.Build().Run();

        using var mutex = new Mutex(initiallyOwned: true, $@"Local\{InstanceId}", out var isFirstInstance);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{InstanceId}-show");
        if (!isFirstInstance)
        {
            // Ask the running instance to show its window.
            showSignal.Set();
            return;
        }

        RegisteredWaitHandle? showRegistration = null;
        var options = new AppOptions
        {
            IsAutostart = args.Contains(StartupService.AutostartArgument, StringComparer.OrdinalIgnoreCase),
            RegisterPlatform = RegisterWindowsServices,
            RegisterShowRequest = show => showRegistration = ThreadPool.RegisterWaitForSingleObject(
                showSignal, (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(show), null, Timeout.Infinite, executeOnlyOnce: false),
        };

        AppBuilder.Configure(() => new App(options))
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);

        showRegistration?.Unregister(null);
        GC.KeepAlive(mutex);
    }

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
        .AddSingleton<IUpdateService, UpdateService>();
}
