namespace MicControl.Tests.Fuzz;

/// <summary>
/// The opt-in fuzz suite: skipped unless MICCONTROL_FUZZ=1 (tools/Fuzz.ps1 sets it). Each run uses a new seed, printed
/// with every failure; MICCONTROL_FUZZ_SEED repeats a run and MICCONTROL_FUZZ_SCALE multiplies the case counts.
/// </summary>
internal static class FuzzRun
{
    public static bool Enabled { get; } = Environment.GetEnvironmentVariable("MICCONTROL_FUZZ") == "1";

    public static int Seed { get; } = int.TryParse(Environment.GetEnvironmentVariable("MICCONTROL_FUZZ_SEED"), out var seed)
        ? seed
        : System.Random.Shared.Next();

    private static readonly double Factor = double.TryParse(Environment.GetEnvironmentVariable("MICCONTROL_FUZZ_SCALE"),
        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var factor) && factor > 0 ? factor : 1;

    public static void Require() => Assert.SkipUnless(Enabled, "Set MICCONTROL_FUZZ=1 (or run tools/Fuzz.ps1) to fuzz.");

    /// <summary>A generator for one test: the run's seed mixed with the test's own number, so tests don't share a sequence.</summary>
    public static Random Random(int salt) => new(unchecked((Seed * 1_000_003) + salt));

    public static int Scale(int cases) => Math.Max(1, (int)(cases * Factor));

    /// <summary>Fails with every distinct problem found, and the seed that repeats the run.</summary>
    public static void AssertNone(IReadOnlyDictionary<string, string> failures)
    {
        if (failures.Count > 0)
        {
            Assert.Fail($"Seed {Seed} (set MICCONTROL_FUZZ_SEED={Seed} to repeat this run)\n\n"
                + string.Join("\n\n", failures.Select(f => f.Key + "\n  " + f.Value)));
        }
    }
}
