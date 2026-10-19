using System.Diagnostics;
using System.Runtime.InteropServices;
using MicControl.Platform;
using static MicControl.Windows.NativeMethods;

namespace MicControl.Windows;

/// <summary>
/// Subclasses a window to report close and session-end messages: who sent them (sent from another thread or
/// process, or posted), their flags, and which process was in the foreground. Used to find out what closes a
/// hidden tray app, and to keep toolkit helper windows alive.
/// </summary>
/// <remarks>
/// A window on the calling thread is subclassed with comctl32 (<c>SetWindowSubclass</c>), which only works on the
/// window's own thread. A window on another thread of this process (e.g. a toolkit helper on a render thread) gets
/// its window procedure replaced instead, so the handler runs on that window's thread.
/// </remarks>
internal sealed class WindowLifecycleWatcher : IDisposable
{
    // Shared delegates keep the native callbacks alive for every watched window.
    private static readonly SubclassProc SubclassCallback = OnSubclassMessage;
    private static readonly WndProc WndProcCallback = OnWndProcMessage;
    private static readonly nint WndProcPointer = Marshal.GetFunctionPointerForDelegate(WndProcCallback);
    private static readonly Lock Gate = new();
    private static readonly Dictionary<nint, WindowLifecycleWatcher> Watchers = [];
    // Original procedures of windows whose procedure we replaced. Kept after restoring: the window's thread may
    // already be passing a message to ours.
    private static readonly Dictionary<nint, nint> ReplacedProcs = [];
    private static nuint nextId;

    private readonly nint handle;
    private readonly nuint id;
    private readonly string window;
    private readonly Func<WindowLifecycleMessage, bool> handler;
    private readonly bool replacedProc;

    private WindowLifecycleWatcher(nint handle, string window, Func<WindowLifecycleMessage, bool> handler, nuint id, bool replacedProc)
    {
        this.handle = handle;
        this.window = window;
        this.handler = handler;
        this.id = id;
        this.replacedProc = replacedProc;
    }

    /// <returns><c>null</c> if the window can't be watched (gone, or the OS refused).</returns>
    public static WindowLifecycleWatcher? TryStart(nint handle, string window, Func<WindowLifecycleMessage, bool> handler)
    {
        if (handle == 0)
        {
            return null;
        }

        var thread = GetWindowThreadProcessId(handle, out var pid);
        if (thread == 0 || pid != Environment.ProcessId)
        {
            return null;
        }

        var onThisThread = thread == GetCurrentThreadId();
        WindowLifecycleWatcher watcher;
        lock (Gate)
        {
            if (Watchers.ContainsKey(handle))
            {
                return null;
            }

            watcher = new WindowLifecycleWatcher(handle, window, handler, ++nextId, replacedProc: !onThisThread);
            // Registered first, so no message is missed once the hook is in place.
            Watchers[handle] = watcher;
        }

        if (onThisThread ? SetWindowSubclass(handle, SubclassCallback, watcher.id, 0) : ReplaceWndProc(handle))
        {
            return watcher;
        }

        lock (Gate)
        {
            Watchers.Remove(handle);
        }

        return null;
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (!Watchers.TryGetValue(handle, out var current) || current != this)
            {
                return;
            }

            Watchers.Remove(handle);
            if (!replacedProc)
            {
                RemoveWindowSubclass(handle, SubclassCallback, id);
            }
            else if (ReplacedProcs.TryGetValue(handle, out var original) && GetWindowLongPtr(handle, GWLP_WNDPROC) == WndProcPointer)
            {
                // Only while ours is still the outermost procedure; otherwise ours stays and keeps forwarding. The
                // original stays recorded for a message the window's thread may already be passing to ours.
                SetWindowLongPtr(handle, GWLP_WNDPROC, original);
            }
        }
    }

    private static bool ReplaceWndProc(nint handle)
    {
        lock (Gate)
        {
            // Store the original before swapping: the window's thread can call ours as soon as it's in place.
            var original = GetWindowLongPtr(handle, GWLP_WNDPROC);
            if (original == 0 || original == WndProcPointer)
            {
                return false;
            }

            ReplacedProcs[handle] = original;
            var previous = SetWindowLongPtr(handle, GWLP_WNDPROC, WndProcPointer);
            if (previous == 0)
            {
                ReplacedProcs.Remove(handle);
                return false;
            }

            ReplacedProcs[handle] = previous;
            return true;
        }
    }

    private static nint OnSubclassMessage(nint hWnd, uint msg, nint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData) =>
        TryHandle(hWnd, msg, wParam, lParam, out var result) ? result : DefSubclassProc(hWnd, msg, wParam, lParam);

    private static nint OnWndProcMessage(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        nint original;
        lock (Gate)
        {
            ReplacedProcs.TryGetValue(hWnd, out original);
        }

        if (TryHandle(hWnd, msg, wParam, lParam, out var result))
        {
            return result;
        }

        return original != 0 ? CallWindowProc(original, hWnd, msg, wParam, lParam) : 0;
    }

    /// <returns><c>true</c> if the handler handled the message and <paramref name="result"/> is the answer.</returns>
    private static bool TryHandle(nint hWnd, uint msg, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        WindowLifecycleWatcher? watcher;
        lock (Gate)
        {
            Watchers.TryGetValue(hWnd, out watcher);
        }

        if (watcher is null || Describe(msg, wParam, lParam) is not { } info)
        {
            return false;
        }

        var message = new WindowLifecycleMessage(info.Kind, watcher.window, info.Name, info.Details, Origin(), ForegroundProcess());
        if (watcher.handler(message))
        {
            // Handled: a refused close returns 0; a handled session-end query answers TRUE (the session may end).
            result = msg == WM_QUERYENDSESSION ? 1 : 0;
            return true;
        }

        if (msg == WM_DESTROY)
        {
            watcher.Dispose();
        }

        return false;
    }

    private static (WindowLifecycleKind Kind, string Name, string Details)? Describe(uint msg, nint wParam, nint lParam) => msg switch
    {
        WM_CLOSE => (WindowLifecycleKind.CloseRequest, "WM_CLOSE", string.Empty),
        WM_SYSCOMMAND when ((int)wParam & 0xFFF0) == SC_CLOSE => (WindowLifecycleKind.CloseRequest, "WM_SYSCOMMAND", "SC_CLOSE"),
        WM_QUERYENDSESSION => (WindowLifecycleKind.SessionEndQuery, "WM_QUERYENDSESSION", EndSessionFlags(lParam)),
        WM_ENDSESSION when wParam != 0 => (WindowLifecycleKind.SessionEnd, "WM_ENDSESSION", $"ending; {EndSessionFlags(lParam)}"),
        WM_ENDSESSION => (WindowLifecycleKind.SessionEndCancelled, "WM_ENDSESSION", $"not ending; {EndSessionFlags(lParam)}"),
        WM_DESTROY => (WindowLifecycleKind.Destroyed, "WM_DESTROY", string.Empty),
        _ => null,
    };

    private static string EndSessionFlags(nint lParam)
    {
        var flags = (uint)lParam;
        if (flags == 0)
        {
            return "shutdown or restart";
        }

        var parts = new List<string>(3);
        if ((flags & ENDSESSION_CLOSEAPP) != 0) parts.Add("close app (Restart Manager/installer)");
        if ((flags & ENDSESSION_CRITICAL) != 0) parts.Add("critical (forced)");
        if ((flags & ENDSESSION_LOGOFF) != 0) parts.Add("sign-out");
        return parts.Count > 0 ? string.Join(", ", parts) : $"flags 0x{flags:X8}";
    }

    private static string Origin()
    {
        var flags = InSendMessageEx(0);
        if ((flags & (ISMEX_SEND | ISMEX_NOTIFY | ISMEX_CALLBACK)) == 0)
        {
            return "posted, or sent by this thread";
        }

        var how = (flags & ISMEX_NOTIFY) != 0 ? "notify" : (flags & ISMEX_CALLBACK) != 0 ? "callback" : "sent";
        return $"{how} from another thread or process{((flags & ISMEX_REPLIED) != 0 ? " (already replied)" : string.Empty)}";
    }

    private static string? ForegroundProcess()
    {
        var foreground = GetForegroundWindow();
        if (foreground == 0)
        {
            return null;
        }

        GetWindowThreadProcessId(foreground, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return $"{process.ProcessName} ({pid})";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return $"pid {pid}";
        }
    }
}
