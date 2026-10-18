using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using MicControl.Controls;
using Avalonia.Threading;
using MicControl.Hotkeys;
using MicControl.Settings;
using MicControl.ViewModels;
using MicControl.Views;

namespace MicControl.Tests.Ui;

public sealed class MainWindowTests
{
    [AvaloniaFact]
    public void Window_ShowsStatus_AndMuteButtonToggles()
    {
        using var harness = new TestHarness(s =>
        {
            s.Microphone.MuteMode = MuteMode.ToggleMute;
            s.Microphone.InitialState = MicrophoneState.Unmute;
        });
        var window = new MainWindow(harness.ViewModel, harness.Settings);
        window.Show();

        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Microphone live");
        var muteButton = window.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, "Mute"));

        muteButton.Command!.Execute(null);

        Assert.True(harness.Devices.Microphone.Mute);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Microphone muted");
        Assert.Equal([NotificationSettings.DefaultMutedSound], harness.Sounds.Played);
        Assert.Equal("Muted", harness.ViewModel.StateLabel);
    }

    [AvaloniaFact]
    public void OpeningTheWindow_DoesNotChangeSettings()
    {
        using var harness = new TestHarness(s => s.Notifications.OutputDeviceId = "speaker-1");
        var before = SettingsStore.Serialize(harness.Settings.Current);
        var changes = new List<string?>();
        harness.ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.SoundWhenMuted) or nameof(MainViewModel.SoundWhenUnmuted) or nameof(MainViewModel.PlaybackDeviceId))
            {
                changes.Add(e.PropertyName);
            }
        };

        var window = new MainWindow(harness.ViewModel, harness.Settings);
        window.Show();
        foreach (var page in Enum.GetValues<ViewModels.Page>())
        {
            harness.ViewModel.Navigate(page);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Empty(changes);
        Assert.Equal(before, SettingsStore.Serialize(harness.Settings.Current));
    }

    [AvaloniaFact]
    public void Window_ListsDevicesAndSounds()
    {
        using var harness = new TestHarness();
        var window = new MainWindow(harness.ViewModel, harness.Settings);
        window.Show();

        Assert.Contains(harness.ViewModel.Microphones, d => d.Name == "USB Microphone");
        Assert.Equal(["None", "Relay off", "Relay on"], harness.ViewModel.SoundOptions.Select(o => o.Name));
    }

    [AvaloniaFact]
    public void Tabs_NavigateBetweenPages()
    {
        using var harness = new TestHarness(s =>
        {
            s.CustomHotkeysEnabled = true;
            s.Notifications.CustomSoundsEnabled = true;
        });
        var window = new MainWindow(harness.ViewModel, harness.Settings);
        window.Show();

        var nav = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Classes.Contains("tabs"));
        Assert.Equal(["MicControl", "Overlay", "About", "Hotkeys", "Sounds"], harness.ViewModel.NavItems.Select(n => n.Title));

        foreach (var item in harness.ViewModel.NavItems)
        {
            nav.SelectedItem = item;
            window.UpdateLayout();
            var title = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("pageTitle") && t.IsEffectivelyVisible);
            Assert.Equal(item.IsBrand ? "Home" : item.Title, Assert.Single(title).Text, ignoreCase: true); // the name tab leads Home
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

    [AvaloniaFact]
    public void AdvancedSwitches_AreOnAbout_NotOnHome()
    {
        using var harness = new TestHarness();
        var window = new MainWindow(harness.ViewModel, harness.Settings);
        window.Show();

        string[] VisibleSwitches() => window.GetVisualDescendants().OfType<ToggleSwitch>()
            .Where(t => t.IsEffectivelyVisible).Select(t => Avalonia.Automation.AutomationProperties.GetName(t) ?? "").ToArray();

        Assert.DoesNotContain("Custom hotkeys", VisibleSwitches());
        harness.ViewModel.Navigate(ViewModels.Page.About);
        window.UpdateLayout();
        Assert.Equal(["Custom hotkeys", "Custom sounds"], VisibleSwitches()[^2..]);

        window.GetVisualDescendants().OfType<ToggleSwitch>().Single(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Custom hotkeys").IsChecked = true;
        Assert.Contains(harness.ViewModel.NavItems, n => n.Page == ViewModels.Page.Hotkeys && n.Title == "Hotkeys");
    }

    [AvaloniaFact]
    public void Header_ReplacesTheTitleBar_AndKeepsItsControlsClickable()
    {
        using var harness = new TestHarness();
        var window = new MainWindow(harness.ViewModel, harness.Settings);
        window.Show();

        Assert.True(window.ExtendClientAreaToDecorationsHint);
        var tabs = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Classes.Contains("tabs"));
        Assert.Equal(Avalonia.Input.WindowDecorationsElementRole.User, Avalonia.Controls.Chrome.WindowDecorationProperties.GetElementRole(tabs));
        var header = tabs.GetVisualAncestors().OfType<Border>()
            .First(b => Avalonia.Controls.Chrome.WindowDecorationProperties.GetElementRole(b) == Avalonia.Input.WindowDecorationsElementRole.TitleBar);
        Assert.Equal(window.ExtendClientAreaTitleBarHeightHint, header.Height);

        // The status chip shows on every page, Home included, and switches the microphone.
        var chip = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("statusChip"));
        Assert.True(chip.IsEffectivelyVisible);
        var muted = harness.ViewModel.IsMuted;
        chip.Command!.Execute(null);
        Assert.NotEqual(muted, harness.ViewModel.IsMuted);

        // Avalonia's own title text and full-screen button would overlap the header; our decorations theme hides them.
        var decorations = window.WindowDecorationsTheme;
        Assert.NotNull(decorations);
        Assert.NotNull(decorations.BasedOn);
        var hidden = decorations.Children.OfType<Avalonia.Styling.Style>().Select(s => s.Selector?.ToString() ?? "").ToList();
        Assert.Contains(hidden, s => s.Contains("PART_TitleTextPanel", StringComparison.Ordinal) && s.Contains("TextBlock", StringComparison.Ordinal));
        Assert.Contains(hidden, s => s.Contains("PART_FullScreenButton", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void HeaderDecorations_HideTheDrawnTitleAndFullScreenButton()
    {
        // Avalonia draws the title bar itself once the header replaces it; build those decorations with our theme, as
        // the window does, and check what would be on screen.
        using var harness = new TestHarness();
        var window = new MainWindow(harness.ViewModel, harness.Settings) { Title = "MicControl" };
        window.Show();
        // Headless windows never ask for drawn decorations, so make the window's host create them the way Windows does.
        const System.Reflection.BindingFlags Internal = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var host = typeof(TopLevel).GetProperty("TopLevelHost", Internal)!.GetValue(window)!;
        var partsType = typeof(Avalonia.Controls.Chrome.WindowDrawnDecorations).Assembly.GetType("Avalonia.Controls.Chrome.DrawnWindowDecorationParts")!;
        host.GetType().GetMethod("UpdateDrawnDecorations", Internal)!.Invoke(host, [Enum.Parse(partsType, "All"), WindowState.Normal, window.WindowDecorationsTheme]);
        var decorations = (Avalonia.Controls.Chrome.WindowDrawnDecorations)host.GetType().GetProperty("Decorations", Internal)!.GetValue(host)!;
        var overlay = Assert.IsAssignableFrom<Control>(decorations.Content!.Overlay);
        var parts = overlay.GetVisualDescendants().OfType<Control>().Prepend(overlay).ToList();

        var title = parts.OfType<Panel>().Single(p => p.Name == "PART_TitleTextPanel").Children.OfType<TextBlock>().Single();
        Assert.False(title.IsVisible);
        Assert.False(parts.OfType<Button>().Single(b => b.Name == "PART_FullScreenButton").IsVisible);
        Assert.True(parts.OfType<Button>().Single(b => b.Name == "PART_CloseButton").IsVisible);
    }
}
