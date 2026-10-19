using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MicControl.Hotkeys;
using MicControl.Security;
using MicControl.Settings;
using MicControl.Updates;

namespace MicControl.Tests.Fuzz;

/// <summary>Mutation fuzzing of the parsers that read files and downloads: settings, MicSwitch configs, hotkey text, signatures, the update feed.</summary>
public sealed class ParserFuzzTests : IDisposable
{
    private static readonly int Iterations = FuzzRun.Scale(20_000);
    private readonly string directory = Directory.CreateTempSubdirectory("miccontrol-fuzz").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private static readonly string[] Tokens =
    [
        "null", "true", "false", "0", "-1", "1e308", "-1e308", "1e39", "3.4e38", "1e-400", "99999999999999999999", "2147483648",
        "\"\"", "\"x\"", "\"NaN\"", "\"Infinity\"", "\"-Infinity\"", "[]", "{}", "[null]", "[1,2]", "{\"a\":1}", "\"Ctrl+\"", "\"++\"",
        "\"Mouse\"", "\"MouseXButton9\"", "\"tray\"", "\"window\"", "\"Toggle\"", "\"PushToTalk\"", "999", "-2147483649", "\"\\u0000\"",
        "1.5", "NaN", "\"Ctrl+Alt+Shift+Win+F24\"",
    ];

    private static string Rich()
    {
        var s = new AppSettings();
        s.Microphone.Hotkey.Key = HotkeyGesture.Parse("Ctrl+F2");
        s.Microphone.Volume = 0.5f;
        s.CustomHotkeys.Add(new CustomHotkey { Action = HotkeyAction.SpeakerVolumeUp, Hotkey = new HotkeySettings { Key = HotkeyGesture.Parse("MouseXButton1") } });
        s.Overlay.Bounds = new WindowBounds(1, 2, 3, 4);
        s.Overlay.Monitor = new WindowBounds(0, 0, 1920, 1080);
        s.Window.Bounds = new WindowBounds(10, 10, 800, 600);
        s.Notifications.OutputDeviceId = "dev";
        return SettingsStore.Serialize(s);
    }

    // Mutates JSON both structurally (replace a value with a hostile token) and textually (byte noise, truncation).
    private static string Mutate(Random r, string json)
    {
        switch (r.Next(7))
        {
            case 0:
            case 1:
            case 2:
                {
                    var node = JsonNode.Parse(json)!;
                    for (var n = r.Next(1, 4); n > 0; n--)
                    {
                        ReplaceRandom(r, node);
                    }

                    return node.ToJsonString();
                }
            case 3:
                {
                    var chars = json.ToCharArray();
                    for (var n = r.Next(1, 5); n > 0; n--)
                    {
                        chars[r.Next(chars.Length)] = (char)r.Next(0, 128);
                    }

                    return new string(chars);
                }
            case 4:
                return json[..r.Next(json.Length)];
            case 5:
                {
                    // A lone surrogate escape in a random string: valid JSON syntax, invalid text.
                    var strings = System.Text.RegularExpressions.Regex.Matches(json, "\"[^\"\\\\]*\"");
                    var m = strings[r.Next(strings.Count)];
                    return json[..m.Index] + "\"\\uD800x\"" + json[(m.Index + m.Length)..];
                }
            default:
                var depth = r.Next(1, 200);
                return new string('[', depth) + json + new string(']', depth);
        }
    }

    private static void ReplaceRandom(Random r, JsonNode root)
    {
        var all = new List<(JsonNode Parent, object Key)>();
        void Walk(JsonNode? n)
        {
            switch (n)
            {
                case JsonObject o:
                    foreach (var p in o.ToList())
                    {
                        all.Add((o, p.Key));
                        Walk(p.Value);
                    }

                    break;
                case JsonArray a:
                    for (var i = 0; i < a.Count; i++)
                    {
                        all.Add((a, i));
                        Walk(a[i]);
                    }

                    break;
            }
        }

        Walk(root);
        if (all.Count == 0)
        {
            return;
        }

        var (parent, key) = all[r.Next(all.Count)];
        var replacement = JsonNode.Parse(Tokens[r.Next(Tokens.Length)].Replace("NaN", "\"NaN\"", StringComparison.Ordinal).Replace("\"\"NaN\"\"", "\"NaN\"", StringComparison.Ordinal));
        if (parent is JsonObject obj)
        {
            obj[(string)key] = replacement;
        }
        else
        {
            ((JsonArray)parent)[(int)key] = replacement;
        }
    }

    private static void Record(Dictionary<string, string> failures, Exception ex, string input)
    {
        var key = $"{ex.GetType().Name} at {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}";
        failures.TryAdd(key, $"{ex.Message}\n    input: {(input.Length > 400 ? input[..400] + "…" : input)}");
    }

    /// <summary>What a loaded settings object must look like for the app to run with it.</summary>
    private static string? Invariants(AppSettings s)
    {
        if (s.Microphone is null || s.Microphone.Hotkey?.Key is null || s.Notifications is null || s.Overlay is null || s.Window is null
            || s.CustomHotkeys is null || s.CustomHotkeys.Any(h => h?.Hotkey?.Key is null || h.Hotkey.AlternativeKey is null))
        {
            return "null section";
        }

        if (!Enum.IsDefined(s.Microphone.MuteMode) || !Enum.IsDefined(s.Microphone.InitialState) || !Enum.IsDefined(s.Overlay.Visibility)
            || !Enum.IsDefined(s.Overlay.LastVisibleMode) || !Enum.IsDefined(s.Overlay.Corner) || !Enum.IsDefined(s.Window.AppMode)
            || !Enum.IsDefined(s.Window.Theme) || !Enum.IsDefined(s.LogLevel) || s.CustomHotkeys.Any(h => !Enum.IsDefined(h.Action)))
        {
            return "undefined enum value";
        }

        if (s.Microphone.Volume is { } v && (!float.IsFinite(v) || v < 0 || v > 1) || !float.IsFinite(s.Notifications.Volume)
            || s.Notifications.Volume < 0 || s.Notifications.Volume > 1)
        {
            return $"volume out of range (mic {s.Microphone.Volume}, sounds {s.Notifications.Volume})";
        }

        if (!double.IsFinite(s.Overlay.Opacity) || s.Overlay.Opacity is < 0 or > 1 || !double.IsFinite(s.Overlay.Scale) || s.Overlay.Scale <= 0)
        {
            return $"overlay opacity/scale out of range ({s.Overlay.Opacity}, {s.Overlay.Scale})";
        }

        foreach (var b in new[] { s.Overlay.Bounds, s.Overlay.Monitor, s.Window.Bounds })
        {
            if (b is not null && (!double.IsFinite(b.Left) || !double.IsFinite(b.Top) || !double.IsFinite(b.Width) || !double.IsFinite(b.Height) || b.Width < 0 || b.Height < 0))
            {
                return $"bounds out of range {b}";
            }
        }

        return null;
    }

    [Fact]
    public void Settings_MutatedJson_LoadsOrIsPreserved()
    {
        FuzzRun.Require();
        var r = FuzzRun.Random(1);
        var seeds = new[] { SettingsStore.Serialize(new AppSettings()), Rich() };
        var crashes = new Dictionary<string, string>();
        var invalid = new Dictionary<string, string>();
        var store = new SettingsStore(directory, Path.Combine(directory, "no-legacy"));
        for (var i = 0; i < Iterations; i++)
        {
            var json = Mutate(r, seeds[r.Next(seeds.Length)]);
            try
            {
                File.WriteAllText(store.FilePath, json);
                var loaded = store.Load();
                if (Invariants(loaded) is { } problem)
                {
                    invalid.TryAdd(problem, json);
                }

                // Whatever loads must save and load again.
                SettingsStore.Deserialize(SettingsStore.Serialize(loaded));
            }
            catch (Exception ex)
            {
                Record(crashes, ex, json);
            }
        }

        FuzzRun.AssertNone(crashes.Concat(invalid.Select(p => KeyValuePair.Create("INVARIANT: " + p.Key, p.Value))).ToDictionary());
    }

    [Fact]
    public void Settings_LockedOrUnreadableFile_DoesNotThrow()
    {
        FuzzRun.Require();
        var store = new SettingsStore(directory, Path.Combine(directory, "no-legacy"));
        File.WriteAllText(store.FilePath, "{}");
        var crashes = new Dictionary<string, string>();
        using (new FileStream(store.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            try
            {
                store.Load();
            }
            catch (Exception ex)
            {
                Record(crashes, ex, "settings.json opened exclusively by another process");
            }
        }

        // A directory where the file should be.
        File.Delete(store.FilePath);
        Directory.CreateDirectory(store.FilePath);
        try
        {
            store.Load();
        }
        catch (Exception ex)
        {
            Record(crashes, ex, "settings.json is a directory");
        }

        Directory.Delete(store.FilePath);
        FuzzRun.AssertNone(crashes);
    }

    private static string LegacySeed() => """
        {"Items":[
          {"TypeName":"MicSwitch.MainWindow.Models.MicSwitchConfig, MicSwitch","ConfigValue":{
            "MicrophoneLineId":{"LineId":"{0.0.1.00000000}.{abc}"},"VolumeControlEnabled":true,"Volume":0.4,
            "MicrophoneHotkey":"Ctrl+F1","MicrophoneHotkeyAlt":"MouseXButton1","SuppressHotkey":false,
            "Notifications":{"On":"Beep300","Off":"custom.wav"},"NotificationVolume":0.7,
            "MainWindowBounds":"10, 20, 800, 600","OverlayBounds":{"X":5,"Y":6,"Width":100,"Height":50},
            "OverlayOpacity":0.5,"OverlayVisibilityMode":"Always","OverlayEnabled":true,"MutedMicrophoneIcon":"AAEC","MicrophoneIcon":"AAEC"}},
          {"TypeName":"MicSwitch.Modularity.MicSwitchHotkeyConfig","ConfigValue":{
            "Hotkey":{"Key":"F13","AlternativeKey":"None","Suppress":true},"EnableAdvancedHotkeys":true,
            "HotkeyForToggle":{"Key":"Ctrl+T"},"HotkeyForMute":{"Key":"Ctrl+M"},"HotkeyForUnmute":{"Key":"Ctrl+U"},
            "HotkeyForPushToTalk":{"Key":"MouseMiddle"},"HotkeyForPushToMute":{"Key":"Alt+P"}}},
          {"TypeName":"MicSwitchOverlayConfig","ConfigValue":{"OverlayVisibilityMode":2,"OverlayBounds":"1;2;3;4"}},
          {"TypeName":"MicSwitchVolumeControlConfig","ConfigValue":{"IsEnabled":true,"DeviceId":{"LineId":"out"},
            "HotkeyForToggle":{"Key":"Ctrl+S"},"HotkeyForVolumeUp":{"Key":"WheelUp"}}}
        ]}
        """;

    [Fact]
    public void Legacy_MutatedConfig_ImportsOrIsSkipped()
    {
        FuzzRun.Require();
        var r = FuzzRun.Random(2);
        var seed = LegacySeed();
        var crashes = new Dictionary<string, string>();
        var invalid = new Dictionary<string, string>();
        var legacy = Path.Combine(directory, "MicSwitch");
        Directory.CreateDirectory(Path.Combine(legacy, "release"));
        var configPath = Path.Combine(legacy, "release", "config.cfg");
        var icons = Path.Combine(directory, "Icons");
        var intact = 0;
        for (var i = 0; i < Iterations / 4; i++)
        {
            var json = Mutate(r, seed);
            try
            {
                File.WriteAllText(configPath, json, r.Next(2) == 0 ? Encoding.Unicode : Encoding.UTF8);
                var migrated = LegacyConfigMigrator.TryMigrate(legacy, icons);
                if (migrated is not null)
                {
                    intact++;
                    if (Invariants(migrated.Normalize()) is { } problem)
                    {
                        invalid.TryAdd(problem, json);
                    }

                    SettingsStore.Deserialize(SettingsStore.Serialize(migrated));
                }
            }
            catch (Exception ex)
            {
                Record(crashes, ex, json);
            }
        }

        Assert.True(intact > 0);
        FuzzRun.AssertNone(crashes.Concat(invalid.Select(p => KeyValuePair.Create("INVARIANT: " + p.Key, p.Value))).ToDictionary());
    }

    [Fact]
    public void Legacy_OneMalformedEntry_KeepsTheRest()
    {
        FuzzRun.Require();
        // MicSwitch configs edited by hand: one broken item or field shouldn't lose the whole import.
        var cases = new Dictionary<string, string>
        {
            ["non-object item"] = """{"Items":[1,{"TypeName":"MicSwitchHotkeyConfig","ConfigValue":{"Hotkey":{"Key":"F13"}}}]}""",
            ["string LineId parent"] = """{"Items":[{"TypeName":"MicSwitchConfig","ConfigValue":{"MicrophoneLineId":"x"}},{"TypeName":"MicSwitchHotkeyConfig","ConfigValue":{"Hotkey":{"Key":"F13"}}}]}""",
            ["bad icon"] = """{"Items":[{"TypeName":"MicSwitchConfig","ConfigValue":{"MicrophoneIcon":"!!notbase64"}},{"TypeName":"MicSwitchHotkeyConfig","ConfigValue":{"Hotkey":{"Key":"F13"}}}]}""",
        };
        var failures = new Dictionary<string, string>();
        foreach (var (name, json) in cases)
        {
            try
            {
                var s = LegacyConfigMigrator.Migrate(json, Path.Combine(directory, "Icons"));
                if (s.Microphone.Hotkey.Key != HotkeyGesture.Parse("F13"))
                {
                    failures[name] = "hotkey lost";
                }
            }
            catch (Exception ex)
            {
                failures[name] = $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        FuzzRun.AssertNone(failures);
    }

    private const string HotkeyAlphabet = "+ CtrlAltShiftWinMouseXButton12WheelUpDownNone*-/=`F1Num\t\u00a0\0é😀";

    [Fact]
    public void Hotkey_RandomText_ParsesSafelyAndRoundTrips()
    {
        FuzzRun.Require();
        var r = FuzzRun.Random(3);
        var crashes = new Dictionary<string, string>();
        var words = new[] { "Ctrl", "Alt", "Shift", "Win", "Windows", "Control", "+", "Mouse", "MouseXButton1", "WheelUp", "None", "F1", " ", "Num +", "Num", "*", "0", "Mouse0", "Mouse-1", "MouseNone", "Wheel" };
        for (var i = 0; i < Iterations * 5; i++)
        {
            var sb = new StringBuilder();
            for (var n = r.Next(0, 6); n > 0; n--)
            {
                if (r.Next(2) == 0)
                {
                    sb.Append(words[r.Next(words.Length)]);
                }
                else
                {
                    sb.Append(HotkeyAlphabet[r.Next(HotkeyAlphabet.Length)]);
                }
            }

            var text = sb.ToString();
            try
            {
                if (HotkeyGesture.TryParse(text, out var g))
                {
                    var printed = g.ToString();
                    if (!HotkeyGesture.TryParse(printed, out var again) || again != g)
                    {
                        crashes.TryAdd("round trip " + printed, $"'{text}' -> '{printed}' -> {(again is null ? "fail" : again.ToString())}");
                    }

                    // The settings file keeps hotkeys as text, so every parsed gesture must survive a save.
                    var s = new AppSettings();
                    s.Microphone.Hotkey.Key = g;
                    if (SettingsStore.Deserialize(SettingsStore.Serialize(s)).Microphone.Hotkey.Key != g)
                    {
                        crashes.TryAdd("json round trip " + printed, text);
                    }
                }
            }
            catch (Exception ex)
            {
                Record(crashes, ex, text);
            }
        }

        FuzzRun.AssertNone(crashes);
    }

    [Fact]
    public void Signature_MutatedSshsig_NeverThrowsAndNeverPasses()
    {
        FuzzRun.Require();
        var r = FuzzRun.Random(4);
        var message = Encoding.UTF8.GetBytes(SecurityTests.Sums);
        Assert.True(SshSignature.Verify(message, SecurityTests.GoodSignature, ReleaseFeed.SignatureNamespace, [SecurityTests.ReleaseKey]));
        var lines = SecurityTests.GoodSignature.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var blob = Convert.FromBase64String(string.Concat(lines[1..^1]));
        var failures = new Dictionary<string, string>();
        for (var i = 0; i < Iterations / 10; i++)
        {
            var bytes = (byte[])blob.Clone();
            var mode = r.Next(4);
            switch (mode)
            {
                case 0:
                    bytes[r.Next(bytes.Length)] ^= (byte)(1 << r.Next(8));
                    break;
                case 1:
                    Array.Resize(ref bytes, r.Next(bytes.Length));
                    break;
                case 2:
                    // Hostile length prefixes (0xFFFFFFFF, 0x7FFFFFFF, 0x80000000) at a random offset.
                    var at = r.Next(Math.Max(1, bytes.Length - 4));
                    var len = new[] { 0xFFFFFFFFu, 0x7FFFFFFFu, 0x80000000u, 0u }[r.Next(4)];
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at), len);
                    break;
                default:
                    bytes = [.. bytes, .. new byte[r.Next(1, 64)]];
                    break;
            }

            var armored = "-----BEGIN SSH SIGNATURE-----\n" + Convert.ToBase64String(bytes) + "\n-----END SSH SIGNATURE-----\n";
            try
            {
                if (SshSignature.Verify(message, armored, ReleaseFeed.SignatureNamespace, [SecurityTests.ReleaseKey])
                    && !bytes.AsSpan().SequenceEqual(blob))
                {
                    failures.TryAdd("ACCEPTED MUTATION " + mode, Convert.ToBase64String(bytes));
                }
            }
            catch (Exception ex)
            {
                Record(failures, ex, Convert.ToBase64String(bytes));
            }
        }

        // Garbage armour, keys and namespaces.
        string[] armours = ["", "\n", "-----BEGIN SSH SIGNATURE-----", "-----BEGIN SSH SIGNATURE-----\n\n-----END SSH SIGNATURE-----", "-----BEGIN SSH SIGNATURE-----\n====\n-----END SSH SIGNATURE-----", SecurityTests.GoodSignature.Replace("\n", "\r\n", StringComparison.Ordinal)];
        string[] keys = ["", " ", "ssh-ed25519", "ssh-ed25519 !!!", "ssh-ed25519 AAAA", "ssh-rsa AAAAB3NzaC1yc2E=", SecurityTests.ReleaseKey + " extra words"];
        foreach (var a in armours)
        {
            foreach (var k in keys)
            {
                foreach (var ns in new[] { "", ReleaseFeed.SignatureNamespace, "\0", "git" })
                {
                    try
                    {
                        SshSignature.Verify(message, a, ns, [k]);
                    }
                    catch (Exception ex)
                    {
                        Record(failures, ex, $"{a} | {k} | {ns}");
                    }
                }
            }
        }

        // Raw Ed25519 with random points and scalars.
        for (var i = 0; i < 300; i++)
        {
            var key = new byte[r.Next(30, 34)];
            var sig = new byte[r.Next(62, 66)];
            r.NextBytes(key);
            r.NextBytes(sig);
            if (r.Next(3) == 0)
            {
                Array.Fill(key, (byte)0xFF);
            }

            try
            {
                if (Ed25519.Verify(key, message, sig))
                {
                    failures.TryAdd("ED25519 ACCEPTED RANDOM", Convert.ToBase64String(sig));
                }
            }
            catch (Exception ex)
            {
                Record(failures, ex, Convert.ToBase64String(key) + " " + Convert.ToBase64String(sig));
            }
        }

        FuzzRun.AssertNone(failures);
    }

    private const string FeedSeed = """
        {"tag_name":"v2.1.0","draft":false,"prerelease":false,"assets":[
          {"name":"MicControl-Setup-2.1.0.exe","browser_download_url":"https://example.com/MicControl-Setup-2.1.0.exe"},
          {"name":"SHA512SUMS","browser_download_url":"https://example.com/SHA512SUMS"},
          {"name":"SHA512SUMS.sig","browser_download_url":"https://example.com/SHA512SUMS.sig"}]}
        """;

    [Fact]
    public void ReleaseFeed_MutatedJson_FailsOnlyWithCaughtExceptions()
    {
        FuzzRun.Require();
        // MainViewModel.IsUpdateFailure catches JsonException and InvalidOperationException; anything else crashes the check.
        var r = FuzzRun.Random(5);
        var failures = new Dictionary<string, string>();
        for (var i = 0; i < Iterations; i++)
        {
            var json = Mutate(r, FeedSeed);
            try
            {
                if (ReleaseFeed.Parse(json) is { } release)
                {
                    Assert.StartsWith("https", release.SetupUrl.Scheme, StringComparison.Ordinal);
                }

                ReleaseFeed.FindSha512(json, "MicControl-Setup-2.1.0.exe");
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
            }
            catch (Exception ex)
            {
                Record(failures, ex, json);
            }
        }

        foreach (var tag in new[] { "", "v", "vv1.0.0", "1", "1.2.3.4.5", "-1.0.0", "1.-1.0", "99999999999.0.0", "2147483647.2147483647.2147483647", " v1.0.0 ", "1.0.0-beta" })
        {
            try
            {
                if (ReleaseFeed.TryParseVersion(tag, out var v))
                {
                    _ = $"{ReleaseFeed.SetupFilePrefix}{v.ToString(3)}.exe";
                }
            }
            catch (Exception ex)
            {
                Record(failures, ex, tag);
            }
        }

        FuzzRun.AssertNone(failures);
    }
}
