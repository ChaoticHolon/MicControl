using Avalonia.Controls;
using MicControlNG.Platform;
using Microsoft.Extensions.Logging;

namespace MicControlNG.Services;

/// <summary>
/// Watches our windows and the hidden helper windows the app depends on for close and session-end messages.
/// <list type="bullet">
/// <item>Logs who asks a window to close, for our windows and helpers alike. Our windows' Closing handlers decide.</item>
/// <item>Helper windows refuse every close: closing Avalonia's message window leaves a "zombie" (no tray icon, dead
/// hotkeys and timers, process alive), and closing the hidden owner window destroys the overlay and tray popup.</item>
/// <item>A session end exits the app only once the OS confirms it (WM_ENDSESSION). The helpers answer the query
/// themselves, so the toolkit doesn't start exiting on the question and stay closed if another app vetoes it.</item>
/// </list>
/// </summary>
/// <remarks>
/// Helpers on other threads (e.g. Avalonia's renderer) report their messages on those threads, so the state here is
/// locked, and only the UI thread's windows start the exit.
/// </remarks>
public sealed partial class WindowGuard(IWindowInterop interop, AppShutdown shutdown, ILogger logger, Action exitForSessionEnd) : IDisposable
{
    private readonly Lock gate = new();
    private readonly Dictionary<nint, IDisposable> watched = [];
    private bool sessionEndAsked;

    /// <summary>Watches one of our windows, and looks for new helper windows each time it opens.</summary>
    public void Watch(Window window, string name)
    {
        if (window.TryGetPlatformHandle()?.Handle is { } handle and not 0)
        {
            Start(handle, name, message => OnMessage(handle, message, isHelper: false, onUiThread: true));
        }

        // Showing a window can create helpers, e.g. the hidden owner the first time a window kept off the taskbar opens.
        window.Opened += (_, _) => ProtectHelperWindows();
    }

    /// <summary>Starts protecting helper windows that aren't watched yet.</summary>
    public void ProtectHelperWindows()
    {
        var added = 0;
        foreach (var helper in interop.FindToolkitHelperWindows())
        {
            var handle = helper.Handle;
            if (Start(handle, helper.Name, message => OnMessage(handle, message, isHelper: true, helper.OnUiThread)))
            {
                LogProtecting(logger, helper.Name, handle, helper.ThreadId);
                added++;
            }
        }

        if (added > 0)
        {
            int count;
            lock (gate)
            {
                count = watched.Count;
            }

            LogWatching(logger, added, count);
        }
    }

    public void Dispose()
    {
        List<IDisposable> watches;
        lock (gate)
        {
            watches = [.. watched.Values];
            watched.Clear();
        }

        watches.ForEach(w => w.Dispose());
    }

    /// <returns><c>true</c> if the window wasn't watched yet and now is.</returns>
    private bool Start(nint handle, string name, Func<WindowLifecycleMessage, bool> handler)
    {
        lock (gate)
        {
            if (watched.ContainsKey(handle))
            {
                return false;
            }
        }

        if (interop.WatchLifecycle(handle, name, handler) is not { } watch)
        {
            LogWatchFailed(logger, name, handle);
            return false;
        }

        lock (gate)
        {
            watched[handle] = watch;
        }

        return true;
    }

    private bool OnMessage(nint handle, WindowLifecycleMessage message, bool isHelper, bool onUiThread)
    {
        var foreground = message.ForegroundProcess ?? "unknown";
        LogWindowMessage(logger, message.Window, message.Message, message.Details, message.Origin, foreground);
        switch (message.Kind)
        {
            case WindowLifecycleKind.CloseRequest when isHelper:
                LogHelperCloseRefused(logger, message.Message, message.Window, message.Origin, foreground);
                return true;

            case WindowLifecycleKind.SessionEndQuery:
                lock (gate)
                {
                    sessionEndAsked = true;
                }

                // The UI thread's helpers answer "yes" themselves: Avalonia's message window would start exiting on
                // the question. Our own windows and other threads' helpers answer it as usual.
                return isHelper && onUiThread;

            case WindowLifecycleKind.SessionEnd when !onUiThread:
                // The exit closes windows on the UI thread; the UI thread's own windows get this message too.
                LogSessionEndLeftToUiThread(logger, message.Window);
                return false;

            case WindowLifecycleKind.SessionEnd:
                if (!shutdown.IsExiting)
                {
                    LogSessionEnding(logger, message.Details);
                    exitForSessionEnd();
                }

                return true;

            case WindowLifecycleKind.SessionEndCancelled:
                bool wasAsked;
                lock (gate)
                {
                    wasAsked = sessionEndAsked;
                    sessionEndAsked = false;
                }

                if (wasAsked)
                {
                    LogSessionEndCancelled(logger);
                }

                return false;

            case WindowLifecycleKind.Destroyed:
                lock (gate)
                {
                    watched.Remove(handle);
                }

                if (isHelper && !shutdown.IsExiting)
                {
                    LogHelperDestroyed(logger, message.Window);
                }

                return false;

            default:
                return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Window} received {Message} {Details} ({Origin}; foreground: {Foreground})")]
    private static partial void LogWindowMessage(ILogger logger, string window, string message, string details, string origin, string foreground);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused {Message} to the {Window} ({Origin}; foreground: {Foreground}); the app needs this hidden window to keep its tray icon, hotkeys and windows working")]
    private static partial void LogHelperCloseRefused(ILogger logger, string message, string window, string origin, string foreground);

    [LoggerMessage(Level = LogLevel.Critical, Message = "The {Window} was destroyed while the app is running")]
    private static partial void LogHelperDestroyed(ILogger logger, string window);

    [LoggerMessage(Level = LogLevel.Information, Message = "Session ending ({Details}): exiting instead of hiding to the tray")]
    private static partial void LogSessionEnding(ILogger logger, string details);

    [LoggerMessage(Level = LogLevel.Information, Message = "The session end was cancelled; keeps running")]
    private static partial void LogSessionEndCancelled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Protecting the {Window} (handle 0x{Handle:X}, thread {Thread}) from close requests")]
    private static partial void LogProtecting(ILogger logger, string window, nint handle, int thread);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not watch the {Window} (handle 0x{Handle:X}) for close and session-end messages")]
    private static partial void LogWatchFailed(ILogger logger, string window, nint handle);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Window} saw the session end; the UI thread's windows exit the app")]
    private static partial void LogSessionEndLeftToUiThread(ILogger logger, string window);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Protecting {Added} more hidden helper windows; watching {Count} windows for close and session-end messages")]
    private static partial void LogWatching(ILogger logger, int added, int count);
}
