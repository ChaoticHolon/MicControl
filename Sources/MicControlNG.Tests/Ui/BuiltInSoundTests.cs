using MicControlNG.Settings;

namespace MicControlNG.Tests.Ui;

/// <summary>Checks the sound files that actually ship (copied from MicControlNG.UI/Assets/Notifications).</summary>
public sealed class BuiltInSoundTests
{
    private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "Resources", "Notifications");

    [Fact]
    public void DefaultSounds_Ship_AndOldBeepsStayAvailable()
    {
        var sounds = new NotificationSounds(new FakeSoundPlayer(), Directory, Path.Combine(Directory, "no-user-sounds"));
        var defaults = new AppSettings().Notifications;

        Assert.Equal(NotificationSettings.DefaultMutedSound, defaults.WhenMuted);
        Assert.Equal(NotificationSettings.DefaultUnmutedSound, defaults.WhenUnmuted);
        Assert.Subset(sounds.GetNames().ToHashSet(), new HashSet<string> { "Relay on", "Relay off", "Beep300", "Beep750" });
    }

    [Theory]
    [InlineData("Relay on")]
    [InlineData("Relay off")]
    public void DefaultCues_AreShortCleanPcm(string name)
    {
        var bytes = File.ReadAllBytes(Path.Combine(Directory, name + ".wav"));

        // 44-byte PCM header: format 1 (PCM), 1 channel, 48 kHz, 16 bits.
        Assert.Equal("RIFF"u8.ToArray(), bytes[..4]);
        Assert.Equal(1, BitConverter.ToInt16(bytes, 20));
        Assert.Equal(1, BitConverter.ToInt16(bytes, 22));
        Assert.Equal(48000, BitConverter.ToInt32(bytes, 24));
        Assert.Equal(16, BitConverter.ToInt16(bytes, 34));

        var samples = Enumerable.Range(0, (bytes.Length - 44) / 2).Select(i => BitConverter.ToInt16(bytes, 44 + (i * 2))).ToArray();
        var milliseconds = samples.Length * 1000.0 / 48000;
        Assert.InRange(milliseconds, 20, 100);

        // A cue, not an alarm: peaks well below full scale, and it ends in silence rather than being cut off.
        Assert.InRange(samples.Max(s => Math.Abs((int)s)), short.MaxValue / 10, short.MaxValue / 2);
        Assert.Equal(0, samples[^1]);
    }
}
