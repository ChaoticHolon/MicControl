using System.Text.Json;

namespace MicControlNG.Settings;

/// <summary>
/// Loads and atomically saves <see cref="AppSettings"/> as JSON. On first run it migrates from the previous
/// MicSwitch folder (<paramref name="legacyDirectory"/>): a 2.x <c>settings.json</c> with its icons and sounds,
/// or a 1.x <c>release\config.cfg</c>.
/// </summary>
public sealed class SettingsStore(string directory, string? legacyDirectory = null)
{
    public const string FileName = "settings.json";

    private static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public static string DefaultDirectory { get; } = Path.Combine(AppData, "MicControlNG");

    /// <summary>Where MicSwitch (1.x and early 2.x builds) kept its data.</summary>
    public static string DefaultLegacyDirectory { get; } = Path.Combine(AppData, "MicSwitch");

    public string Directory { get; } = directory;

    public string? LegacyDirectory { get; } = legacyDirectory;

    public string FilePath => Path.Combine(Directory, FileName);

    /// <summary>True when the last <see cref="Load"/> imported settings from the legacy folder or a 1.x config.</summary>
    public bool MigratedFromLegacy { get; private set; }

    /// <summary>
    /// Reads settings, falling back to migration and then to defaults.
    /// A corrupt file is preserved as <c>settings.json.corrupt</c> rather than overwritten silently.
    /// </summary>
    public AppSettings Load()
    {
        MigratedFromLegacy = false;
        if (File.Exists(FilePath))
        {
            try
            {
                using var stream = File.OpenRead(FilePath);
                return Upgrade(JsonSerializer.Deserialize(stream, SettingsJsonContext.Readable.AppSettings) ?? new AppSettings());
            }
            catch (JsonException)
            {
                File.Copy(FilePath, FilePath + ".corrupt", overwrite: true);
                return new AppSettings();
            }
        }

        var migrated = TryMigrateLegacyFolder()
            ?? LegacyConfigMigrator.TryMigrate(LegacyDirectory ?? Directory, Path.Combine(Directory, "Icons"));
        if (migrated is null)
        {
            return new AppSettings();
        }

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

    public static AppSettings Deserialize(string json) => JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();

    private static AppSettings Upgrade(AppSettings settings)
    {
        if (settings.Version < AppSettings.CurrentVersion)
        {
            // Written by an earlier version: this user has already set things up.
            settings.SetupCompleted = true;
            settings.Version = AppSettings.CurrentVersion;
        }

        // Version 3 had "Start in tray"; everything older behaved like tray mode.
        if (settings.Window.LegacyStartInTray is { } startInTray)
        {
            settings.Window.AppMode = startInTray ? AppMode.Tray : AppMode.Window;
            settings.Window.LegacyStartInTray = null;
        }

        return settings;
    }

    /// <summary>Copies user files from the legacy folder and loads its 2.x settings, if any.</summary>
    private AppSettings? TryMigrateLegacyFolder()
    {
        if (LegacyDirectory is null || !System.IO.Directory.Exists(LegacyDirectory))
        {
            return null;
        }

        // Custom icons and sounds (1.x used the same Resources\Notifications folder for sounds).
        CopyDirectory(Path.Combine(LegacyDirectory, "Icons"), Path.Combine(Directory, "Icons"));
        CopyDirectory(Path.Combine(LegacyDirectory, "Resources", "Notifications"), Path.Combine(Directory, "Resources", "Notifications"));

        var legacyFile = Path.Combine(LegacyDirectory, FileName);
        if (!File.Exists(legacyFile))
        {
            return null;
        }

        try
        {
            var settings = Upgrade(Deserialize(File.ReadAllText(legacyFile)));
            settings.Overlay.MutedIconPath = MovePath(settings.Overlay.MutedIconPath);
            settings.Overlay.UnmutedIconPath = MovePath(settings.Overlay.UnmutedIconPath);
            return settings;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Points a path inside the legacy folder at the same file in the new folder.</summary>
    internal string? MovePath(string? path)
    {
        if (path is null || LegacyDirectory is null)
        {
            return path;
        }

        var relative = Path.GetRelativePath(LegacyDirectory, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? path : Path.Combine(Directory, relative);
    }

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
