using System.Runtime.InteropServices;
using MicControl.Platform;
using static MicControl.Windows.NativeMethods;

namespace MicControl.Windows;

public sealed class WindowInterop : IWindowInterop
{
    public void ConfigureOverlay(nint handle, bool clickThrough)
    {
        if (handle == 0)
        {
            return;
        }

        var style = (int)GetWindowLongPtr(handle, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
        style = clickThrough ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(handle, GWL_EXSTYLE, style);
    }

    public bool HideTitleBarText(nint handle)
    {
        // With the client area extended into the frame, Windows still draws the caption's icon and title under the
        // app's header unless told not to.
        var options = new WTA_OPTIONS { Flags = WTNCA_NODRAWCAPTION | WTNCA_NODRAWICON, Mask = WTNCA_NODRAWCAPTION | WTNCA_NODRAWICON };
        return handle != 0 && SetWindowThemeAttribute(handle, WTA_NONCLIENT, ref options, (uint)Marshal.SizeOf<WTA_OPTIONS>()) == 0;
    }

    public IDisposable? WatchLifecycle(nint handle, string window, Func<WindowLifecycleMessage, bool> handler) =>
        WindowLifecycleWatcher.TryStart(handle, window, handler);

    public IReadOnlyList<ToolkitHelperWindow> FindToolkitHelperWindows()
    {
        // Every thread of this process, not just the UI thread: Avalonia also keeps hidden windows on other threads
        // (e.g. its renderer's), and a WM_CLOSE posted to all of a process's windows (taskkill) destroys those too.
        var found = new List<ToolkitHelperWindow>();
        var buffer = new char[256];
        var uiThread = GetCurrentThreadId();
        EnumWindows((hWnd, _) =>
        {
            var thread = GetWindowThreadProcessId(hWnd, out var pid);
            if (pid == Environment.ProcessId)
            {
                var length = GetClassName(hWnd, buffer, buffer.Length);
                if (length > 0 && HelperName(new string(buffer, 0, length)) is { } name)
                {
                    var onUiThread = thread == uiThread;
                    found.Add(new ToolkitHelperWindow(hWnd, onUiThread ? name : $"{name} on thread {thread}", (int)thread, onUiThread));
                }
            }

            return true;
        }, 0);
        return found;
    }

    private static string? HelperName(string className) =>
        // "AvaloniaMessageWindow <guid>": dispatcher wake-ups, dispatcher timers and tray icon messages.
        className.StartsWith("AvaloniaMessageWindow", StringComparison.Ordinal) ? "UI message window"
        // "AvaloniaSimpleWindow-<guid>": Avalonia's other hidden windows. On the UI thread, among others the owner of
        // windows kept off the taskbar (the overlay): Windows destroys owned windows with their owner.
        : className.StartsWith("AvaloniaSimpleWindow", StringComparison.Ordinal) ? "hidden Avalonia window"
        // Each thread's default input method (IME) windows, created by Windows for text input.
        : className is "IME" or "MSCTFIME UI" ? "input method window"
        : null;
}
