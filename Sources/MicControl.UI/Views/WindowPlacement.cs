using Avalonia;
using Avalonia.Controls;

namespace MicControl.Views;

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

    /// <summary>
    /// Shrinks the window to its monitor's working area (never below its minimum size) and moves it fully onto it, so
    /// a window larger than a small or highly scaled display (1366x768 at 125%) never has its header off screen.
    /// </summary>
    /// <returns>True when the window was resized or moved.</returns>
    public static bool FitToWorkingArea(Window window)
    {
        var screens = window.Screens;
        if ((screens.ScreenFromWindow(window) ?? screens.Primary) is not { } screen)
        {
            return false;
        }

        var area = screen.WorkingArea;
        var size = window.Bounds.Size;
        var fitted = new Size(
            Math.Max(window.MinWidth, Math.Min(size.Width, area.Width / screen.Scaling)),
            Math.Max(window.MinHeight, Math.Min(size.Height, area.Height / screen.Scaling)));
        var resized = fitted != size;
        if (resized)
        {
            (window.Width, window.Height) = (fitted.Width, fitted.Height);
        }

        var pixels = PixelSize.FromSize(fitted, screen.Scaling);
        var position = new PixelPoint(
            Math.Max(area.X, Math.Min(window.Position.X, area.Right - pixels.Width)),
            Math.Max(area.Y, Math.Min(window.Position.Y, area.Bottom - pixels.Height)));
        var moved = position != window.Position;
        if (moved)
        {
            window.Position = position;
        }

        return resized || moved;
    }

    public static PixelRect PrimaryWorkingArea(Window window)
    {
        var screens = window.Screens;
        var screen = screens.Primary ?? (screens.All.Count > 0 ? screens.All[0] : null);
        return screen?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
    }
}
