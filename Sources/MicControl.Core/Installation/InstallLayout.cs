namespace MicControl.Installation;

/// <summary>
/// Where an install puts things, and what it registers in Settings → Apps. Paths are built from the folders passed
/// in, so the rules can be tested anywhere; the Windows installer passes the real known folders.
/// </summary>
public static class InstallLayout
{
    /// <summary>Installer and package name (Settings → Apps, folders, registry key).</summary>
    public const string ProductName = "MicControl";

    /// <summary>The app's own name, used for things that start it, such as the Start menu shortcut.</summary>
    public const string AppName = "MicControl";

    public const string ExecutableName = "MicControl.exe";

    public const string Publisher = "ChaoticHolon";

    public const string RepositoryUrl = "https://github.com/ChaoticHolon/MicControl";

    /// <summary>Key under <c>Software\Microsoft\Windows\CurrentVersion\Uninstall</c> in HKLM (all users) or HKCU (just me).</summary>
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + ProductName;

    /// <summary>The built-in notification sounds, next to the executable (<see cref="NotificationSounds"/> reads them there).</summary>
    public static readonly string SoundsFolder = Path.Combine("Resources", "Notifications");

    /// <param name="programFiles">Program Files (64-bit).</param>
    /// <param name="localAppData">The user's local application data folder.</param>
    public static string DirectoryFor(InstallScope scope, string programFiles, string localAppData) =>
        scope == InstallScope.AllUsers
            ? Path.Combine(programFiles, ProductName)
            : Path.Combine(localAppData, "Programs", ProductName);

    /// <param name="commonPrograms">The all-users Start menu Programs folder.</param>
    /// <param name="userPrograms">The user's Start menu Programs folder.</param>
    public static string ShortcutFor(InstallScope scope, string commonPrograms, string userPrograms) =>
        Path.Combine(scope == InstallScope.AllUsers ? commonPrograms : userPrograms, AppName + ".lnk");

    /// <summary>The values of the Settings → Apps entry. Uninstalling runs this same program with --uninstall.</summary>
    public static IReadOnlyDictionary<string, object> UninstallEntry(string directory, string version, long sizeBytes)
    {
        var exe = Path.Combine(directory, ExecutableName);
        return new Dictionary<string, object>
        {
            ["DisplayName"] = ProductName,
            ["DisplayVersion"] = version,
            ["Publisher"] = Publisher,
            ["DisplayIcon"] = exe,
            ["InstallLocation"] = directory,
            ["UninstallString"] = $"\"{exe}\" {SetupCommand.UninstallArgument}",
            ["QuietUninstallString"] = $"\"{exe}\" {SetupCommand.UninstallArgument} {SetupCommand.SilentArgument}",
            ["URLInfoAbout"] = RepositoryUrl,
            ["HelpLink"] = RepositoryUrl + "/issues",
            ["URLUpdateInfo"] = RepositoryUrl + "/releases",
            ["NoModify"] = 1,
            ["NoRepair"] = 1,
            ["EstimatedSize"] = (int)Math.Min(int.MaxValue, (sizeBytes + 1023) / 1024),
        };
    }

    /// <summary>Which scope a running copy belongs to, judging by where it runs from; null if it isn't installed there.</summary>
    public static InstallScope? ScopeOf(string executableDirectory, string programFiles, string localAppData)
    {
        foreach (var scope in new[] { InstallScope.AllUsers, InstallScope.CurrentUser })
        {
            if (SameFolder(DirectoryFor(scope, programFiles, localAppData), executableDirectory))
            {
                return scope;
            }
        }

        return null;
    }

    public static bool SameFolder(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);
}
