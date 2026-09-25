using MicControlNG.Platform;

namespace MicControlNG;

/// <summary>
/// Notification sound library: built-in sounds shipped with the app plus user-added ones
/// (a user sound overrides a built-in one of the same name).
/// </summary>
public sealed class NotificationSounds(ISoundPlayer player, string builtInDirectory, string userDirectory)
{
    private static readonly string[] Extensions = [".wav", ".mp3"];

    public string UserDirectory { get; } = userDirectory;

    public IReadOnlyList<string> GetNames() =>
        [.. new[] { builtInDirectory, UserDirectory }
            .Where(Directory.Exists)
            .SelectMany(Directory.EnumerateFiles)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];

    public string? FindFile(string? name) => string.IsNullOrEmpty(name)
        ? null
        : new[] { UserDirectory, builtInDirectory }
            .SelectMany(dir => Extensions.Select(ext => Path.Combine(dir, name + ext)))
            .FirstOrDefault(File.Exists);

    public Task PlayAsync(string? name, float volume, string? outputDeviceId) =>
        FindFile(name) is { } file ? player.PlayAsync(file, volume, outputDeviceId) : Task.CompletedTask;

    /// <summary>Converts an audio file to WAV in the user folder and returns the new sound's name.</summary>
    public string Add(string sourceFile)
    {
        Directory.CreateDirectory(UserDirectory);
        var name = Path.GetFileNameWithoutExtension(sourceFile);
        player.ConvertToWav(sourceFile, Path.Combine(UserDirectory, name + ".wav"));
        return name;
    }
}
