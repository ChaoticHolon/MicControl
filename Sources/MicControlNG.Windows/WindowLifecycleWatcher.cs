using System.Diagnostics;
using MicControlNG.Platform;
using static MicControlNG.Windows.NativeMethods;

namespace MicControlNG.Windows;

/// <summary>
/// Subclasses a window to report close and session-end messages: who sent them (sent from another thread or
/// process, or posted), their flags, and which process was in the foreground. Used to find out what closes a
/// hidden tray app, and to keep toolkit helper windows alive.
/// </summary>
internal sealed class WindowLifecycleWatcher : IDisposable
{
    // One shared delegate keeps the native callback alive for every subclassed window.
    private static readonly SubclassProc Proc = OnMessage;
    private static readonly Dictionary<nint, WindowLifecycleWatcher> Watchers = [];
    private static nuint nextId;

    private readonly nint handle;
    private readonly nuint id;
    private readonly string window;
    private readonly Func<WindowLifecycleMessage, bool> handler;

    public WindowLifecycleWatcher(nint handle, string window, Func<WindowLifecycleMessage, bool> handler)
    {
        this.handle = handle;
        this.window = window;
        this.handler = handler;
        id = ++nextId;
        if (handle != 0 && SetWindowSubclass(handle, Proc, id, 0))
        {
            Watchers[handle] = this;
        }
    }

    public void Dispose()
    {
        if (Watchers.Remove(handle))
        {
            RemoveWindowSubclass(handle, Proc, id);
        }
    }

    private static nint OnMessage(nint hWnd, uint msg, nint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData)
    {
        if (Watchers.TryGetValue(hWnd, out var watcher) && Describe(msg, wParam, lParam) is { } info)
        {
            var message = new WindowLifecycleMessage(info.Kind, watcher.window, info.Name, info.Details, Origin(), ForegroundProcess());
            if (watcher.handler(message))
            {
                return 0;
            }

            if (msg == WM_DESTROY)
            {
                watcher.Dispose();
            }
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private static (WindowLifecycleKind Kind, string Name, string Details)? Describe(uint msg, nint wParam, nint lParam) => msg switch
    {
        WM_CLOSE => (WindowLifecycleKind.CloseRequest, "WM_CLOSE", string.Empty),
        WM_SYSCOMMAND when ((int)wParam & 0xFFF0) == SC_CLOSE => (WindowLifecycleKind.CloseRequest, "WM_SYSCOMMAND", "SC_CLOSE"),
        WM_QUERYENDSESSION => (WindowLifecycleKind.SessionEndQuery, "WM_QUERYENDSESSION", EndSessionFlags(lParam)),
        WM_ENDSESSION => (WindowLifecycleKind.SessionEnd, "WM_ENDSESSION", $"{(wParam != 0 ? "ending" : "not ending")}; {EndSessionFlags(lParam)}"),
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
