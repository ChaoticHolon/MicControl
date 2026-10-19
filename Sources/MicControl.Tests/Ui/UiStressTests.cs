using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MicControl.Hotkeys;
using MicControl.Settings;
using MicControl.Views;

namespace MicControl.Tests.Ui;

// Rapid hotkeys, mode switches and device changes, posted from many threads the way the Windows hook and Core Audio
// callbacks reach the UI thread.
public sealed class UiStressTests
{
    private static void Configure(AppSettings s)
    {
        s.Microphone.Hotkey.Key = HotkeyGesture.Parse("F13");
        s.Microphone.Hotkey.AlternativeKey = HotkeyGesture.Parse("MouseXButton1");
        s.CustomHotkeysEnabled = true;
        s.Notifications.CustomSoundsEnabled = true;
        var key = 1;
        foreach (var action in Enum.GetValues<HotkeyAction>())
        {
            s.CustomHotkeys.Add(new CustomHotkey { Action = action, Hotkey = new HotkeySettings { Key = HotkeyGesture.Parse($"Ctrl+F{key++}") } });
        }
    }

    [AvaloniaFact]
    public async Task RapidHotkeysModesAndDevices_FromManyThreads_NeverThrow()
    {
        const int seed = 1;
        using var harness = new TestHarness(Configure);
        var vm = harness.ViewModel;
        var window = new MainWindow(vm, harness.Settings);
        var overlay = new OverlayWindow(vm, harness.Settings, new FakeWindowInterop());
        window.Show();
        overlay.Show();
        var errors = new List<Exception>();
        void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            errors.Add(e.Exception);
            e.Handled = true;
        }

        Dispatcher.UIThread.UnhandledException += OnUnhandled;
        try
        {
            var settings = harness.Settings.Current;
            var hotkeys = new[] { settings.Microphone.Hotkey }.Concat(settings.CustomHotkeys.Select(c => c.Hotkey)).ToArray();
            var modes = Enum.GetValues<MuteMode>();
            var pages = Enum.GetValues<ViewModels.Page>();
            var visibility = Enum.GetValues<OverlayVisibilityMode>();

            void Step(Random r)
            {
                switch (r.Next(12))
                {
                    case < 5:
                        harness.Hotkeys.Trigger(hotkeys[r.Next(hotkeys.Length)], r.Next(2) == 0);
                        break;
                    case 5:
                        vm.MuteMode = modes[r.Next(modes.Length)];
                        break;
                    case 6:
                        // A device unplugged or plugged back in, and the device list changing under it.
                        harness.Devices.Microphone.IsConnected = r.Next(3) > 0;
                        harness.Devices.Microphones.Clear();
                        for (var i = r.Next(4); i > 0; i--)
                        {
                            harness.Devices.Microphones.Add(new($"mic-{i}", $"Mic {i}"));
                        }

                        harness.Devices.RaiseDevicesChanged();
                        harness.Devices.Microphone.Rebind();
                        break;
                    case 7:
                        vm.SelectedMicrophoneId = r.Next(3) == 0 ? AppSettings.AllDevices : $"mic-{r.Next(4)}";
                        break;
                    case 8:
                        vm.Navigate(pages[r.Next(pages.Length)]);
                        break;
                    case 9:
                        vm.ToggleMuteCommand.Execute(null);
                        break;
                    case 10:
                        vm.CustomHotkeysEnabled = r.Next(4) > 0;
                        vm.OverlayVisibilityStep = r.Next(visibility.Length);
                        break;
                    default:
                        vm.MicrophoneVolumeControlEnabled = r.Next(2) == 0;
                        vm.MicrophoneVolume = r.NextDouble() * 100;
                        break;
                }
            }

            // Many producers, one UI thread: every step is posted, like GlobalHotkeyService and AudioEndpointGroup do.
            var producers = Enumerable.Range(0, 8).Select(t => Task.Run(() =>
            {
                var r = new Random(seed * 100 + t);
                for (var i = 0; i < 400; i++)
                {
                    var stepSeed = r.Next();
                    Dispatcher.UIThread.Post(() => Step(new Random(stepSeed)));
                }
            })).ToArray();
            while (!producers.All(p => p.IsCompleted))
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Yield();
            }

            await Task.WhenAll(producers);
            Dispatcher.UIThread.RunJobs();

            // Settle: push-to-talk with every key released is muted.
            harness.Devices.Microphone.IsConnected = true;
            vm.CustomHotkeysEnabled = false;
            vm.MuteMode = MuteMode.PushToTalk;
            harness.Hotkeys.Trigger(settings.Microphone.Hotkey, true);
            harness.Hotkeys.Trigger(settings.Microphone.Hotkey, false);
            Dispatcher.UIThread.RunJobs();
            Assert.True(harness.Devices.Microphone.Mute);
            Assert.True(harness.Settings.Flush());
            SettingsStore.Deserialize(File.ReadAllText(harness.SettingsFile));
        }
        finally
        {
            Dispatcher.UIThread.UnhandledException -= OnUnhandled;
            overlay.Close();
            window.Close();
        }

        Assert.Empty(errors.Select(e => e.ToString()));
    }

    public static TheoryData<string> HostileSettings() =>
    [
        """{"setupCompleted":true,"notifications":{"volume":1e39}}""",
        """{"setupCompleted":true,"notifications":{"volume":-5}}""",
        """{"setupCompleted":true,"microphone":{"volumeControlEnabled":true,"volume":1e39}}""",
        """{"setupCompleted":true,"microphone":{"volumeControlEnabled":true,"volume":-3}}""",
        """{"setupCompleted":true,"microphone":{"muteMode":99}}""",
        """{"setupCompleted":true,"microphone":{"initialState":-1}}""",
        """{"setupCompleted":true,"overlay":{"visibility":42,"lastVisibleMode":42,"corner":7}}""",
        """{"setupCompleted":true,"overlay":{"opacity":-1,"scale":0}}""",
        """{"setupCompleted":true,"overlay":{"scale":1e300,"opacity":1e39}}""",
        """{"setupCompleted":true,"overlay":{"bounds":{"left":1e300,"top":-1e300,"width":-10,"height":1e300}}}""",
        """{"setupCompleted":true,"overlay":{"monitor":{"left":0,"top":0,"width":0,"height":0}}}""",
        """{"setupCompleted":true,"window":{"bounds":{"left":10,"top":10,"width":-800,"height":-600}}}""",
        """{"setupCompleted":true,"window":{"bounds":{"left":1e300,"top":1e300,"width":1e300,"height":1e300}}}""",
        """{"setupCompleted":true,"window":{"theme":12,"appMode":"window"}}""",
        """{"setupCompleted":true,"logLevel":77}""",
        """{"setupCompleted":true,"customHotkeysEnabled":true,"customHotkeys":[{"action":1234,"hotkey":{"key":"F1"}}]}""",
    ];

    [AvaloniaTheory]
    [MemberData(nameof(HostileSettings))]
    public void HandEditedOutOfRangeSettings_OpenAndSave(string json)
    {
        using var harness = new TestHarness(settingsJson: json);
        var vm = harness.ViewModel;
        var errors = new List<Exception>();
        void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            errors.Add(e.Exception);
            e.Handled = true;
        }

        Dispatcher.UIThread.UnhandledException += OnUnhandled;
        try
        {
            var window = new MainWindow(vm, harness.Settings);
            var overlay = new OverlayWindow(vm, harness.Settings, new FakeWindowInterop());
            window.Show();
            overlay.Show();
            foreach (var page in Enum.GetValues<ViewModels.Page>())
            {
                vm.Navigate(page);
                Dispatcher.UIThread.RunJobs();
            }

            vm.ToggleMuteCommand.Execute(null);
            harness.Hotkeys.Trigger(harness.Settings.Current.Microphone.Hotkey, true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(harness.Settings.Flush(), harness.Settings.LastSaveError?.ToString());
            overlay.Close();
            window.Close();
        }
        finally
        {
            Dispatcher.UIThread.UnhandledException -= OnUnhandled;
        }

        Assert.Empty(errors.Select(e => e.ToString()));
    }

    public static TheoryData<string> BadIcons() => ["garbage", "empty", "truncated", "bomb", "directory", "zero-size"];

    [AvaloniaTheory]
    [MemberData(nameof(BadIcons))]
    public void CustomIcon_BadFile_UsesTheDefaultIcon(string kind)
    {
        // A huge image used to throw Skia's plain "Unable to allocate pixels" Exception out of the view model: no start.
        var path = Path.Combine(Path.GetTempPath(), $"miccontrol-icon-{Guid.NewGuid():N}.png");
        switch (kind)
        {
            case "garbage": File.WriteAllText(path, "this is not an image"); break;
            case "empty": File.WriteAllBytes(path, []); break;
            case "truncated": File.WriteAllBytes(path, ImageSizeTests.OnePixelPng[..30]); break;
            case "bomb": File.WriteAllBytes(path, ImageSizeTests.PngClaiming(40000, 40000)); break;
            case "directory": Directory.CreateDirectory(path); break;
            default: File.WriteAllBytes(path, ImageSizeTests.PngClaiming(0, 0)); break;
        }

        try
        {
            using var defaults = new TestHarness();
            using var harness = new TestHarness(s => s.Overlay.MutedIconPath = s.Overlay.UnmutedIconPath = path);
            var vm = harness.ViewModel;
            Assert.Same(defaults.ViewModel.MutedIcon, vm.MutedIcon);

            harness.Dialogs.PickedFile = path;
            vm.SelectMutedIconCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("That file isn't an image MicControl can read.", vm.StatusMessage);
        }
        finally
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path);
            }
            else
            {
                File.Delete(path);
            }
        }
    }

    [AvaloniaFact]
    public void HotkeysAndDeviceEvents_QueuedBehindDispose_DoNotThrow()
    {
        var errors = new List<Exception>();
        void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            errors.Add(e.Exception);
            e.Handled = true;
        }

        Dispatcher.UIThread.UnhandledException += OnUnhandled;
        try
        {
            var harness = new TestHarness(s =>
            {
                s.Microphone.Hotkey.Key = HotkeyGesture.Parse("F13");
                s.CustomHotkeysEnabled = true;
                s.CustomHotkeys.Add(new CustomHotkey { Action = HotkeyAction.SpeakerVolumeUp, Hotkey = new HotkeySettings { Key = HotkeyGesture.Parse("F14") } });
            });
            var main = harness.Settings.Current.Microphone.Hotkey;
            harness.Hotkeys.Trigger(harness.Settings.Current.CustomHotkeys[0].Hotkey, true); // starts the volume repeat timer

            // What the hook and Core Audio do on exit: events are posted, then the app shuts down before they run.
            for (var i = 0; i < 50; i++)
            {
                var pressed = i % 2 == 0;
                Dispatcher.UIThread.Post(() => harness.Hotkeys.Trigger(main, pressed));
                Dispatcher.UIThread.Post(harness.Devices.RaiseDevicesChanged);
                Dispatcher.UIThread.Post(harness.Devices.Microphone.Rebind);
            }

            harness.Dispose();
            Dispatcher.UIThread.RunJobs();
            harness.Settings.Flush();
        }
        finally
        {
            Dispatcher.UIThread.UnhandledException -= OnUnhandled;
        }

        Assert.Empty(errors.Select(e => e.ToString()));
    }

    [AvaloniaFact]
    public void PushToTalk_HeldOnAMouseButton_WhileHotkeysChange_EndsMuted()
    {
        // The new registration never saw the press, so it ignored the release, and a mouse button doesn't repeat its
        // press: push-to-talk stayed live after letting go.
        using var harness = new TestHarness(s =>
        {
            s.Microphone.Hotkey.Key = HotkeyGesture.Parse("MouseXButton1");
            s.CustomHotkeys.Add(new CustomHotkey { Action = HotkeyAction.PushToTalk, Hotkey = new HotkeySettings { Key = HotkeyGesture.Parse("MouseXButton2") } });
        });
        var vm = harness.ViewModel;
        var main = harness.Settings.Current.Microphone.Hotkey;
        harness.Hotkeys.Trigger(main, true);
        Assert.False(harness.Devices.Microphone.Mute);
        vm.CustomHotkeysEnabled = true; // About → Advanced clicked while holding the button
        Assert.True(harness.Devices.Microphone.Mute);
        harness.Hotkeys.Trigger(main, false);
        Assert.True(harness.Devices.Microphone.Mute);

        var custom = harness.Settings.Current.CustomHotkeys[0].Hotkey;
        harness.Hotkeys.Trigger(custom, true);
        Assert.False(harness.Devices.Microphone.Mute);
        vm.CustomHotkeys[0].IsEnabled = false; // the row switched off while held
        harness.Hotkeys.Trigger(custom, false);
        Assert.True(harness.Devices.Microphone.Mute);
    }

    [AvaloniaFact]
    public void SettingsFolderCantBeWritten_PickIconAndOpenLogs_ShowAMessage()
    {
        var errors = new List<Exception>();
        void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            errors.Add(e.Exception);
            e.Handled = true;
        }

        Dispatcher.UIThread.UnhandledException += OnUnhandled;
        try
        {
            using var harness = new TestHarness();
            var vm = harness.ViewModel;
            var folder = Path.GetDirectoryName(harness.SettingsFile)!;
            File.WriteAllText(Path.Combine(folder, "Icons"), "a file where the folder goes");
            File.WriteAllText(Path.Combine(folder, "logs"), "a file where the folder goes");
            var icon = Path.Combine(folder, "picked.png");
            File.WriteAllBytes(icon, ImageSizeTests.OnePixelPng);
            harness.Dialogs.PickedFile = icon;

            vm.SelectMutedIconCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.StartsWith("Couldn't use that image:", vm.StatusMessage, StringComparison.Ordinal);
            Assert.Null(harness.Settings.Current.Overlay.MutedIconPath);

            vm.OpenLogsFolderCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.StartsWith("Couldn't open the logs folder:", vm.StatusMessage, StringComparison.Ordinal);
            Assert.Empty(harness.Dialogs.Opened);
        }
        finally
        {
            Dispatcher.UIThread.UnhandledException -= OnUnhandled;
        }

        Assert.Empty(errors.Select(e => e.ToString()));
    }

    [AvaloniaFact]
    public void CustomHotkeys_CappedOnLoadAndOnAdd()
    {
        // 1,000 rows took 33 s to show headlessly; 5,000 long enough for the UI watchdog to restart the app.
        using var harness = new TestHarness(s =>
        {
            for (var i = 0; i < AppSettings.MaxCustomHotkeys + 50; i++)
            {
                s.CustomHotkeys.Add(new CustomHotkey { Action = HotkeyAction.ToggleMute, Hotkey = new HotkeySettings { Key = HotkeyGesture.Parse($"Ctrl+F{i % 24 + 1}") } });
            }
        });
        var vm = harness.ViewModel;
        Assert.Equal(AppSettings.MaxCustomHotkeys, vm.CustomHotkeys.Count);
        Assert.False(vm.CanAddCustomHotkey);
        Assert.False(vm.AddCustomHotkeyCommand.CanExecute(null));

        vm.CustomHotkeys[0].RemoveCommand.Execute(null);
        Assert.True(vm.AddCustomHotkeyCommand.CanExecute(null));
        vm.AddCustomHotkeyCommand.Execute(null);
        Assert.Equal(AppSettings.MaxCustomHotkeys, harness.Settings.Current.CustomHotkeys.Count);
        Assert.False(vm.AddCustomHotkeyCommand.CanExecute(null));
    }
}
