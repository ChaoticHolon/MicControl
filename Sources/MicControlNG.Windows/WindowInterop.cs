using MicControlNG.Platform;
using static MicControlNG.Windows.NativeMethods;

namespace MicControlNG.Windows;

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

    public IDisposable WatchLifecycle(nint handle, string window, Func<WindowLifecycleMessage, bool> handler) =>
        new WindowLifecycleWatcher(handle, window, handler);

    public IReadOnlyList<ToolkitHelperWindow> FindToolkitHelperWindows()
    {
        var found = new List<ToolkitHelperWindow>();
        var buffer = new char[256];
        EnumThreadWindows(GetCurrentThreadId(), (hWnd, _) =>
        {
            var length = GetClassName(hWnd, buffer, buffer.Length);
            if (length > 0 && HelperName(new string(buffer, 0, length)) is { } name)
            {
                found.Add(new ToolkitHelperWindow(hWnd, name));
            }

            return true;
        }, 0);
        return found;
    }

    private static string? HelperName(string className) =>
        // "AvaloniaMessageWindow <guid>": dispatcher wake-ups, dispatcher timers and tray icon messages.
        className.StartsWith("AvaloniaMessageWindow", StringComparison.Ordinal) ? "UI message window"
        // "AvaloniaSimpleWindow-<guid>": among others the hidden owner of windows kept off the taskbar (overlay,
        // tray popup). Windows destroys owned windows with their owner.
        : className.StartsWith("AvaloniaSimpleWindow", StringComparison.Ordinal) ? "hidden owner window"
        // The thread's default input method (IME) windows, created by Windows for text input.
        : className is "IME" or "MSCTFIME UI" ? "input method window"
        : null;
}
