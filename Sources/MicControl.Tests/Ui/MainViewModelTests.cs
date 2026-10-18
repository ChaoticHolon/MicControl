using Avalonia.Headless.XUnit;
using MicControl.Hotkeys;
using MicControl.Settings;
using MicControl.ViewModels;

namespace MicControl.Tests.Ui;

public sealed class MainViewModelTests
{
    [AvaloniaTheory]
    [InlineData(true, true, true)] // a new user of an installed copy: on by default
    [InlineData(true, false, false)] // development or portable build: never registers itself
    [InlineData(false, true, false)] // existing user: their choice is kept
    public void FirstRun_StartsWithWindows_OnlyForNewUsersOfAnInstalledCopy(bool firstRun, bool installed, bool expected)
    {
        using var harness = new TestHarness(firstRun: firstRun);
        harness.Updates.IsInstalled = installed;

        harness.ViewModel.ApplyFirstRunDefaults();

        Assert.Equal(expected, harness.Startup.IsEnabled);
        Assert.Equal(expected, harness.ViewModel.RunAtStartup);
    }

    [AvaloniaFact]
    public void FirstRun_DefaultsToTrayMode_AndShowsSetup()
    {
        using var harness = new TestHarness(firstRun: true);

        Assert.Equal(AppMode.Tray, harness.ViewModel.AppMode);
        Assert.True(harness.ViewModel.IsSetupVisible);
    }

    [AvaloniaFact]
    public async Task Update_InstallerStarts_AsksTheAppToExit()
    {
        using var harness = new TestHarness();
        harness.Updates.IsInstalled = true;
        harness.Updates.Available = "2.1.0";
        var vm = harness.ViewModel;
        var exits = 0;
        vm.ExitRequested += (_, _) => exits++;

        await vm.CheckUpdatesCommand.ExecuteAsync(null);
        Assert.Equal("2.1.0", vm.UpdateVersion);

        await vm.InstallUpdateCommand.ExecuteAsync(null);
        Assert.Equal(1, harness.Updates.Installs);
        Assert.Equal(1, exits);
    }

    [AvaloniaFact]
    public async Task Update_InstallerDoesNotStart_KeepsRunning()
    {
        using var harness = new TestHarness();
        harness.Updates.IsInstalled = true;
        harness.Updates.Available = "2.1.0";
        harness.Updates.InstallerStarts = false;
        var exits = 0;
        harness.ViewModel.ExitRequested += (_, _) => exits++;

        await harness.ViewModel.CheckUpdatesCommand.ExecuteAsync(null);
        await harness.ViewModel.InstallUpdateCommand.ExecuteAsync(null);

        Assert.Equal(0, exits);
    }

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
    public void Toggle_StartsMuted_AndFlipsOnPressOnly()
    {
        using var harness = new TestHarness(s =>
        {
            s.Microphone.MuteMode = MuteMode.ToggleMute;
            s.Microphone.Hotkey.Key = HotkeyGesture.Parse("F13");
        });
        var hotkey = harness.ViewModel.Settings.Microphone.Hotkey;
        Assert.True(harness.ViewModel.IsMuted); // the default startup state

        harness.Hotkeys.Trigger(hotkey, pressed: true);
        harness.Hotkeys.Trigger(hotkey, pressed: false);
        Assert.False(harness.ViewModel.IsMuted);

        harness.Hotkeys.Trigger(hotkey, pressed: true);
        Assert.True(harness.ViewModel.IsMuted);
    }

    [AvaloniaFact]
    public void CustomHotkeys_AddRunAndRemove()
    {
        using var harness = new TestHarness(s => s.CustomHotkeysEnabled = true);
        var vm = harness.ViewModel;

        vm.AddCustomHotkeyCommand.Execute(null);
        var row = Assert.Single(vm.CustomHotkeys);
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
        Assert.Empty(vm.CustomHotkeys);
        Assert.Empty(vm.Settings.CustomHotkeys);
    }

    [AvaloniaFact]
    public void CustomHotkeyActions_ExcludeWhatTheMainKeyDoes()
    {
        using var harness = new TestHarness();
        var vm = harness.ViewModel;
        vm.AddCustomHotkeyCommand.Execute(null);
        var row = vm.CustomHotkeys[0];

        Assert.DoesNotContain(row.Actions, a => Equals(a.Value, HotkeyAction.PushToTalk));
        Assert.NotEqual(HotkeyAction.PushToTalk, row.Action);

        vm.MuteMode = MuteMode.ToggleMute;
        Assert.Contains(row.Actions, a => Equals(a.Value, HotkeyAction.PushToTalk));
        Assert.Equal("Toggle key", vm.MainHotkeyLabel);
    }

    [AvaloniaFact]
    public void SpeakerAction_ChangesSpeakers_AndShowsOverlayIndicator()
    {
        using var harness = new TestHarness(s =>
        {
            s.CustomHotkeysEnabled = true;
            s.CustomHotkeys.Add(new CustomHotkey
            {
                Action = HotkeyAction.SpeakerMute,
                Hotkey = new HotkeySettings { Key = HotkeyGesture.Parse("F15") },
            });
        });

        Assert.True(harness.ViewModel.HasSpeakerActions);
        harness.Hotkeys.Trigger(harness.ViewModel.Settings.CustomHotkeys[0].Hotkey, pressed: true);

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
        using var harness = new TestHarness(s => s.Notifications.CustomSoundsEnabled = true);
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
    public void SpeakingIndicator_WithVisibleOverlay_StartsWithoutCrashing()
    {
        using var harness = new TestHarness(s =>
        {
            s.Overlay.Visibility = OverlayVisibilityMode.Always;
            s.Overlay.ShowSpeakingIndicator = true;
        });

        Assert.Equal(1, harness.LevelMonitor.ActiveSessions);
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
        using var harness = new TestHarness(s => s.Notifications.WhenMuted = "relay off");

        Assert.Equal("Relay off", harness.ViewModel.SoundWhenMuted);
        Assert.Contains(harness.ViewModel.SoundOptions, o => Equals(o.Value, harness.ViewModel.SoundWhenMuted));
        Assert.Equal(string.Empty, harness.ViewModel.PlaybackDeviceId);
        Assert.Contains(harness.ViewModel.PlaybackDevices, o => Equals(o.Value, string.Empty) && o.Name == "Default device");

        harness.ViewModel.SoundWhenUnmuted = string.Empty;
        Assert.Null(harness.ViewModel.Settings.Notifications.WhenUnmuted);
    }

    [AvaloniaFact]
    public void WindowIcon_FollowsTheMuteState()
    {
        using var harness = new TestHarness();
        var vm = harness.ViewModel;
        var before = vm.CurrentWindowIcon;

        vm.ToggleMuteCommand.Execute(null);
        Assert.NotSame(before, vm.CurrentWindowIcon);

        vm.ToggleMuteCommand.Execute(null);
        Assert.Same(before, vm.CurrentWindowIcon); // the built-in icons are loaded once
    }

    [AvaloniaFact]
    public void AdvancedFeatures_AreOffByDefault_AndHideTheirPages()
    {
        using var harness = new TestHarness();
        var vm = harness.ViewModel;

        Assert.False(vm.CustomHotkeysEnabled);
        Assert.False(vm.CustomSoundsEnabled);
        Assert.Equal([Page.Home, Page.Overlay, Page.About], vm.NavItems.Select(n => n.Page));

        vm.CustomSoundsEnabled = true;
        vm.CustomHotkeysEnabled = true;
        Assert.Equal([Page.Home, Page.Overlay, Page.About, Page.Hotkeys, Page.Sounds], vm.NavItems.Select(n => n.Page));

        vm.Navigate(Page.Sounds);
        vm.CustomSoundsEnabled = false;
        Assert.Equal(Page.Home, vm.CurrentPage); // its page went away
        vm.Navigate(Page.Sounds);
        Assert.Equal(Page.Home, vm.CurrentPage);
    }

    [AvaloniaFact]
    public void CustomHotkeys_ArePausedWhileSwitchedOff()
    {
        using var harness = new TestHarness(s => s.CustomHotkeys.Add(new CustomHotkey
        {
            Action = HotkeyAction.Unmute,
            Hotkey = new HotkeySettings { Key = HotkeyGesture.Parse("F14") },
        }));
        var vm = harness.ViewModel;
        var hotkey = vm.Settings.CustomHotkeys[0].Hotkey;
        Assert.True(vm.IsMuted); // push-to-talk starts muted

        harness.Hotkeys.Trigger(hotkey, pressed: true);
        Assert.True(vm.IsMuted); // not registered
        Assert.Single(vm.CustomHotkeys); // but kept

        vm.CustomHotkeysEnabled = true;
        harness.Hotkeys.Trigger(hotkey, pressed: true);
        Assert.False(vm.IsMuted);
    }

    [AvaloniaFact]
    public void CustomSounds_Off_PlaysTheBuiltInCues_AndKeepsTheChoices()
    {
        using var harness = new TestHarness(s =>
        {
            s.Microphone.MuteMode = MuteMode.ToggleMute;
            s.Microphone.InitialState = MicrophoneState.Unmute;
            s.Notifications.WhenMuted = null; // "None" on the Sounds page
        });
        var vm = harness.ViewModel;

        vm.ToggleMuteCommand.Execute(null);
        Assert.Equal([NotificationSettings.DefaultMutedSound], harness.Sounds.Played);
        Assert.Null(vm.Settings.Notifications.WhenMuted);

        vm.CustomSoundsEnabled = true;
        vm.ToggleMuteCommand.Execute(null);
        vm.ToggleMuteCommand.Execute(null);
        Assert.Equal([NotificationSettings.DefaultMutedSound, NotificationSettings.DefaultUnmutedSound], harness.Sounds.Played);
    }

    [AvaloniaFact]
    public void MainHotkeySummary_SaysWhatTheKeyDoes()
    {
        using var harness = new TestHarness(s => s.Microphone.Hotkey.Key = HotkeyGesture.Parse("F13"));
        var vm = harness.ViewModel;

        Assert.Equal("Hold F13 to talk", vm.MainHotkeySummary);
        vm.MuteMode = MuteMode.ToggleMute;
        Assert.Equal("Press F13 to switch", vm.MainHotkeySummary);
        vm.MainHotkey.Key = HotkeyGesture.Empty;
        Assert.Equal("No hotkey set", vm.MainHotkeySummary);
    }

    [AvaloniaFact]
    public void RunInTheTraySwitch_SetsTheAppMode()
    {
        using var harness = new TestHarness();
        var vm = harness.ViewModel;
        Assert.True(vm.IsTrayMode); // Tray is the default

        vm.IsTrayMode = false;
        Assert.Equal(AppMode.Window, vm.Settings.Window.AppMode);
        Assert.Contains("Closing the window exits", vm.AppModeDescription, StringComparison.Ordinal);

        vm.IsTrayMode = true;
        Assert.Equal(AppMode.Tray, vm.Settings.Window.AppMode);
    }
}
