using Avalonia.Headless.XUnit;
using MicControlNG.Hotkeys;
using MicControlNG.Settings;
using MicControlNG.ViewModels;

namespace MicControlNG.Tests.Ui;

public sealed class MainViewModelTests
{
    [AvaloniaFact]
    public void PushToTalk_IsDefault_MutesAtStart_AndHotkeyUnmutesWhileHeld()
    {
        using var harness = new TestHarness(s => s.Microphone.Hotkey.Key = HotkeyGesture.Parse("F13"));
        var hotkey = harness.ViewModel.Settings.Microphone.Hotkey;

        Assert.Equal(MuteMode.PushToTalk, harness.ViewModel.MuteMode);
        Assert.True(harness.ViewModel.IsMuted);
        harness.Hotkeys.Trigger(hotkey, pressed: true);
        Assert.False(harness.ViewModel.IsMuted);
        harness.Hotkeys.Trigger(hotkey, pressed: false);
        Assert.True(harness.ViewModel.IsMuted);
        Assert.Equal("Push-to-talk key", harness.ViewModel.MainHotkeyLabel);
    }

    [AvaloniaFact]
    public void Toggle_FlipsOnPressOnly()
    {
        using var harness = new TestHarness(s =>
        {
            s.Microphone.MuteMode = MuteMode.ToggleMute;
            s.Microphone.Hotkey.Key = HotkeyGesture.Parse("F13");
        });
        var hotkey = harness.ViewModel.Settings.Microphone.Hotkey;

        harness.Hotkeys.Trigger(hotkey, pressed: true);
        harness.Hotkeys.Trigger(hotkey, pressed: false);
        Assert.True(harness.ViewModel.IsMuted);

        harness.Hotkeys.Trigger(hotkey, pressed: true);
        Assert.False(harness.ViewModel.IsMuted);
    }

    [AvaloniaFact]
    public void ExtraHotkeys_AddRunAndRemove()
    {
        using var harness = new TestHarness();
        var vm = harness.ViewModel;

        vm.AddExtraHotkeyCommand.Execute(null);
        var row = Assert.Single(vm.ExtraHotkeys);
        row.Action = HotkeyAction.Mute;
        row.Editor.Key = HotkeyGesture.Parse("F14");
        vm.ToggleMuteCommand.Execute(null);
        Assert.False(vm.IsMuted);

        harness.Hotkeys.Trigger(row.Model.Hotkey, pressed: true);
        Assert.True(vm.IsMuted);

        row.IsEnabled = false;
        vm.ToggleMuteCommand.Execute(null);
        harness.Hotkeys.Trigger(row.Model.Hotkey, pressed: true);
        Assert.False(vm.IsMuted);

        row.RemoveCommand.Execute(null);
        Assert.Empty(vm.ExtraHotkeys);
        Assert.Empty(vm.Settings.ExtraHotkeys);
    }

    [AvaloniaFact]
    public void ExtraHotkeyActions_ExcludeWhatTheMainKeyDoes()
    {
        using var harness = new TestHarness();
        var vm = harness.ViewModel;
        vm.AddExtraHotkeyCommand.Execute(null);
        var row = vm.ExtraHotkeys[0];

        Assert.DoesNotContain(row.Actions, a => Equals(a.Value, HotkeyAction.PushToTalk));
        Assert.NotEqual(HotkeyAction.PushToTalk, row.Action);

        vm.MuteMode = MuteMode.ToggleMute;
        Assert.Contains(row.Actions, a => Equals(a.Value, HotkeyAction.PushToTalk));
        Assert.Equal("Toggle key", vm.MainHotkeyLabel);
    }

    [AvaloniaFact]
    public void SpeakerAction_ChangesSpeakers_AndShowsOverlayIndicator()
    {
        using var harness = new TestHarness(s => s.ExtraHotkeys.Add(new ExtraHotkey
        {
            Action = HotkeyAction.SpeakerMute,
            Hotkey = new HotkeySettings { Key = HotkeyGesture.Parse("F15") },
        }));

        Assert.True(harness.ViewModel.HasSpeakerActions);
        harness.Hotkeys.Trigger(harness.ViewModel.Settings.ExtraHotkeys[0].Hotkey, pressed: true);

        Assert.True(harness.Devices.Speaker.Mute);
        Assert.True(harness.ViewModel.ShowOutputIndicator);
        Assert.Equal("Muted", harness.ViewModel.OutputIndicatorText);
    }

    [AvaloniaFact]
    public void QuickToggles_OverlayRestoresLastMode_AndSoundsCanBeSilenced()
    {
        using var harness = new TestHarness();
        var vm = harness.ViewModel;
        vm.OverlayVisibility = OverlayVisibilityMode.WhenMuted;

        vm.OverlayEnabled = false;
        Assert.Equal(OverlayVisibilityMode.Never, vm.OverlayVisibility);
        vm.OverlayEnabled = true;
        Assert.Equal(OverlayVisibilityMode.WhenMuted, vm.OverlayVisibility);

        vm.SoundsEnabled = false;
        vm.ToggleMuteCommand.Execute(null);
        Assert.Empty(harness.Sounds.Played);
    }

    [AvaloniaFact]
    public void LevelMeter_RunsOnlyWhileHomeIsVisible()
    {
        using var harness = new TestHarness();
        var vm = harness.ViewModel;

        Assert.Equal(0, harness.LevelMonitor.ActiveSessions);
        vm.IsWindowVisible = true;
        Assert.Equal(1, harness.LevelMonitor.ActiveSessions);
        vm.Navigate(Page.Sounds);
        Assert.Equal(0, harness.LevelMonitor.ActiveSessions);
        vm.Navigate(Page.Home);
        vm.IsWindowVisible = false;
        Assert.Equal(0, harness.LevelMonitor.ActiveSessions);
    }

    [AvaloniaFact]
    public void Setup_WalksThroughSteps_AndCompletes()
    {
        using var harness = new TestHarness(s => s.SetupCompleted = false);
        var vm = harness.ViewModel;

        Assert.True(vm.IsSetupVisible);
        Assert.True(vm.IsSetupStep1);
        vm.SetupNextCommand.Execute(null);
        vm.SetModeCommand.Execute(MuteMode.ToggleMute);
        Assert.Equal(MuteMode.ToggleMute, vm.MuteMode);
        vm.SetupNextCommand.Execute(null);
        Assert.Equal("Finish", vm.SetupNextText);
        vm.SetupNextCommand.Execute(null);
        Assert.True(vm.IsSetupDone);
        vm.SetupNextCommand.Execute(null);

        Assert.False(vm.IsSetupVisible);
        Assert.True(vm.Settings.SetupCompleted);
    }

    [AvaloniaFact]
    public void TrayNotice_OncePerSession_UntilDismissedForever()
    {
        using var harness = new TestHarness();

        Assert.True(harness.ViewModel.ShouldShowTrayNotice());
        Assert.False(harness.ViewModel.ShouldShowTrayNotice());
        Assert.Contains("right-click the tray icon to exit", harness.ViewModel.TrayNoticeText, StringComparison.OrdinalIgnoreCase);

        harness.ViewModel.DismissTrayNoticeForever();
        Assert.True(harness.ViewModel.Settings.Window.TrayNoticeDismissed);
    }

    [AvaloniaFact]
    public void AppMode_DefaultsToTray_AndCanBeChangedFromSetup()
    {
        using var harness = new TestHarness();

        Assert.True(harness.ViewModel.IsTrayMode);
        harness.ViewModel.SetAppModeCommand.Execute(AppMode.Window);
        Assert.Equal(AppMode.Window, harness.ViewModel.Settings.Window.AppMode);
        Assert.Contains("Closing the window exits", harness.ViewModel.AppModeDescription, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Changes_ArePersisted()
    {
        using var harness = new TestHarness();

        harness.ViewModel.MuteMode = MuteMode.PushToMute;
        harness.ViewModel.RunAtStartup = true;
        harness.Settings.Flush();

        Assert.Equal(MuteMode.PushToMute, new SettingsStore(Path.GetDirectoryName(harness.SettingsFile)!).Load().Microphone.MuteMode);
        Assert.True(harness.Startup.IsEnabled);
    }

    [AvaloniaFact]
    public void UnplugAndReplug_KeepsSelectedDeviceSelectable()
    {
        using var harness = new TestHarness(s => s.Microphone.DeviceId = "mic-1");

        harness.Devices.Microphones.Clear();
        harness.Devices.RaiseDevicesChanged();

        Assert.Contains(harness.ViewModel.Microphones, d => d.Id == "mic-1" && d.Name == "Disconnected device");
        Assert.Equal("mic-1", harness.ViewModel.SelectedMicrophoneId);
    }

    [AvaloniaFact]
    public void SoundAndPlaybackDropdowns_HaveMatchingSelections()
    {
        using var harness = new TestHarness(s => s.Notifications.WhenMuted = "beep750");

        Assert.Equal("Beep750", harness.ViewModel.SoundWhenMuted);
        Assert.Contains(harness.ViewModel.SoundOptions, o => Equals(o.Value, harness.ViewModel.SoundWhenMuted));
        Assert.Equal(string.Empty, harness.ViewModel.PlaybackDeviceId);
        Assert.Contains(harness.ViewModel.PlaybackDevices, o => Equals(o.Value, string.Empty) && o.Name == "Default device");

        harness.ViewModel.SoundWhenUnmuted = string.Empty;
        Assert.Null(harness.ViewModel.Settings.Notifications.WhenUnmuted);
    }
}
