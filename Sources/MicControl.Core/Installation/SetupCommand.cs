namespace MicControl.Installation;

/// <summary>Where an installed copy lives: Program Files for all users, or the user's own Programs folder.</summary>
public enum InstallScope
{
    AllUsers,
    CurrentUser,
}

public enum SetupMode
{
    /// <summary>Run the app normally.</summary>
    None,

    Install,

    Uninstall,
}

/// <summary>
/// What a MicControl.exe launch should do. The release download, <c>MicControl-Setup-&lt;version&gt;.exe</c>, is the app
/// itself: started under that name (or with <c>--install</c>) it shows the installer; the installed copy, started with
/// <c>--uninstall</c> from Settings → Apps, shows the uninstaller. Anything else runs the app.
/// </summary>
public sealed record SetupCommand(SetupMode Mode, bool Silent = false, InstallScope? Scope = null, bool Relaunch = false)
{
    public const string InstallArgument = "--install";
    public const string UninstallArgument = "--uninstall";
    public const string SilentArgument = "--silent";
    public const string RelaunchArgument = "--relaunch";
    public const string ScopeArgument = "--scope=";
    public const string SetupFilePrefix = "MicControl-Setup";

    public static SetupCommand Parse(IReadOnlyList<string> args, string? executablePath)
    {
        ArgumentNullException.ThrowIfNull(args);
        var mode = SetupMode.None;
        var silent = false;
        var relaunch = false;
        InstallScope? scope = null;
        foreach (var arg in args)
        {
            if (Is(arg, InstallArgument))
            {
                mode = SetupMode.Install;
            }
            else if (Is(arg, UninstallArgument))
            {
                mode = SetupMode.Uninstall;
            }
            else if (Is(arg, SilentArgument))
            {
                silent = true;
            }
            else if (Is(arg, RelaunchArgument))
            {
                relaunch = true;
            }
            else if (arg.StartsWith(ScopeArgument, StringComparison.OrdinalIgnoreCase))
            {
                scope = arg[ScopeArgument.Length..].ToLowerInvariant() switch
                {
                    "all" or "allusers" or "machine" => InstallScope.AllUsers,
                    "user" or "currentuser" or "me" => InstallScope.CurrentUser,
                    _ => null,
                };
            }
        }

        if (mode == SetupMode.None && IsSetupFile(executablePath))
        {
            mode = SetupMode.Install;
        }

        return new SetupCommand(mode, mode != SetupMode.None && silent, scope, relaunch);
    }

    /// <summary>The downloaded setup file, e.g. MicControl-Setup-2.1.0.exe (or a browser's "… (1).exe" copy).</summary>
    public static bool IsSetupFile(string? executablePath) =>
        Path.GetFileName(executablePath ?? string.Empty).StartsWith(SetupFilePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The command line that repeats this command, e.g. for an elevated restart or a silent update.</summary>
    public string ToArguments()
    {
        var parts = new List<string>();
        if (Mode != SetupMode.None)
        {
            parts.Add(Mode == SetupMode.Install ? InstallArgument : UninstallArgument);
        }

        if (Silent)
        {
            parts.Add(SilentArgument);
        }

        if (Scope is { } s)
        {
            parts.Add(ScopeArgument + (s == InstallScope.AllUsers ? "all" : "user"));
        }

        if (Relaunch)
        {
            parts.Add(RelaunchArgument);
        }

        return string.Join(' ', parts);
    }

    private static bool Is(string arg, string expected) =>
        string.Equals(arg, expected, StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "/" + expected[2..], StringComparison.OrdinalIgnoreCase);
}
