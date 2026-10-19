namespace MicControl.Platform;

/// <summary>Reads autostart command lines (Run key values, scheduled task actions).</summary>
public static class AutostartCommand
{
    /// <summary>The executable a command line starts: the quoted first token, or everything up to the first ".exe" that ends a name.</summary>
    public static string? GetExecutable(string? command)
    {
        var text = command?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text[0] == '"')
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }

        // ".exe" ends the path only where the name ends: C:\Users\alex.exeter\…\MicSwitch.exe isn't C:\Users\alex.exe.
        for (var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); exe >= 0;
            exe = text.IndexOf(".exe", exe + 1, StringComparison.OrdinalIgnoreCase))
        {
            var end = exe + ".exe".Length;
            if (end == text.Length || char.IsWhiteSpace(text[end]))
            {
                return text[..end];
            }
        }

        var space = text.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? text : text[..space];
    }

    /// <summary>
    /// True when an entry starts a program that no longer exists, so it can be replaced.
    /// Missing or unreadable commands count as not stale, so nothing is removed by guesswork.
    /// </summary>
    public static bool IsStale(string? command, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        var exe = GetExecutable(command);
        return exe is not null && !fileExists(Environment.ExpandEnvironmentVariables(exe));
    }
}
