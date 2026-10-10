// Generates MicControl's default mute/unmute cues ("Relay on" / "Relay off") as 48 kHz 16-bit mono WAV.
// Run from the repository root:  dotnet run tools/GenerateCueSounds.cs
// Optional argument: output folder (default Sources/MicControlNG.UI/Assets/Notifications).
//
// The sounds are synthesised, not recorded, so they are ours to ship. The model is the "asymmetric relay" from the
// cue designer: a short seeded noise burst (plus optional contact bounces) rings a band-pass resonance whose pitch can
// sag, a one-pole low-pass sets the brightness, the body decays exponentially, and a faint steady tone sits underneath.
// The cue is trimmed once it is 60 dB down, with a 2 ms fade, so nothing lingers.

using System.Text;

var output = args.Length > 0 ? args[0] : Path.Combine("Sources", "MicControlNG.UI", "Assets", "Notifications");
Directory.CreateDirectory(output);

// Unmute: the relay latches. A dark 2.2 kHz click with one contact bounce over a soft, steady 1175 Hz triangle.
var on = new Cue(Freq: 2200, FreqEnd: 2200, Q: 7, BurstMs: 1.2, DecayMs: 3, Bounces: 1, BounceGapMs: 2.6, BounceGain: 0.45,
    LowpassHz: 2500, LevelDb: -9, ToneMix: 0.18, ToneHz: 1175, ToneMs: 40, ToneTriangle: true, ToneBrightHz: 4000);

// Mute: the relay releases. A muffled 450 Hz thud sagging to 340 Hz over a steady 494 Hz sine.
var off = new Cue(Freq: 450, FreqEnd: 340, Q: 2.5, BurstMs: 5, DecayMs: 16, Bounces: 0, BounceGapMs: 3, BounceGain: 0.4,
    LowpassHz: 1100, LevelDb: -11, ToneMix: 0.18, ToneHz: 494, ToneMs: 55, ToneTriangle: false, ToneBrightHz: 16000);

foreach (var (name, cue) in new[] { ("Relay on", on), ("Relay off", off) })
{
    var samples = Synth.Render(cue);
    var path = Path.Combine(output, name + ".wav");
    Wav.Write(path, samples, Synth.SampleRate);
    Console.WriteLine($"{path}: {samples.Length * 1000.0 / Synth.SampleRate:0.0} ms");
}

internal sealed record Cue(
    double Freq, double FreqEnd, double Q, double BurstMs, double DecayMs, int Bounces, double BounceGapMs, double BounceGain,
    double LowpassHz, double LevelDb, double ToneMix, double ToneHz, double ToneMs, bool ToneTriangle, double ToneBrightHz);

internal static class Synth
{
    public const int SampleRate = 48000;
    private const double MaxSeconds = 0.25;

    public static float[] Render(Cue p)
    {
        var n = (int)Math.Round(MaxSeconds * SampleRate);
        var output = new float[n];
        double burst = p.BurstMs / 1000, tau = p.DecayMs / 1000, gap = p.BounceGapMs / 1000;

        // Excitation: raised-cosine windowed noise from a fixed seed, repeated (quieter) for each contact bounce.
        var hits = Enumerable.Range(0, p.Bounces + 1).Select(k => (At: k * gap, Gain: Math.Pow(p.BounceGain, k))).ToArray();
        var seed = 12345u;
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0, lowpassed = 0;
        var lowpassAlpha = 1 - Math.Exp(-2 * Math.PI * p.LowpassHz / SampleRate);
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / SampleRate;
            seed = unchecked(seed * 1664525u + 1013904223u);
            var noise = seed / 2147483648.0 - 1;
            double x = 0;
            foreach (var (at, gain) in hits)
            {
                var u = t - at;
                if (u >= 0 && u < burst)
                {
                    x += gain * noise * (0.5 - 0.5 * Math.Cos(2 * Math.PI * u / burst));
                }
            }

            // Band-pass biquad (RBJ, constant 0 dB peak gain), retuned every sample as the resonance sags.
            var k = Math.Min(1, t / Math.Max(tau * 3, 0.001));
            var f = p.Freq * Math.Pow(p.FreqEnd / p.Freq, k);
            var w0 = 2 * Math.PI * f / SampleRate;
            var alpha = Math.Sin(w0) / (2 * p.Q);
            var y = ((alpha * x) - (alpha * x2) + (2 * Math.Cos(w0) * y1) - ((1 - alpha) * y2)) / (1 + alpha);
            (x2, x1, y2, y1) = (x1, x, y1, y);
            lowpassed += lowpassAlpha * (y - lowpassed);
            output[i] = (float)(lowpassed * Math.Exp(-t / tau));
        }

        // The tone layer, mixed against the relay's own peak: 3 ms raised-cosine in, flat, 12 ms raised-cosine out.
        var relayPeak = output.Max(Math.Abs);
        if (p.ToneMix > 0)
        {
            var length = p.ToneMs / 1000;
            var count = Math.Min(n, (int)Math.Round(length * SampleRate));
            double attack = 0.003, release = Math.Min(0.012, length / 2), phase = 0;
            for (var i = 0; i < count; i++)
            {
                var t = (double)i / SampleRate;
                phase += 2 * Math.PI * p.ToneHz / SampleRate;
                var envelope = t < attack ? 0.5 - (0.5 * Math.Cos(Math.PI * t / attack)) : 1;
                if (length - t < release)
                {
                    envelope = Math.Min(envelope, 0.5 - (0.5 * Math.Cos(Math.PI * (length - t) / release)));
                }

                output[i] += (float)(relayPeak * p.ToneMix * envelope * Tone(p, phase));
            }
        }

        // Trim once 60 dB below the peak, fade the last 2 ms, and set the peak level.
        var peak = output.Max(Math.Abs);
        var end = n;
        while (end > 1 && Math.Abs(output[end - 1]) < peak * 0.001)
        {
            end--;
        }

        var fade = (int)Math.Round(0.002 * SampleRate);
        end = Math.Min(n, end + fade);
        var trimmed = output[..end];
        for (var i = 0; i < fade && i < end; i++)
        {
            trimmed[end - 1 - i] *= (float)i / fade;
        }

        var level = (float)(Math.Pow(10, p.LevelDb / 20) / peak);
        for (var i = 0; i < trimmed.Length; i++)
        {
            trimmed[i] *= level;
        }

        return trimmed;
    }

    // Sine, or a band-limited triangle (odd harmonics up to ToneBrightHz) so it stays mellow and never aliases.
    private static double Tone(Cue p, double phase)
    {
        if (!p.ToneTriangle)
        {
            return Math.Sin(phase);
        }

        double v = 0;
        for (var h = 1; h * p.ToneHz <= Math.Max(p.ToneHz, p.ToneBrightHz); h += 2)
        {
            v += ((h - 1) / 2 % 2 == 0 ? 1 : -1) * Math.Sin(h * phase) / (h * h);
        }

        return v * 8 / (Math.PI * Math.PI);
    }
}

internal static class Wav
{
    public static void Write(string path, float[] samples, int sampleRate)
    {
        using var writer = new BinaryWriter(File.Create(path), Encoding.ASCII);
        var dataBytes = samples.Length * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (var s in samples)
        {
            writer.Write((short)Math.Round(Math.Clamp(s, -1f, 1f) * short.MaxValue));
        }
    }
}
