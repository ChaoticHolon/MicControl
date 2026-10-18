using Avalonia.Headless.XUnit;
using MicControl.Platform;
using MicControl.Services;
using MicControl.Settings;
using MicControl.Views;
using Microsoft.Extensions.Logging.Abstractions;

namespace MicControl.Tests.Ui;

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

    [AvaloniaFact]
    public void ClosedOverlayAndPopup_IgnoreLaterChanges_InsteadOfCrashing()
    {
        using var harness = new TestHarness();
        var shutdown = new AppShutdown();
        var overlay = new OverlayWindow(harness.ViewModel, harness.Settings, new FakeWindowInterop(), shutdown: shutdown);
        var popup = new TrayPopupWindow(harness.ViewModel, () => { }, shutdown: shutdown);
        overlay.SyncVisibility();
        popup.Show();

        // As when Windows destroys them with their hidden owner: the windows are gone without being asked.
        shutdown.BeginExit("test");
        overlay.Close();
        popup.Close();

        // Used to throw "Cannot re-show a closed window" on the next mute change.
        harness.ViewModel.OverlayVisibility = OverlayVisibilityMode.Never;
        harness.ViewModel.OverlayVisibility = OverlayVisibilityMode.Always;
        overlay.SyncVisibility();
        popup.Toggle();
        Assert.False(overlay.IsVisible);
        Assert.False(popup.IsVisible);
    }

    [AvaloniaFact]
    public void TrayMode_ExternalCloseOfTheHiddenMainWindow_IsRefused_WithoutTheTrayNotice()
    {
        using var harness = new TestHarness();
        var exited = false;
        var window = new MainWindow(harness.ViewModel, harness.Settings) { ExitApplication = _ => exited = true };
        var closed = false;
        window.Closed += (_, _) => closed = true;

        window.Close();

        Assert.False(closed);
        Assert.False(exited);
        Assert.True(harness.ViewModel.ShouldShowTrayNotice(), "the notice is kept for a close the user makes");
    }

    [Fact]
    public void HelperWindows_RefuseCloses_AndNewOnesAreFoundOnRescan()
    {
        var interop = new FakeWindowInterop();
        interop.HelperWindows.Add(new ToolkitHelperWindow(1, "UI message window", 1, OnUiThread: true));
        using var guard = new WindowGuard(interop, new AppShutdown(), NullLogger.Instance, () => { });

        guard.ProtectHelperWindows();
        interop.HelperWindows.Add(new ToolkitHelperWindow(2, "hidden Avalonia window", 1, OnUiThread: true));
        guard.ProtectHelperWindows();

        Assert.Equal(["UI message window", "hidden Avalonia window"], interop.Watched.Select(w => w.Window));
        Assert.All(interop.Watched, w => Assert.True(w.Handler(Message(WindowLifecycleKind.CloseRequest, w.Window))));
    }

    [Fact]
    public void SessionEnd_ExitsOnlyWhenConfirmed_NotWhenAsked()
    {
        var interop = new FakeWindowInterop();
        interop.HelperWindows.Add(new ToolkitHelperWindow(1, "UI message window", 1, OnUiThread: true));
        var shutdown = new AppShutdown();
        var exits = 0;
        using var guard = new WindowGuard(interop, shutdown, NullLogger.Instance, () =>
        {
            exits++;
            shutdown.BeginExit("the session is ending");
        });
        guard.ProtectHelperWindows();
        var handler = interop.Watched.Single().Handler;

        // The query is answered "yes" here, so the toolkit doesn't start exiting on the question.
        Assert.True(handler(Message(WindowLifecycleKind.SessionEndQuery)));
        Assert.False(handler(Message(WindowLifecycleKind.SessionEndCancelled)));
        Assert.Equal(0, exits);

        handler(Message(WindowLifecycleKind.SessionEndQuery));
        Assert.True(handler(Message(WindowLifecycleKind.SessionEnd)));
        handler(Message(WindowLifecycleKind.SessionEnd));
        Assert.Equal(1, exits);
    }

    [Fact]
    public void HelperOnAnotherThread_RefusesCloses_ButLeavesTheSessionEndToTheUiThread()
    {
        var interop = new FakeWindowInterop();
        interop.HelperWindows.Add(new ToolkitHelperWindow(1, "UI message window", 1, OnUiThread: true));
        interop.HelperWindows.Add(new ToolkitHelperWindow(2, "hidden Avalonia window on thread 7", 7, OnUiThread: false));
        var exits = 0;
        using var guard = new WindowGuard(interop, new AppShutdown(), NullLogger.Instance, () => exits++);
        guard.ProtectHelperWindows();
        var background = interop.Watched.Single(w => w.Window.EndsWith("thread 7", StringComparison.Ordinal)).Handler;

        Assert.True(background(Message(WindowLifecycleKind.CloseRequest, "hidden Avalonia window on thread 7")));
        // Its own window procedure answers the query and sees the session end; the exit runs on the UI thread.
        Assert.False(background(Message(WindowLifecycleKind.SessionEndQuery)));
        Assert.False(background(Message(WindowLifecycleKind.SessionEnd)));
        Assert.Equal(0, exits);
    }

    [Fact]
    public void HelperThatCantBeWatched_IsRetriedOnTheNextScan()
    {
        var interop = new FakeWindowInterop();
        interop.HelperWindows.Add(new ToolkitHelperWindow(1, "UI message window", 1, OnUiThread: true));
        interop.Unwatchable.Add(1);
        using var guard = new WindowGuard(interop, new AppShutdown(), NullLogger.Instance, () => { });

        guard.ProtectHelperWindows();
        Assert.Empty(interop.Watched);

        interop.Unwatchable.Clear();
        guard.ProtectHelperWindows();
        Assert.Single(interop.Watched);
    }

    private static WindowLifecycleMessage Message(WindowLifecycleKind kind, string window = "UI message window") =>
        new(kind, window, kind.ToString(), string.Empty, "sent from another thread or process", null);
}
