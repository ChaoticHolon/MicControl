using Avalonia;
using Avalonia.Controls;

namespace MicControlNG.Views;

internal static class WindowPlacement
{
    /// <summary>False when the window's position is off every monitor (e.g. a display was disconnected).</summary>
    public static bool IsOnScreen(Window window, Size size)
    {
        var rect = new PixelRect(window.Position, PixelSize.FromSize(size, window.RenderScaling));
        return window.Screens.All.Any(s => s.Bounds.Intersects(rect));
    }

    public static PixelPoint CenterOfPrimary(Window window, Size size)
    {
        var area = PrimaryWorkingArea(window);
        var pixels = PixelSize.FromSize(size, window.RenderScaling);
        return new PixelPoint(area.X + ((area.Width - pixels.Width) / 2), area.Y + ((area.Height - pixels.Height) / 2));
    }

    public static PixelRect PrimaryWorkingArea(Window window)
    {
        var screens = window.Screens;
        var screen = screens.Primary ?? (screens.All.Count > 0 ? screens.All[0] : null);
        return screen?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
    }
}
