using Avalonia;
using Avalonia.Headless;
using MicControl.Platform;
using MicControl.Services;
using MicControl.Settings;
using MicControl.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

[assembly: AvaloniaTestApplication(typeof(MicControl.Tests.Ui.TestHarness))]

namespace MicControl.Tests.Ui;

/// <summary>Builds the real view model and views on top of fake platform services.</summary>
internal sealed class TestHarness : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("miccontrol-ui").FullName;
    private readonly string builtInSounds;

    /// <param name="configure">Adjusts the initial settings; setup is marked complete unless changed here.</param>
    /// <param name="firstRun">Start with no settings file at all, like a new user (<paramref name="configure"/> is ignored).</param>
    public TestHarness(Action<AppSettings>? configure = null, bool firstRun = false)
    {
        builtInSounds = Path.Combine(directory, "builtin");
        Directory.CreateDirectory(builtInSounds);
        foreach (var name in new[] { NotificationSettings.DefaultMutedSound, NotificationSettings.DefaultUnmutedSound })
        {
            File.WriteAllBytes(Path.Combine(builtInSounds, name + ".wav"), [0]);
        }

        var store = new SettingsStore(directory);
        if (!firstRun)
        {
            var initial = new AppSettings { SetupCompleted = true };
            configure?.Invoke(initial);
            store.Save(initial);
        }

        Settings = new SettingsService(store);
        ViewModel = new MainViewModel(
            Settings,
            Devices,
            Hotkeys,
            new NotificationSounds(Sounds, builtInSounds, Path.Combine(directory, "user-sounds")),
            Startup,
            Updates,
            Dialogs,
            LevelMonitor,
            new PlatformCapabilities { CanSuppressHotkeys = true, SupportsMouseHotkeys = true },
            NullLogger<MainViewModel>.Instance);
    }

    public FakeAudioDevices Devices { get; } = new();

    public FakeHotkeys Hotkeys { get; } = new();

    public FakeSoundPlayer Sounds { get; } = new();

    public FakeStartup Startup { get; } = new();

    public FakeUpdates Updates { get; } = new();

    public FakeDialogs Dialogs { get; } = new();

    public FakeLevelMonitor LevelMonitor { get; } = new();

    public SettingsService Settings { get; }

    public MainViewModel ViewModel { get; }

    public string SettingsFile => Path.Combine(directory, SettingsStore.FileName);

    // Skia rather than headless drawing: windows really render, so they can be captured (see ScreenshotTests).
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public void Dispose()
    {
        ViewModel.Dispose();
        Directory.Delete(directory, recursive: true);
    }
}
