using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using MicControlNG.Controls;
using MicControlNG.Hotkeys;
using MicControlNG.Views;

namespace MicControlNG.Tests.Ui;

public sealed class MainWindowTests
{
    [AvaloniaFact]
    public void Window_ShowsStatus_AndMuteButtonToggles()
    {
        using var harness = new TestHarness(s => s.Microphone.MuteMode = MuteMode.ToggleMute);
        var window = new MainWindow(harness.ViewModel, harness.Settings);
        window.Show();

        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Microphone live");
        var muteButton = window.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, "Mute"));

        muteButton.Command!.Execute(null);

        Assert.True(harness.Devices.Microphone.Mute);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Microphone muted");
        Assert.Equal(["Beep750"], harness.Sounds.Played);
        Assert.Equal("Muted", harness.ViewModel.StateLabel);
    }

    [AvaloniaFact]
    public void Window_ListsDevicesAndSounds()
    {
        using var harness = new TestHarness();
        var window = new MainWindow(harness.ViewModel, harness.Settings);
        window.Show();

        Assert.Contains(harness.ViewModel.Microphones, d => d.Name == "USB Microphone");
        Assert.Equal(["None", "Beep300", "Beep750"], harness.ViewModel.SoundOptions.Select(o => o.Name));
    }

    [AvaloniaFact]
    public void Sidebar_NavigatesBetweenPages()
    {
        using var harness = new TestHarness();
        var window = new MainWindow(harness.ViewModel, harness.Settings);
        window.Show();

        var nav = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Classes.Contains("nav"));
        Assert.Equal(["Home", "Extra hotkeys", "Sounds", "Overlay", "About"], harness.ViewModel.NavItems.Select(n => n.Title));

        foreach (var item in harness.ViewModel.NavItems)
        {
            nav.SelectedItem = item;
            window.UpdateLayout();
            var title = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("pageTitle") && t.IsEffectivelyVisible);
            Assert.Equal(item.Title, Assert.Single(title).Text, ignoreCase: true);
        }
    }

    [AvaloniaFact]
    public void Setup_CoversWindow_UntilSkipped()
    {
        using var harness = new TestHarness(s => s.SetupCompleted = false);
        var window = new MainWindow(harness.ViewModel, harness.Settings);
        window.Show();

        var setup = window.GetVisualDescendants().OfType<Views.Pages.SetupView>().Single();
        Assert.True(setup.IsVisible);
        harness.ViewModel.SkipSetupCommand.Execute(null);
        Assert.False(setup.IsVisible);
    }

    [AvaloniaFact]
    public void Theme_Switches()
    {
        using var harness = new TestHarness();
        harness.ViewModel.Theme = Settings.AppTheme.Dark;
        Assert.Equal(Avalonia.Styling.ThemeVariant.Dark, Avalonia.Application.Current!.RequestedThemeVariant);
        harness.ViewModel.Theme = Settings.AppTheme.System;
        Assert.Equal(Avalonia.Styling.ThemeVariant.Default, Avalonia.Application.Current!.RequestedThemeVariant);
    }

    [AvaloniaFact]
    public void HotkeyBox_CapturesCombination_AndBackspaceClears()
    {
        using var harness = new TestHarness();
        var window = new MainWindow(harness.ViewModel, harness.Settings);
        window.Show();
        var box = window.GetVisualDescendants().OfType<HotkeyBox>().First(b => b.IsEffectivelyVisible);

        box.Focus();
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F5, KeyModifiers = KeyModifiers.Control });

        Assert.Equal(HotkeyGesture.Parse("Ctrl+F5"), harness.ViewModel.MainHotkey.Key);
        Assert.Equal("Ctrl+F5", box.Text);

        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Back });

        Assert.True(harness.ViewModel.MainHotkey.Key.IsEmpty);
    }

    [AvaloniaFact]
    public void HotkeyBox_LoneModifier_CommitsOnRelease()
    {
        using var harness = new TestHarness();
        var window = new MainWindow(harness.ViewModel, harness.Settings);
        window.Show();
        var box = window.GetVisualDescendants().OfType<HotkeyBox>().First(b => b.IsEffectivelyVisible);

        box.Focus();
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.LeftCtrl, KeyModifiers = KeyModifiers.Control });
        Assert.True(harness.ViewModel.MainHotkey.Key.IsEmpty);
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.LeftCtrl });

        Assert.Equal(new HotkeyGesture(HotkeyModifiers.None, "LeftCtrl"), harness.ViewModel.MainHotkey.Key);
    }

    [AvaloniaFact]
    public void OverlayWindow_Loads()
    {
        using var harness = new TestHarness();
        var overlay = new OverlayWindow(harness.ViewModel, harness.Settings, new FakeWindowInterop());
        overlay.SyncVisibility();

        Assert.True(overlay.IsVisible);
        Assert.NotNull(overlay.GetVisualDescendants().OfType<OverlayBadge>().SingleOrDefault());
        harness.ViewModel.OverlayVisibility = OverlayVisibilityMode.Never;
        Assert.False(overlay.IsVisible);
    }
}
