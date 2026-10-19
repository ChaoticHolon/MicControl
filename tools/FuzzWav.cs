// Fuzzes the notification sound player's decoding path with damaged WAV files: WaveFileReader → SampleChannel, as
// WasapiSoundPlayer.Play reads them, failing on any exception IsUnplayable doesn't catch and on any file that hangs.
// Also lists Avalonia keys with no virtual key (a hotkey on one would never fire). Runs on Windows and Linux.
// Run from the repository root:  dotnet run tools/FuzzWav.cs [cases] [seed]   (tools/Fuzz.ps1 runs it too)
// Failing files are written to artifacts/fuzz/ so they can be replayed.
#:project ../Sources/MicControl.Windows/MicControl.Windows.csproj
#:project ../Sources/MicControl.UI/MicControl.UI.csproj
#:property TargetFramework=net10.0-windows10.0.19041.0
#:property EnableWindowsTargeting=true
#:property PublishAot=false

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

var seedFolder = Path.Combine("Sources", "MicControl.UI", "Assets", "Notifications");
var seeds = Directory.GetFiles(seedFolder, "*.wav").Select(File.ReadAllBytes).ToArray();
var n = args.Length > 0 ? int.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture) : 20000;
var seed = args.Length > 1 ? int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : Random.Shared.Next();
var r = new Random(seed);
Console.WriteLine($"WAV fuzz: {n} cases, seed {seed}");
var failures = new Dictionary<string, (string Message, byte[] Data)>();
var hangs = 0;

// Play() in WasapiSoundPlayer: WaveFileReader → SampleChannel → WasapiOut pulls samples. Only these are caught:
var filter = typeof(MicControl.Windows.WasapiSoundPlayer).GetMethod("IsUnplayable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
bool Caught(Exception ex) => (bool)filter.Invoke(null, [ex])!;

byte[] Mutate(byte[] seed)
{
    var b = (byte[])seed.Clone();
    switch (r.Next(6))
    {
        case 0: for (var i = r.Next(1, 8); i > 0; i--) b[r.Next(b.Length)] = (byte)r.Next(256); break;
        case 1: Array.Resize(ref b, r.Next(b.Length)); break;
        case 2:
            // Hostile 32-bit field anywhere in the first 64 bytes (chunk sizes, rates, block align).
            var at = r.Next(0, 60);
            var v = new uint[] { 0, 1, 0xFFFFFFFF, 0x7FFFFFFF, 0x80000000, 0xFFFFFFF0, 2, 3 }[r.Next(8)];
            BitConverter.GetBytes(v).CopyTo(b, at);
            break;
        case 3:
            // A 16-bit header field (format tag, channels, bits, block align, extra size).
            var at16 = new[] { 20, 22, 32, 34, 36 }[r.Next(5)];
            BitConverter.GetBytes((ushort)new[] { 0, 1, 2, 3, 6, 7, 0x11, 0xFFFE, 0xFFFF, 255, 64, 24, 32 }[r.Next(13)]).CopyTo(b, at16);
            break;
        case 4:
            // An extra chunk with a hostile size before "data".
            var extra = new byte[8 + r.Next(0, 16)];
            "junk"u8.CopyTo(extra);
            BitConverter.GetBytes(new uint[] { 0xFFFFFFFF, 0x7FFFFFFF, 0x80000000, 0, 0xFFFFFFF8 }[r.Next(5)]).CopyTo(extra, 4);
            b = [.. b[..36], .. extra, .. b[36..]];
            break;
        default:
            BitConverter.GetBytes(new uint[] { 0, 0xFFFFFFFF, 0x7FFFFFFF, (uint)b.Length }[r.Next(4)]).CopyTo(b, 4); // RIFF size
            break;
    }

    return b;
}

for (var i = 0; i < n; i++)
{
    var data = Mutate(seeds[r.Next(seeds.Length)]);
    var task = Task.Run(() =>
    {
        using var reader = new WaveFileReader(new MemoryStream(data));
        var samples = new SampleChannel(reader) { Volume = 1 };
        var buffer = new float[4800];
        long total = 0;
        int read;
        while ((read = samples.Read(buffer, 0, buffer.Length)) > 0 && total < 10_000_000)
        {
            total += read;
        }
    });
    bool finished;
    try { finished = task.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { finished = true; }
    if (!finished)
    {
        hangs++;
        failures.TryAdd("HANG", ("did not finish in 5 s", data));
        continue;
    }
    if (task.Exception?.InnerException is { } ex && !Caught(ex))
    {
        var key = $"{ex.GetType().Name} @ {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}";
        failures.TryAdd(key, (ex.Message, data));
    }
}

Console.WriteLine($"WAV cases: {n}, hangs: {hangs}, uncaught kinds: {failures.Count}");
foreach (var (k, (m, d)) in failures)
{
    Console.WriteLine($"- {k}\n    {m}\n    header: {Convert.ToHexString(d.AsSpan(0, Math.Min(64, d.Length)))}");
    Directory.CreateDirectory(Path.Combine("artifacts", "fuzz"));
    File.WriteAllBytes(Path.Combine("artifacts", "fuzz", $"wav-{seed}-{failures.Keys.ToList().IndexOf(k)}.wav"), d);
}

// Every key Avalonia's HotkeyBox can produce (Key.ToString()) must map to a virtual key, or that hotkey never fires.
var vk = typeof(MicControl.Windows.GlobalHotkeyService).Assembly.GetType("MicControl.Windows.VirtualKeys")!.GetMethod("FromKeyName")!;
// These have no Windows virtual key at all (Avalonia keeps them for other platforms and TV remotes), so Windows never sends them.
string[] noWindowsKey = ["LineFeed", "System", "DeadCharProcessed", "FnLeftArrow", "FnRightArrow", "FnUpArrow", "FnDownArrow", "MediaHome",
    "MediaChannelList", "MediaChannelRaise", "MediaChannelLower", "MediaRecord", "MediaRed", "MediaGreen", "MediaYellow", "MediaBlue",
    "MediaMenu", "MediaMore", "MediaOption", "MediaInfo", "MediaSearch", "MediaSubtitle", "MediaTvGuide", "MediaPreviousChannel"];
var unmapped = Enum.GetValues<Avalonia.Input.Key>().Distinct().Select(k => k.ToString()).Distinct()
    .Where(name => (int)vk.Invoke(null, [name])! == 0 && name != "None" && !noWindowsKey.Contains(name)).ToList();
Console.WriteLine($"Avalonia keys with no virtual key: {unmapped.Count}\n  {string.Join(", ", unmapped)}");
return failures.Count == 0 && unmapped.Count == 0 ? 0 : 1;
