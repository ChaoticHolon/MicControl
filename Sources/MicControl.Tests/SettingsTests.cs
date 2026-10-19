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
    public void Defaults_ArePushToTalk_Muted_NonExclusive_AndNeedSetup()
    {
        var settings = new SettingsStore(directory).Load();

        Assert.Equal(MuteMode.PushToTalk, settings.Microphone.MuteMode);
        Assert.Equal(MicrophoneState.Mute, settings.Microphone.InitialState);
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
    public void Load_NullsFromAHandEditedFile_BecomeDefaults()
    {
        File.WriteAllText(
            Path.Combine(directory, SettingsStore.FileName),
            """
            { "version": 1, "setupCompleted": true, "microphone": { "deviceId": null, "hotkey": { "key": null, "alternativeKey": 5 } },
              "customHotkeys": [ null, { "action": "Mute", "hotkey": null } ], "speakerDeviceId": null,
              "notifications": null, "overlay": null, "window": null }
            """);

        var settings = new SettingsStore(directory).Load();

        Assert.True(settings.SetupCompleted); // the rest of the file is kept
        Assert.Equal(AppSettings.AllDevices, settings.Microphone.DeviceId);
        Assert.True(settings.Microphone.Hotkey.IsEmpty);
        var row = Assert.Single(settings.CustomHotkeys);
        Assert.Equal(HotkeyAction.Mute, row.Action);
        Assert.True(row.Hotkey.IsEmpty);
        Assert.Equal(AppSettings.AllDevices, settings.SpeakerDeviceId);
        Assert.NotNull(settings.Notifications);
        Assert.NotNull(settings.Overlay);
        Assert.NotNull(settings.Window);
        Assert.False(File.Exists(Path.Combine(directory, SettingsStore.FileName + ".corrupt")));
    }

    [Fact]
    public void Migrate_TypeListedTwice_KeepsTheLastEntry()
    {
        var settings = LegacyConfigMigrator.Migrate(
            """
            { "Items": [
              { "TypeName": "MicSwitch.MicSwitchHotkeyConfig", "ConfigValue": { "Hotkey": { "Key": "F1" } } },
              { "TypeName": 7, "ConfigValue": {} },
              { "TypeName": "MicSwitch.MicSwitchHotkeyConfig", "ConfigValue": { "Hotkey": { "Key": "F2" } } } ] }
            """,
            iconDirectory: null);

        Assert.Equal(HotkeyGesture.Parse("F2"), settings.Microphone.Hotkey.Key);
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
                "MuteMode": "ToggleMute", "EnableAdvancedHotkeys": true, "InitialMicrophoneState": "Unmute",
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
        Assert.Equal(MuteMode.PushToTalk, settings.Microphone.MuteMode); // MicControl's defaults, not MicSwitch's
        Assert.Equal(MicrophoneState.Mute, settings.Microphone.InitialState);
        Assert.Equal(HotkeyGesture.Parse("Ctrl+F1"), settings.Microphone.Hotkey.Key);
        Assert.Equal(MouseHotkey.XButton1, settings.Microphone.Hotkey.AlternativeKey.Mouse);
        Assert.False(settings.Microphone.Hotkey.Suppress);
        Assert.False(settings.SetupCompleted); // setup still runs, to review what was imported
        var toggle = Assert.Single(settings.CustomHotkeys, e => e.Action == HotkeyAction.ToggleMute);
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
        var volumeUp = Assert.Single(settings.CustomHotkeys, e => e.Action == HotkeyAction.SpeakerVolumeUp);
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

    [Fact]
    public void CustomSounds_AreOffByDefault_AndOnlyTheirChoicesPlayWhenOn()
    {
        var n = new AppSettings().Notifications;
        Assert.False(n.CustomSoundsEnabled);
        Assert.False(n.HasCustomChoices);

        (n.WhenMuted, n.WhenUnmuted, n.Volume, n.OutputDeviceId) = ("mine", null, 0.4f, "speaker-1");
        Assert.True(n.HasCustomChoices);
        Assert.Equal((NotificationSettings.DefaultMutedSound, NotificationSettings.DefaultUnmutedSound, 1f, (string?)null),
            (n.PlayedWhenMuted, n.PlayedWhenUnmuted, n.PlayedVolume, n.PlayedOutputDeviceId));

        n.CustomSoundsEnabled = true;
        Assert.Equal(("mine", (string?)null, 0.4f, "speaker-1"), (n.PlayedWhenMuted, n.PlayedWhenUnmuted, n.PlayedVolume, n.PlayedOutputDeviceId));
    }

    [Fact]
    public void Load_MicSwitchImport_KeepsCustomHotkeysAndSoundsSwitchedOff()
    {
        // Setup asks whether to use them; until then they are stored but inactive.
        var legacy = """
        { "Version": 1, "Items": [
            { "AssemblyName": "MicSwitch", "TypeName": "MicSwitch.Modularity.MicSwitchConfig", "Version": 1,
              "ConfigValue": { "Notifications": { "On": "mine", "Off": "Beep750" } } },
            { "AssemblyName": "MicSwitch", "TypeName": "MicSwitch.Modularity.MicSwitchHotkeyConfig", "Version": 1,
              "ConfigValue": { "EnableAdvancedHotkeys": true, "HotkeyForToggle": { "Key": "F9" } } } ] }
        """;
        Directory.CreateDirectory(Path.Combine(directory, "release"));
        File.WriteAllText(Path.Combine(directory, "release", "config.cfg"), legacy, System.Text.Encoding.Unicode);

        var settings = new SettingsStore(directory).Load();

        Assert.Single(settings.CustomHotkeys);
        Assert.False(settings.CustomHotkeysEnabled);
        Assert.True(settings.Notifications.HasCustomChoices);
        Assert.False(settings.Notifications.CustomSoundsEnabled);
    }

    [Fact]
    public void Load_LockedFile_LoadsDefaultsAndLeavesTheFileAlone()
    {
        var store = new SettingsStore(directory, Path.Combine(directory, "no-legacy"));
        File.WriteAllText(store.FilePath, """{ "setupCompleted": true, "microphone": { "muteMode": "ToggleMute" } }""");

        AppSettings loaded;
        using (new FileStream(store.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            loaded = store.Load();
        }

        Assert.NotNull(store.LoadError);
        Assert.False(store.IsFirstRun);
        Assert.True(loaded.SetupCompleted); // no setup over settings that are only out of reach for now
        store.Save(loaded);
        Assert.Contains("ToggleMute", File.ReadAllText(store.FilePath), StringComparison.Ordinal);

        Assert.Equal(MuteMode.ToggleMute, store.Load().Microphone.MuteMode);
        Assert.Null(store.LoadError);
    }

    [Fact]
    public void Load_FileWithByteOrderMark_Loads()
    {
        var store = new SettingsStore(directory);
        File.WriteAllText(store.FilePath, """{ "microphone": { "muteMode": "ToggleMute" } }""", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.Equal(MuteMode.ToggleMute, store.Load().Microphone.MuteMode);
        Assert.False(File.Exists(store.FilePath + ".corrupt"));
    }

    [Fact]
    public void Load_OutOfRangeValues_AreClampedAndCanBeSaved()
    {
        // 1e39 is too large for a float: it reads as infinity, which JSON can't hold, so every save used to throw.
        var store = new SettingsStore(directory);
        File.WriteAllText(store.FilePath, """
            {
              "microphone": { "muteMode": 99, "initialState": -1, "volume": 1e39 },
              "customHotkeys": [ { "action": 1234, "hotkey": { "key": "F1" } }, { "action": "Mute", "hotkey": { "key": "F2" } } ],
              "notifications": { "volume": -5 },
              "overlay": { "visibility": 42, "corner": 7, "opacity": 1e39, "scale": 0, "bounds": { "left": 1, "top": 2, "width": -10, "height": 50 } },
              "window": { "theme": 12, "bounds": { "left": 10, "top": 10, "width": -800, "height": -600 } },
              "logLevel": 77
            }
            """);

        var loaded = store.Load();
        store.Save(loaded);

        Assert.Equal(MuteMode.PushToTalk, loaded.Microphone.MuteMode);
        Assert.Equal(MicrophoneState.Mute, loaded.Microphone.InitialState);
        Assert.Equal(1, loaded.Microphone.Volume);
        Assert.Equal([HotkeyAction.Mute], loaded.CustomHotkeys.Select(h => h.Action));
        Assert.Equal(0, loaded.Notifications.Volume);
        Assert.Equal(OverlayVisibilityMode.Always, loaded.Overlay.Visibility);
        Assert.Equal(OverlayCorner.TopRight, loaded.Overlay.Corner);
        Assert.Equal(OverlaySettings.MaxOpacity, loaded.Overlay.Opacity);
        Assert.Equal(OverlaySettings.MinScale, loaded.Overlay.Scale);
        Assert.Null(loaded.Overlay.Bounds);
        Assert.Null(loaded.Window.Bounds);
        Assert.Equal(AppTheme.System, loaded.Window.Theme);
        Assert.Equal(LogDetail.Debug, loaded.LogLevel);
    }

    [Fact]
    public void Load_MicSwitchConfigWithHugeNumbers_ImportsAndSaves()
    {
        var legacy = Path.Combine(directory, "MicSwitch");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "config.cfg"), """
            {"Items":[{"TypeName":"MicSwitchConfig","ConfigValue":{"Volume":1e300,"NotificationVolume":-1e300,"OverlayOpacity":1e300,
              "MainWindowBounds":"Infinity, 0, 800, 600"}}]}
            """);
        var store = new SettingsStore(Path.Combine(directory, "MicControl"), legacy);

        var loaded = store.Load();

        Assert.True(store.MigratedFromLegacy);
        Assert.True(File.Exists(store.FilePath));
        Assert.Equal(1, loaded.Microphone.Volume);
        Assert.Equal(0, loaded.Notifications.Volume);
        Assert.Equal(OverlaySettings.MaxOpacity, loaded.Overlay.Opacity);
        Assert.Null(loaded.Window.Bounds);
    }

    [Theory]
    [InlineData("""{"Items":[1,"x",null,{"TypeName":"MicSwitchHotkeyConfig","ConfigValue":{"Hotkey":{"Key":"F13"}}}]}""")]
    [InlineData("""{"Items":[{"TypeName":"MicSwitchConfig","ConfigValue":{"MicrophoneLineId":"not an object"}},{"TypeName":"MicSwitchHotkeyConfig","ConfigValue":{"Hotkey":{"Key":"F13"}}}]}""")]
    [InlineData("""{"Items":[{"TypeName":"MicSwitchConfig","ConfigValue":{"MicrophoneIcon":"!!not base64"}},{"TypeName":"MicSwitchHotkeyConfig","ConfigValue":{"Hotkey":{"Key":"F13"}}}]}""")]
    [InlineData("""{"Items":[{"TypeName":"MicSwitchVolumeControlConfig","ConfigValue":{"DeviceId":42}},{"TypeName":"MicSwitchHotkeyConfig","ConfigValue":{"Hotkey":{"Key":"F13"}}}]}""")]
    public void Migrate_OneMalformedEntry_KeepsTheRest(string json)
    {
        var settings = LegacyConfigMigrator.Migrate(json, Path.Combine(directory, "Icons"));

        Assert.Equal(HotkeyGesture.Parse("F13"), settings.Microphone.Hotkey.Key);
    }
}
