namespace MicControl.Diagnostics;

public enum UiWatchdogState
{
    Healthy,

    /// <summary>The last ping wasn't handled yet; the UI thread may be busy or stuck.</summary>
    Late,

    /// <summary>Several pings in a row went unanswered: the UI thread has stopped processing work.</summary>
    Failed,
}

/// <summary>
/// Detects a UI thread that has stopped processing work (hung, or its dispatcher window was destroyed) while the
/// process stays alive. Call <see cref="Check"/> on a background timer: each call checks the previous ping and posts
/// a new one through <paramref name="post"/>. Missed checks only count while the checker itself runs, so time spent
/// in sleep or hibernation never looks like a hang.
/// </summary>
public sealed class UiWatchdog(Action<Action> post, int missesBeforeFailure)
{
    private long sent;
    private long answered;

    public int Misses { get; private set; }

    public UiWatchdogState Check()
    {
        var last = Interlocked.Read(ref sent);
        Misses = Interlocked.Read(ref answered) >= last ? 0 : Misses + 1;

        var ping = Interlocked.Increment(ref sent);
        post(() => Interlocked.Exchange(ref answered, ping));

        return Misses switch
        {
            0 => UiWatchdogState.Healthy,
            _ when Misses >= missesBeforeFailure => UiWatchdogState.Failed,
            _ => UiWatchdogState.Late,
        };
    }
}
