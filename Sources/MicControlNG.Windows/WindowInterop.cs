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

    public IReadOnlyList<nint> FindToolkitHelperWindows()
    {
        // Avalonia's hidden top-level window that receives dispatcher wake-ups, dispatcher timers and tray icon
        // messages. Its class name is "AvaloniaMessageWindow <guid>".
        var found = new List<nint>();
        var buffer = new char[256];
        EnumThreadWindows(GetCurrentThreadId(), (hWnd, _) =>
        {
            var length = GetClassName(hWnd, buffer, buffer.Length);
            if (length > 0 && new string(buffer, 0, length).StartsWith("AvaloniaMessageWindow", StringComparison.Ordinal))
            {
                found.Add(hWnd);
            }

            return true;
        }, 0);
        return found;
    }
}
