using Avalonia;
using MicControlNG.Settings;

namespace MicControlNG.Views;

/// <summary>A monitor as the overlay sees it, taken from Avalonia's <c>Screens</c> (screen pixels).</summary>
public sealed record OverlayScreen(PixelRect Bounds, PixelRect WorkingArea, double Scaling, bool IsPrimary);

/// <summary>Picks the overlay's monitor and its corner position. Pure, so it works the same on every platform.</summary>
public static class OverlayPlacement
{
    /// <summary>
    /// The monitor to place the overlay on: the one under the centre of <paramref name="current"/> (the overlay's
    /// rectangle, when it is visible), then the <paramref name="remembered"/> monitor if it is still connected,
    /// then the primary monitor. Null only when there are no screens.
    /// </summary>
    public static OverlayScreen? ChooseScreen(IReadOnlyList<OverlayScreen> screens, WindowBounds? remembered, PixelRect? current)
    {
        ArgumentNullException.ThrowIfNull(screens);
        if (current is { } rect && screens.FirstOrDefault(s => s.Bounds.Contains(rect.Center)) is { } underOverlay)
        {
            return underOverlay;
        }

        if (remembered is not null && screens.FirstOrDefault(s => Matches(s.Bounds, remembered)) is { } rememberedScreen)
        {
            return rememberedScreen;
        }

        return screens.FirstOrDefault(s => s.IsPrimary) ?? (screens.Count > 0 ? screens[0] : null);
    }

    /// <summary>Top-left position that puts an overlay of <paramref name="size"/> in a corner of the work area.</summary>
    public static PixelPoint CornerPosition(OverlayCorner corner, PixelRect workArea, PixelSize size, int margin)
    {
        var x = corner is OverlayCorner.TopLeft or OverlayCorner.BottomLeft ? workArea.X + margin : workArea.Right - size.Width - margin;
        var y = corner is OverlayCorner.TopLeft or OverlayCorner.TopRight ? workArea.Y + margin : workArea.Bottom - size.Height - margin;
        return new PixelPoint(Math.Max(workArea.X, x), Math.Max(workArea.Y, y));
    }

    public static WindowBounds ToBounds(PixelRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static bool Matches(PixelRect rect, WindowBounds bounds) =>
        rect.X == bounds.Left && rect.Y == bounds.Top && rect.Width == bounds.Width && rect.Height == bounds.Height;
}
