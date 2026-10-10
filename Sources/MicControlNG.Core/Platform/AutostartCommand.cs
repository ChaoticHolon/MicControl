namespace MicControlNG.Platform;

/// <summary>Reads autostart command lines (Run key values, scheduled task actions).</summary>
public static class AutostartCommand
{
    /// <summary>The executable a command line starts: the quoted first token, or everything up to ".exe".</summary>
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

        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe >= 0)
        {
            return text[..(exe + ".exe".Length)];
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
