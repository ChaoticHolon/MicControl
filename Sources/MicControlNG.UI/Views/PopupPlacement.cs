using Avalonia;

namespace MicControlNG.Views;

/// <summary>Places the tray popup next to the taskbar, like the Windows volume and network flyouts.</summary>
public static class PopupPlacement
{
    /// <summary>
    /// Works out which edge the taskbar is on from how the work area is inset inside the screen, then puts the
    /// popup in the corner of the work area nearest the notification area (bottom-right for a bottom taskbar).
    /// </summary>
    public static PixelPoint Compute(PixelRect screen, PixelRect workArea, PixelSize popup, int margin)
    {
        var taskbarTop = workArea.Y > screen.Y;
        var taskbarLeft = workArea.X > screen.X;
        var x = taskbarLeft ? workArea.X + margin : workArea.Right - popup.Width - margin;
        var y = taskbarTop ? workArea.Y + margin : workArea.Bottom - popup.Height - margin;
        return new PixelPoint(Math.Max(workArea.X, x), Math.Max(workArea.Y, y));
    }
}
