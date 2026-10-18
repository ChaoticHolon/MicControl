using System.Text.Json;

namespace MicControl.Settings;

/// <summary>
/// Loads and atomically saves <see cref="AppSettings"/> as JSON. On first run it imports MicSwitch 1.x settings
/// (<c>release\config.cfg</c> in <paramref name="legacyDirectory"/>) and the custom sounds next to them.
/// </summary>
public sealed class SettingsStore(string directory, string? legacyDirectory = null)
{
    public const string FileName = "settings.json";

    private static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public static string DefaultDirectory { get; } = Path.Combine(AppData, "MicControl");

    /// <summary>Where MicSwitch 1.x kept its data.</summary>
    public static string DefaultLegacyDirectory { get; } = Path.Combine(AppData, "MicSwitch");

    public string Directory { get; } = directory;

    public string? LegacyDirectory { get; } = legacyDirectory;

    public string FilePath => Path.Combine(Directory, FileName);

    /// <summary>True when the last <see cref="Load"/> imported MicSwitch 1.x settings.</summary>
    public bool MigratedFromLegacy { get; private set; }

    /// <summary>True when the last <see cref="Load"/> found no settings at all (none saved, nothing to import): a new user.</summary>
    public bool IsFirstRun { get; private set; }

    /// <summary>
    /// Reads settings, falling back to the MicSwitch 1.x import and then to defaults.
    /// A corrupt file is preserved as <c>settings.json.corrupt</c> rather than overwritten silently.
    /// </summary>
    public AppSettings Load()
    {
        MigratedFromLegacy = false;
        IsFirstRun = false;
        if (File.Exists(FilePath))
        {
            try
            {
                using var stream = File.OpenRead(FilePath);
                return (JsonSerializer.Deserialize(stream, SettingsJsonContext.Readable.AppSettings) ?? new AppSettings()).Normalize();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
            {
                File.Copy(FilePath, FilePath + ".corrupt", overwrite: true);
                return new AppSettings();
            }
        }

        var legacy = LegacyDirectory ?? Directory;
        var migrated = LegacyConfigMigrator.TryMigrate(legacy, Path.Combine(Directory, "Icons"));
        if (migrated is null)
        {
            IsFirstRun = true;
            return new AppSettings();
        }

        // MicSwitch 1.x kept user-added sounds in the same Resources\Notifications layout.
        CopyDirectory(Path.Combine(legacy, "Resources", "Notifications"), Path.Combine(Directory, "Resources", "Notifications"));
        MigratedFromLegacy = true;
        Save(migrated);
        return migrated;
    }

    public void Save(AppSettings settings)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var temp = FilePath + ".tmp";
        using (var stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, settings, SettingsJsonContext.Readable.AppSettings);
        }

        File.Move(temp, FilePath, overwrite: true);
    }

    public static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);

    public static AppSettings Deserialize(string json) => (JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings()).Normalize();

    private static void CopyDirectory(string source, string target)
    {
        if (!System.IO.Directory.Exists(source))
        {
            return;
        }

        System.IO.Directory.CreateDirectory(target);
        foreach (var file in System.IO.Directory.EnumerateFiles(source))
        {
            var destination = Path.Combine(target, Path.GetFileName(file));
            if (!File.Exists(destination))
            {
                File.Copy(file, destination);
            }
        }
    }
}
