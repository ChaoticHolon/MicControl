using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MicControlNG.Hotkeys;
using MicControlNG.Installation;
using MicControlNG.Platform;
using MicControlNG.Settings;
using MicControlNG.ViewModels;
using MicControlNG.Views;
using Microsoft.Extensions.Logging.Abstractions;
using Page = MicControlNG.ViewModels.Page;

namespace MicControlNG.Tests.Ui;

/// <summary>
/// Renders the real windows with sample data into docs/screenshots (light and dark) for the README and release notes.
/// Opt-in: set MICCONTROL_SCREENSHOTS=1 and run the tests; otherwise these are skipped.
/// </summary>
public sealed class ScreenshotTests
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("MICCONTROL_SCREENSHOTS") == "1";

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public void MainWindowPages(string theme)
    {
        Assert.SkipUnless(Enabled, "Set MICCONTROL_SCREENSHOTS=1 to render screenshots.");
        UseTheme(theme);
        using var harness = SampleHarness();
        var window = new MainWindow(harness.ViewModel, harness.Settings) { Width = 1000, Height = 780 };
        window.Show();
        harness.ViewModel.IsWindowVisible = true;

        foreach (var (page, name) in new[] { (Page.Home, "home"), (Page.ExtraHotkeys, "extra-hotkeys"), (Page.Sounds, "sounds"), (Page.Overlay, "overlay"), (Page.About, "about") })
        {
            harness.ViewModel.Navigate(page);
            if (page == Page.Home)
            {
                Speak(harness);
            }

            Save(window, $"{name}-{theme}");
        }

        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public void TrayPopup(string theme)
    {
        Assert.SkipUnless(Enabled, "Set MICCONTROL_SCREENSHOTS=1 to render screenshots.");
        UseTheme(theme);
        using var harness = SampleHarness();
        var popup = new TrayPopupWindow(harness.ViewModel, () => { });
        popup.Show();
        harness.ViewModel.IsPopupVisible = true;
        Speak(harness);
        Save(popup, $"tray-popup-{theme}");
        popup.Close();
    }

    [AvaloniaFact]
    public void OverlayBadges()
    {
        Assert.SkipUnless(Enabled, "Set MICCONTROL_SCREENSHOTS=1 to render screenshots.");
        UseTheme("dark");
        using var harness = SampleHarness();
        var overlay = new OverlayWindow(harness.ViewModel, harness.Settings, new FakeWindowInterop());
        overlay.Show();
        Save(overlay, "overlay-live");
        harness.Devices.Microphone.Mute = true;
        Save(overlay, "overlay-muted");
        overlay.Close();
    }

    [AvaloniaTheory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Installer(string theme)
    {
        Assert.SkipUnless(Enabled, "Set MICCONTROL_SCREENSHOTS=1 to render screenshots.");
        UseTheme(theme);
        var installer = new FakeInstaller();
        var window = new InstallerWindow(new InstallerViewModel(installer, new SetupCommand(SetupMode.Install), NullLogger<InstallerViewModel>.Instance));
        window.Show();
        Save(window, $"installer-{theme}");
        window.Close();
    }

    /// <summary>A believable setup: a named microphone, F13 push-to-talk, two extra hotkeys, live and speaking.</summary>
    private static TestHarness SampleHarness()
    {
        var harness = new TestHarness(s =>
        {
            s.Microphone.Hotkey.Key = HotkeyGesture.Parse("F13");
            s.Microphone.Hotkey.AlternativeKey = HotkeyGesture.Parse("XButton2");
            s.Microphone.DeviceId = "mic-1";
            s.Overlay.ShowSpeakingIndicator = true;
            s.ExtraHotkeys.Add(new ExtraHotkey { Action = HotkeyAction.ToggleMute, Hotkey = new HotkeySettings { Key = HotkeyGesture.Parse("Ctrl+F13") } });
            s.ExtraHotkeys.Add(new ExtraHotkey { Action = HotkeyAction.SpeakerVolumeUp, Hotkey = new HotkeySettings { Key = HotkeyGesture.Parse("Ctrl+Up") } });
        });
        harness.Devices.Microphones.Clear();
        harness.Devices.Microphones.Add(new AudioDeviceInfo("mic-1", "Microphone (USB Audio Device)"));
        harness.Devices.Microphones.Add(new AudioDeviceInfo("mic-2", "Headset Microphone (Wireless Headset)"));
        harness.Devices.RaiseDevicesChanged();
        harness.Devices.Microphone.Mute = false;
        return harness;
    }

    /// <summary>Feeds the level meter a voice-like level and lets its smoothing timer settle.</summary>
    private static void Speak(TestHarness harness)
    {
        for (var i = 0; i < 12; i++)
        {
            harness.LevelMonitor.Report(0.55f);
            Thread.Sleep(35);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void UseTheme(string theme) =>
        Application.Current!.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;

    private static void Save(TopLevel window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        // Grayscale text: subpixel (ClearType-style) smoothing leaves colour fringes in an image. The option isn't
        // inherited, so it goes on every visual.
        foreach (var visual in window.GetVisualDescendants().Prepend(window))
        {
            TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Antialias);
        }

        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"No frame rendered for {name}.");
        var folder = Directory.CreateDirectory(Path.Combine(RepositoryRoot(), "docs", "screenshots")).FullName;
        frame.Save(Path.Combine(folder, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Run the screenshot tests from inside the repository.");
    }
}
