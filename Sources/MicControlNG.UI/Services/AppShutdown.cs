namespace MicControlNG.Services;

/// <summary>
/// Tracks whether the app is exiting, however the exit started (tray menu, Window-mode close, a session end, an
/// update, or the UI loop ending on its own), and runs the cleanup exactly once.
/// Windows consult <see cref="IsExiting"/>: until then they refuse to be closed, because a closed helper or
/// overlay window can't be shown again.
/// </summary>
public sealed class AppShutdown
{
    private int cleanedUp;

    public bool IsExiting { get; private set; }

    /// <summary>Why the exit started, for the log; <c>null</c> while the app is running.</summary>
    public string? Reason { get; private set; }

    /// <returns><c>false</c> if an exit was already under way (the first reason is kept).</returns>
    public bool BeginExit(string reason)
    {
        if (IsExiting)
        {
            return false;
        }

        IsExiting = true;
        Reason = reason;
        return true;
    }

    /// <summary>Runs <paramref name="cleanup"/> the first time it's called; later calls do nothing.</summary>
    /// <returns><c>true</c> if this call ran the cleanup.</returns>
    public bool CleanUpOnce(Action cleanup)
    {
        if (Interlocked.Exchange(ref cleanedUp, 1) == 1)
        {
            return false;
        }

        cleanup();
        return true;
    }
}
