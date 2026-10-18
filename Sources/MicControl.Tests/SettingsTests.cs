using MicControl.Hotkeys;
using MicControl.Settings;

namespace MicControl.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("miccontrol-settings-tests").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new SettingsStore(directory);
        var settings = new AppSettings();
        settings.Microphone.Hotkey.Key = HotkeyGesture.Parse("Ctrl+F2");
        settings.Microphone.MuteMode = MuteMode.PushToTalk;
        settings.Overlay.Bounds = new WindowBounds(10, 20, 120, 120);
        settings.Overlay.Monitor = new WindowBounds(-1920, 0, 1920, 1080);

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(HotkeyGesture.Parse("Ctrl+F2"), loaded.Microphone.Hotkey.Key);
        Assert.Equal(MuteMode.PushToTalk, loaded.Microphone.MuteMode);
        Assert.Equal(new WindowBounds(10, 20, 120, 120), loaded.Overlay.Bounds);
        Assert.Equal(new WindowBounds(-1920, 0, 1920, 1080), loaded.Overlay.Monitor);
        Assert.Contains("\"Ctrl+F2\"", File.ReadAllText(store.FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void Defaults_ArePushToTalk_NonExclusive_AndNeedSetup()
    {
        var settings = new SettingsStore(directory).Load();

        Assert.Equal(MuteMode.PushToTalk, settings.Microphone.MuteMode);
        Assert.False(settings.Microphone.Hotkey.Suppress);
        Assert.False(settings.SetupCompleted);
        Assert.Equal(0.85, settings.Overlay.Opacity);
    }

    [Fact]
    public void Load_FileWithoutOverlayMonitor_Loads()
    {
        File.WriteAllText(
            Path.Combine(directory, SettingsStore.FileName),
            """{ "version": 1, "overlay": { "corner": "BottomLeft", "bounds": { "left": 5, "top": 6, "width": 110, "height": 52 } } }""");

        var settings = new SettingsStore(directory).Load();

        Assert.Null(settings.Overlay.Monitor);
        Assert.Equal(OverlayCorner.BottomLeft, settings.Overlay.Corner);
        Assert.Equal(new WindowBounds(5, 6, 110, 52), settings.Overlay.Bounds);
    }

    [Fact]
    public void NoneSound_SurvivesSaveAndLoad()
    {
        var store = new SettingsStore(directory);
        var settings = new AppSettings();
        settings.Notifications.WhenUnmuted = null;

        store.Save(settings);

        Assert.Null(store.Load().Notifications.WhenUnmuted);
        Assert.Equal(NotificationSettings.DefaultMutedSound, store.Load().Notifications.WhenMuted);
    }

    [Fact]
    public void Load_MigratesMicSwitch1xConfig_FromLegacyFolder()
    {
        var legacy = Path.Combine(directory, "MicSwitch");
        var target = Path.Combine(directory, "MicControl");
        Directory.CreateDirectory(Path.Combine(legacy, "release"));
        Directory.CreateDirectory(Path.Combine(legacy, "Resources", "Notifications"));
        File.WriteAllBytes(Path.Combine(legacy, "Resources", "Notifications", "old.wav"), [3]);
        File.WriteAllText(
            Path.Combine(legacy, "release", "config.cfg"),
            """{ "Items": [ { "TypeName": "MicSwitch.Modularity.MicSwitchOverlayConfig", "ConfigValue": { "MicrophoneIcon": "AQID" } } ] }""",
            System.Text.Encoding.Unicode);

        var store = new SettingsStore(target, legacy);
        var settings = store.Load();

        Assert.True(store.MigratedFromLegacy);
        Assert.StartsWith(target, settings.Overlay.UnmutedIconPath, StringComparison.Ordinal);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(settings.Overlay.UnmutedIconPath!));
        Assert.True(File.Exists(Path.Combine(target, "Resources", "Notifications", "old.wav")));
    }

    [Fact]
    public void Load_NoLegacyData_ReturnsDefaultsWithoutMigrating()
    {
        var store = new SettingsStore(Path.Combine(directory, "new"), Path.Combine(directory, "missing"));

        Assert.False(store.Load().SetupCompleted);
        Assert.False(store.MigratedFromLegacy);
        Assert.True(store.IsFirstRun);
    }

    [Fact]
    public void IsFirstRun_OnlyWithoutSettingsOrAnythingToImport()
    {
        var store = new SettingsStore(directory);
        store.Save(new AppSettings());
        store.Load();
        Assert.False(store.IsFirstRun);

        var legacy = Path.Combine(directory, "MicSwitch");
        Directory.CreateDirectory(Path.Combine(legacy, "release"));
        File.WriteAllText(Path.Combine(legacy, "release", "config.cfg"), """{ "Items": [] }""", System.Text.Encoding.Unicode);
        var importing = new SettingsStore(Path.Combine(directory, "MicControl"), legacy);
        importing.Load();
        Assert.True(importing.MigratedFromLegacy);
        Assert.False(importing.IsFirstRun);
    }

    [Fact]
    public void AppMode_DefaultsToTray()
    {
        Assert.Equal(AppMode.Tray, new SettingsStore(directory).Load().Window.AppMode);
        Assert.Equal(AppMode.Window, SettingsStore.Deserialize("""{ "window": { "appMode": "window" } }""").Window.AppMode);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaultsAndKeepsCopy()
    {
        var store = new SettingsStore(directory);
        File.WriteAllText(store.FilePath, "{ not json");

        var loaded = store.Load();

        Assert.Equal(MuteMode.PushToTalk, loaded.Microphone.MuteMode);
        Assert.True(File.Exists(store.FilePath + ".corrupt"));
    }

    [Fact]
    public void Load_MigratesLegacyConfig()
    {
        const string legacy = """
        {
          "Version": 1,
          "Items": [
            { "AssemblyName": "MicSwitch", "TypeName": "MicSwitch.Modularity.MicSwitchConfig", "Version": 1,
              "ConfigValue": {
                "MicrophoneLineId": { "LineId": "{0.0.1.00000000}.{abc}", "Name": "Mic" },
                "MinimizeOnClose": false, "StartMinimized": true,
                "MainWindowBounds": "100,50,600,680",
                "Notifications": { "On": "Beep300", "Off": "custom" },
                "NotificationVolume": 0.5 } },
            { "AssemblyName": "MicSwitch", "TypeName": "MicSwitch.Modularity.MicSwitchHotkeyConfig", "Version": 1,
              "ConfigValue": {
                "Hotkey": { "Key": "CONTROL+F1", "AlternativeKey": "MouseXButton1", "Suppress": false, "IgnoreModifiers": false },
                "MuteMode": "PushToTalk", "EnableAdvancedHotkeys": true, "InitialMicrophoneState": "Mute",
                "HotkeyForToggle": { "Key": "F9", "Suppress": true } } },
            { "AssemblyName": "MicSwitch", "TypeName": "MicSwitch.Modularity.MicSwitchOverlayConfig", "Version": 1,
              "ConfigValue": {
                "OverlayBounds": "10, 20, 120, 120", "OverlayOpacity": 0.8,
                "OverlayVisibilityMode": "WhenMuted", "MicrophoneIcon": "AQID" } },
            { "AssemblyName": "MicSwitch", "TypeName": "MicSwitch.Modularity.MicSwitchVolumeControlConfig", "Version": 1,
              "ConfigValue": { "IsEnabled": true, "DeviceId": { "LineId": "all" }, "HotkeyForVolumeUp": { "Key": "CTRL+Up" } } }
          ]
        }
        """;
        Directory.CreateDirectory(Path.Combine(directory, "release"));
        File.WriteAllText(Path.Combine(directory, "release", "config.cfg"), legacy, System.Text.Encoding.Unicode);

        var settings = new SettingsStore(directory).Load();

        Assert.Equal("{0.0.1.00000000}.{abc}", settings.Microphone.DeviceId);
        Assert.Equal(MuteMode.PushToTalk, settings.Microphone.MuteMode);
        Assert.Equal(MicrophoneState.Mute, settings.Microphone.InitialState);
        Assert.Equal(HotkeyGesture.Parse("Ctrl+F1"), settings.Microphone.Hotkey.Key);
        Assert.Equal(MouseHotkey.XButton1, settings.Microphone.Hotkey.AlternativeKey.Mouse);
        Assert.False(settings.Microphone.Hotkey.Suppress);
        Assert.True(settings.SetupCompleted);
        var toggle = Assert.Single(settings.ExtraHotkeys, e => e.Action == HotkeyAction.ToggleMute);
        Assert.True(toggle.IsEnabled);
        Assert.Equal("F9", toggle.Hotkey.Key.Key);
        Assert.Equal("custom", settings.Notifications.WhenMuted);
        Assert.Equal(NotificationSettings.DefaultUnmutedSound, settings.Notifications.WhenUnmuted); // MicSwitch's beep
        Assert.Equal(0.5f, settings.Notifications.Volume);
        Assert.Equal(AppMode.Tray, settings.Window.AppMode);
        Assert.Equal(new WindowBounds(100, 50, 600, 680), settings.Window.Bounds);
        Assert.Equal(OverlayVisibilityMode.WhenMuted, settings.Overlay.Visibility);
        Assert.Equal(new WindowBounds(10, 20, 120, 120), settings.Overlay.Bounds);
        Assert.Equal(0.8, settings.Overlay.Opacity, 3);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(settings.Overlay.UnmutedIconPath!));
        var volumeUp = Assert.Single(settings.ExtraHotkeys, e => e.Action == HotkeyAction.SpeakerVolumeUp);
        Assert.True(volumeUp.IsEnabled);
        Assert.Equal(HotkeyGesture.Parse("Ctrl+Up"), volumeUp.Hotkey.Key);
        Assert.True(File.Exists(Path.Combine(directory, SettingsStore.FileName)));
    }

    [Theory]
    [InlineData("Beep300", "Beep750")] // MicSwitch's defaults
    [InlineData("beep750", "BEEP300")] // swapped, any case
    public void Load_LegacyConfigWithBeeps_GetsRelayCues(string on, string off)
    {
        // MicControl doesn't ship MicSwitch's beeps: each slot that used one gets its relay cue.
        var legacy = $$"""
        { "Version": 1, "Items": [
            { "AssemblyName": "MicSwitch", "TypeName": "MicSwitch.Modularity.MicSwitchConfig", "Version": 1,
              "ConfigValue": { "Notifications": { "On": "{{on}}", "Off": "{{off}}" } } } ] }
        """;
        Directory.CreateDirectory(Path.Combine(directory, "release"));
        File.WriteAllText(Path.Combine(directory, "release", "config.cfg"), legacy, System.Text.Encoding.Unicode);

        var settings = new SettingsStore(directory).Load();

        Assert.Equal(NotificationSettings.DefaultMutedSound, settings.Notifications.WhenMuted);
        Assert.Equal(NotificationSettings.DefaultUnmutedSound, settings.Notifications.WhenUnmuted);
    }

    [Fact]
    public void Load_OtherSettingsInTheMicSwitchFolder_AreNotImported()
    {
        // Only MicSwitch 1.x's config.cfg is imported; a settings.json there (from a pre-release build) is ignored.
        var legacy = Path.Combine(directory, "MicSwitch");
        var old = new AppSettings { SetupCompleted = true };
        old.Microphone.MuteMode = MuteMode.PushToMute;
        new SettingsStore(legacy).Save(old);

        var store = new SettingsStore(Path.Combine(directory, "MicControl"), legacy);
        var settings = store.Load();

        Assert.False(store.MigratedFromLegacy);
        Assert.True(store.IsFirstRun);
        Assert.Equal(MuteMode.PushToTalk, settings.Microphone.MuteMode);
        Assert.Equal(1, settings.Version);
    }
}
