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
}
