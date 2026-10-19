using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MicControl.Hotkeys;
using MicControl.Installation;
using MicControl.Platform;
using MicControl.Settings;
using MicControl.Tests.Ui;

namespace MicControl.Tests.Fuzz;

/// <summary>
/// Fuzzing beyond single files: image headers, command lines, autostart entries taken over from MicSwitch, settings
/// files built to be huge or deep, and the hotkey state machine (push-to-talk must always end muted).
/// </summary>
public sealed class StateFuzzTests(ITestOutputHelper output)
{
    [Fact]
    public void ImageSize_MutatedHeaders_NeverThrowOrHang()
    {
        FuzzRun.Require();
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==");
        byte[] gif = [.. "GIF89a"u8, 1, 0, 1, 0, 0, 0, 0];
        var bmp = new byte[54];
        bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, .. new byte[14], 0xFF, 0xC0, 0, 17, 8, 0, 1, 0, 1, 3, .. new byte[9]];
        byte[] ico = [0, 0, 1, 0, 2, 0, .. new byte[12], 22 + 16, 0, 0, 0, .. new byte[12], 0, 0, 0, 0, .. png];
        var seeds = new[] { png, gif, bmp, jpeg, ico };
        var r = FuzzRun.Random(31);
        var failures = new Dictionary<string, string>();
        for (var i = 0; i < FuzzRun.Scale(200_000); i++)
        {
            var b = (byte[])seeds[r.Next(seeds.Length)].Clone();
            for (var n = r.Next(1, 6); n > 0; n--)
            {
                switch (r.Next(3))
                {
                    case 0: b[r.Next(b.Length)] = (byte)r.Next(256); break;
                    case 1: b[r.Next(b.Length)] = new byte[] { 0, 0xFF, 0x89, 0xC0, 0xD8 }[r.Next(5)]; break;
                    default: Array.Resize(ref b, r.Next(b.Length + 1)); if (b.Length == 0) b = [0]; break;
                }
            }

            try
            {
                MicControl.Imaging.ImageSize.IsReasonable(b);
            }
            catch (Exception ex)
            {
                failures.TryAdd(ex.GetType().Name + ex.StackTrace?.Split('\n').FirstOrDefault(), Convert.ToHexString(b.AsSpan(0, Math.Min(b.Length, 80))));
            }
        }

        FuzzRun.AssertNone(failures);
    }

    [Fact]
    public void CommandLines_RandomArguments_ParseAndRoundTrip()
    {
        FuzzRun.Require();
        var r = FuzzRun.Random(21);
        var words = new[] { "--install", "--uninstall", "--silent", "--relaunch", "--scope=all", "--scope=user", "--scope=", "--scope=ME", "/install", "/SILENT", "-install", "--", "", " ", "--scope=all=user", "--relaunched-from=12", "\"", "C:\\x y\\MicControl.exe" };
        var paths = new[] { null, "", "MicControl-Setup-1.0.0.exe", "C:\\Downloads\\MicControl-Setup-1.0.0 (1).exe", "/tmp/micControl-setup", "C:\\a\\MicControl.exe", "\0", "C:\\" + new string('a', 300) + "\\MicControl-Setup.exe" };
        var failures = new Dictionary<string, string>();
        for (var i = 0; i < FuzzRun.Scale(20_000); i++)
        {
            var args = Enumerable.Range(0, r.Next(5)).Select(_ => words[r.Next(words.Length)]).ToArray();
            var path = paths[r.Next(paths.Length)];
            try
            {
                var command = SetupCommand.Parse(args, path);
                var again = SetupCommand.Parse(command.ToArguments().Split(' ', StringSplitOptions.RemoveEmptyEntries), "MicControl.exe");
                if (command.Mode != SetupMode.None && again != command)
                {
                    failures.TryAdd("round trip", $"{string.Join(' ', args)} [{path}] -> {command} -> {again}");
                }
            }
            catch (Exception ex)
            {
                failures.TryAdd($"{string.Join(' ', args)} [{path}]: {ex.GetType().Name}", ex.Message);
            }
        }

        var commands = new[] { null, "", "\"", "\"\"", "\" ", "\"C:\\a b\\x.exe\" --x", "C:\\a b\\x.exe --x", "x", "%APPDATA%\\x.exe", "\"%NOPE%\\x.exe\"", ".exe", "\0", new string('"', 1000) };
        foreach (var c in commands)
        {
            try
            {
                AutostartCommand.IsStale(c, _ => false);
                AutostartCommand.GetExecutable(c);
            }
            catch (Exception ex)
            {
                failures.TryAdd($"{c ?? "null"}: {ex.GetType().Name}", ex.Message);
            }
        }

        FuzzRun.AssertNone(failures);
    }

    [Fact]
    public void Autostart_Differential()
    {
        FuzzRun.Require();
        var r = FuzzRun.Random(3);
        var parts = new[] { "Program Files", "Users", "alex.exeter", "Tools.exe.d", "my.exec", "app-1.2.3", "MicSwitch", "A B", "x.EXE files", ".exe", "dot.ex", "e.exe1" };
        var failures = new List<string>();
        for (var i = 0; i < FuzzRun.Scale(20_000); i++)
        {
            var depth = r.Next(1, 5);
            var folders = Enumerable.Range(0, depth).Select(_ => parts[r.Next(parts.Length)]);
            var exe = @"C:\" + string.Join('\\', folders) + @"\MicSwitch.exe";
            var quoted = r.Next(3) == 0;
            var args = r.Next(3) switch { 0 => "", 1 => " --autostart", _ => " /minimized" };
            var command = (quoted ? $"\"{exe}\"" : exe) + args;
            // Unquoted with a space before the real .exe is ambiguous for Windows too.
            if (!quoted && exe.Contains(' ', StringComparison.Ordinal))
            {
                continue;
            }

            var got = AutostartCommand.GetExecutable(command);
            if (got != exe)
            {
                failures.Add($"{command} -> {got}");
            }
        }

        output.WriteLine($"{failures.Count} wrong");
        foreach (var f in failures.Distinct().Take(15))
        {
            output.WriteLine(f);
        }

        FuzzRun.AssertNone(failures.Distinct().Take(15).Select((f, i) => KeyValuePair.Create($"#{i + 1}", f)).ToDictionary());
    }

    public static TheoryData<string> HugeSettings() => ["deep", "manyHotkeys", "longString", "manySounds", "bigNumbers"];

    [Theory]
    [MemberData(nameof(HugeSettings))]
    public void Settings_HugeOrDeep_LoadQuickly(string kind)
    {
        FuzzRun.Require();
        var json = kind switch
        {
            "deep" => "{\"setupCompleted\":true,\"x\":" + new string('[', 100000) + new string(']', 100000) + "}",
            "manyHotkeys" => "{\"setupCompleted\":true,\"customHotkeysEnabled\":true,\"customHotkeys\":["
                + string.Join(',', Enumerable.Range(0, 200000).Select(i => "{\"action\":" + (i % 12) + ",\"hotkey\":{\"key\":\"Ctrl+F" + (i % 24 + 1) + "\"}}")) + "]}",
            "longString" => "{\"setupCompleted\":true,\"microphone\":{\"deviceId\":\"" + new string('a', 50_000_000) + "\"}}",
            "manySounds" => "{\"setupCompleted\":true,\"notifications\":{\"customSoundsEnabled\":true,\"muteSound\":\"" + string.Concat(Enumerable.Repeat("..\\\\", 100000)) + "x.wav\"}}",
            _ => "{\"setupCompleted\":true,\"version\":1e308,\"logLevel\":99999999999999999999,\"window\":{\"bounds\":{\"left\":-1e308,\"top\":1e-320,\"width\":5e-324,\"height\":1e308}}}",
        };
        var folder = Directory.CreateTempSubdirectory("miccontrol-r3-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "settings.json"), json);
            var watch = Stopwatch.StartNew();
            var store = new SettingsStore(folder, Path.Combine(folder, "none"));
            var loaded = store.Load();
            output.WriteLine($"{kind}: {watch.ElapsedMilliseconds} ms, error {store.LoadError?.GetType().Name}, hotkeys {loaded.CustomHotkeys.Count}");
            store.Save(loaded);
            output.WriteLine($"{kind}: saved after {watch.ElapsedMilliseconds} ms");
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [AvaloniaFact]
    public void HotkeyStateMachine_Fuzz()
    {
        FuzzRun.Require();
        var failures = new List<string>();
        for (var seed = 0; seed < FuzzRun.Scale(300); seed++)
        {
            var r = FuzzRun.Random(1000 + seed);
            using var harness = new TestHarness(s =>
            {
                s.Microphone.Hotkey.Key = HotkeyGesture.Parse("MouseXButton1");
                s.Microphone.Hotkey.AlternativeKey = HotkeyGesture.Parse("F13");
                s.CustomHotkeysEnabled = true;
                s.CustomHotkeys.Add(new CustomHotkey { Action = HotkeyAction.PushToTalk, Hotkey = new HotkeySettings { Key = HotkeyGesture.Parse("MouseXButton2") } });
                s.CustomHotkeys.Add(new CustomHotkey { Action = HotkeyAction.SpeakerVolumeUp, Hotkey = new HotkeySettings { Key = HotkeyGesture.Parse("MouseMiddle") } });
            });
            var vm = harness.ViewModel;
            var settings = harness.Settings.Current;
            var keys = new[] { settings.Microphone.Hotkey, settings.CustomHotkeys[0].Hotkey, settings.CustomHotkeys[1].Hotkey };
            var trace = new List<string>();
            for (var step = 0; step < 40; step++)
            {
                switch (r.Next(6))
                {
                    case < 3:
                        var k = r.Next(keys.Length);
                        var p = r.Next(2) == 0;
                        trace.Add($"key{k}:{p}");
                        harness.Hotkeys.Trigger(keys[k], p);
                        break;
                    case 3:
                        trace.Add("customToggle");
                        vm.CustomHotkeysEnabled = !vm.CustomHotkeysEnabled;
                        break;
                    case 4:
                        trace.Add("rowToggle");
                        var row = vm.CustomHotkeys[r.Next(vm.CustomHotkeys.Count)];
                        row.IsEnabled = !row.IsEnabled;
                        break;
                    default:
                        // A click on Unmute would make the mic live on purpose, so only the mode changes here.
                        trace.Add("mode");
                        vm.MuteMode = r.Next(2) == 0 ? MuteMode.PushToTalk : MuteMode.ToggleMute;
                        break;
                }

                Dispatcher.UIThread.RunJobs();
            }

            // Release everything; in push-to-talk the mic must end muted and the volume must stop moving.
            vm.MuteMode = MuteMode.PushToTalk;
            foreach (var key in keys)
            {
                harness.Hotkeys.Trigger(key, false);
            }

            Dispatcher.UIThread.RunJobs();
            var volume = harness.Devices.Speaker.Volume;
            Thread.Sleep(150);
            Dispatcher.UIThread.RunJobs();
            if (harness.Devices.Microphone.Mute != true || harness.Devices.Speaker.Volume != volume)
            {
                failures.Add($"seed {seed}: mute {harness.Devices.Microphone.Mute}, volume {volume}->{harness.Devices.Speaker.Volume}: {string.Join(' ', trace)}");
            }
        }

        output.WriteLine($"{failures.Count} failing seeds");
        foreach (var f in failures.Take(5))
        {
            output.WriteLine(f);
        }

        FuzzRun.AssertNone(failures.Distinct().Take(15).Select((f, i) => KeyValuePair.Create($"#{i + 1}", f)).ToDictionary());
    }
}
