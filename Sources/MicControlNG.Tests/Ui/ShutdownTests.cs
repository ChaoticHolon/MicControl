using Avalonia.Headless.XUnit;
using MicControlNG.Services;
using MicControlNG.Views;

namespace MicControlNG.Tests.Ui;

public sealed class ShutdownTests
{
    [AvaloniaFact]
    public void TrayMode_CloseDuringSessionEnd_ClosesInsteadOfHiding()
    {
        using var harness = new TestHarness(s => s.Window.TrayNoticeDismissed = true);
        var shutdown = new AppShutdown();
        string? exitReason = null;
        var window = new MainWindow(harness.ViewModel, harness.Settings, shutdown: shutdown) { ExitApplication = r => exitReason = r };
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();

        Assert.True(shutdown.BeginExit("the session is ending"));
        window.Close();

        Assert.True(closed, "a session end must not be vetoed by hiding to the tray");
        Assert.Null(exitReason);
        Assert.Equal("the session is ending", shutdown.Reason);
    }

    [AvaloniaFact]
    public void OverlayAndPopup_RefuseCloses_UntilTheAppExits()
    {
        using var harness = new TestHarness();
        var shutdown = new AppShutdown();
        var overlay = new OverlayWindow(harness.ViewModel, harness.Settings, new FakeWindowInterop(), shutdown: shutdown);
        var popup = new TrayPopupWindow(harness.ViewModel, () => { }, shutdown: shutdown);
        var closed = 0;
        overlay.Closed += (_, _) => closed++;
        popup.Closed += (_, _) => closed++;
        overlay.Show();
        popup.Show();

        overlay.Close();
        popup.Close();
        Assert.Equal(0, closed);

        shutdown.BeginExit("test");
        overlay.Close();
        popup.Close();
        Assert.Equal(2, closed);
    }

    [Fact]
    public void BeginExit_KeepsTheFirstReason_AndCleanUpRunsOnce()
    {
        var shutdown = new AppShutdown();
        Assert.True(shutdown.BeginExit("first"));
        Assert.False(shutdown.BeginExit("second"));
        Assert.Equal("first", shutdown.Reason);

        var runs = 0;
        Assert.True(shutdown.CleanUpOnce(() => runs++));
        Assert.False(shutdown.CleanUpOnce(() => runs++));
        Assert.Equal(1, runs);
    }
}
