using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MicControlNG.Settings;
using MicControlNG.Views;

namespace MicControlNG.Tests.Ui;

public sealed class OverlayPlacementTests
{
    private static readonly PixelSize Badge = new(110, 52);

    // Primary 1920x1080 with a 40px bottom taskbar; a second monitor to its right and one to its left.
    private static readonly OverlayScreen Primary = Screen(0, 0, isPrimary: true);
    private static readonly OverlayScreen Right = Screen(1920, 0);
    private static readonly OverlayScreen Left = Screen(-1920, 0);

    [Theory]
    [InlineData(OverlayCorner.TopLeft, 1920 + 16, 16)]
    [InlineData(OverlayCorner.TopRight, 3840 - 110 - 16, 16)]
    [InlineData(OverlayCorner.BottomLeft, 1920 + 16, 1040 - 52 - 16)]
    [InlineData(OverlayCorner.BottomRight, 3840 - 110 - 16, 1040 - 52 - 16)]
    public void Corner_OnMonitorToTheRight(OverlayCorner corner, int x, int y)
    {
        Assert.Equal(new PixelPoint(x, y), OverlayPlacement.CornerPosition(corner, Right.WorkingArea, Badge, 16));
    }

    [Theory]
    [InlineData(OverlayCorner.TopLeft, -1920 + 16, 16)]
    [InlineData(OverlayCorner.TopRight, -110 - 16, 16)]
    [InlineData(OverlayCorner.BottomLeft, -1920 + 16, 1040 - 52 - 16)]
    [InlineData(OverlayCorner.BottomRight, -110 - 16, 1040 - 52 - 16)]
    public void Corner_OnMonitorToTheLeft(OverlayCorner corner, int x, int y)
    {
        Assert.Equal(new PixelPoint(x, y), OverlayPlacement.CornerPosition(corner, Left.WorkingArea, Badge, 16));
    }

    [Theory]
    [InlineData(0, 40, 1920, 1040, OverlayCorner.TopRight, 1920 - 110 - 16, 40 + 16)] // taskbar at the top
    [InlineData(60, 0, 1860, 1080, OverlayCorner.TopLeft, 60 + 16, 16)] // taskbar on the left
    [InlineData(0, 0, 1860, 1080, OverlayCorner.BottomRight, 1860 - 110 - 16, 1080 - 52 - 16)] // taskbar on the right
    public void Corner_StaysInsideTheWorkArea(int x, int y, int width, int height, OverlayCorner corner, int expectedX, int expectedY)
    {
        var point = OverlayPlacement.CornerPosition(corner, new PixelRect(x, y, width, height), Badge, 16);

        Assert.Equal(new PixelPoint(expectedX, expectedY), point);
    }

    [Fact]
    public void ChooseScreen_UsesTheMonitorUnderTheOverlay()
    {
        var onRight = new PixelRect(new PixelPoint(2500, 300), Badge);

        Assert.Same(Right, OverlayPlacement.ChooseScreen([Primary, Right, Left], OverlayPlacement.ToBounds(Primary.Bounds), onRight));
        Assert.Same(Left, OverlayPlacement.ChooseScreen([Primary, Right, Left], null, new PixelRect(new PixelPoint(-300, 500), Badge)));
    }

    [Fact]
    public void ChooseScreen_UsesTheRememberedMonitor_WhenConnected()
    {
        Assert.Same(Right, OverlayPlacement.ChooseScreen([Primary, Right], OverlayPlacement.ToBounds(Right.Bounds), null));
    }

    [Fact]
    public void ChooseScreen_FallsBackToPrimary_WhenTheRememberedMonitorIsGone()
    {
        var unplugged = OverlayPlacement.ToBounds(Right.Bounds);

        Assert.Same(Primary, OverlayPlacement.ChooseScreen([Left, Primary], unplugged, null));
    }

    [Fact]
    public void ChooseScreen_IgnoresAnOffScreenOverlay()
    {
        var offScreen = new PixelRect(new PixelPoint(9000, 9000), Badge);

        Assert.Same(Right, OverlayPlacement.ChooseScreen([Primary, Right], OverlayPlacement.ToBounds(Right.Bounds), offScreen));
        Assert.Same(Primary, OverlayPlacement.ChooseScreen([Right, Primary], null, offScreen));
    }

    [Fact]
    public void ChooseScreen_NoScreens_ReturnsNull()
    {
        Assert.Null(OverlayPlacement.ChooseScreen([], null, null));
    }

    [AvaloniaTheory]
    [InlineData(OverlayCorner.TopLeft)]
    [InlineData(OverlayCorner.TopRight)]
    [InlineData(OverlayCorner.BottomLeft)]
    [InlineData(OverlayCorner.BottomRight)]
    public void Overlay_OpensInItsCorner(OverlayCorner corner)
    {
        using var harness = new TestHarness(s => s.Overlay.Corner = corner);
        var overlay = new OverlayWindow(harness.ViewModel, harness.Settings, new FakeWindowInterop());

        overlay.Show();
        Dispatcher.UIThread.RunJobs();

        var screen = Assert.Single(overlay.Screens.All);
        var size = PixelSize.FromSize(overlay.ClientSize, screen.Scaling);
        var expected = OverlayPlacement.CornerPosition(corner, screen.WorkingArea, size, (int)(16 * screen.Scaling));
        Assert.Equal(expected, overlay.Position);
        overlay.Close();
    }

    private static OverlayScreen Screen(int x, int y, bool isPrimary = false) =>
        new(new PixelRect(x, y, 1920, 1080), new PixelRect(x, y, 1920, 1040), 1, isPrimary);
}
