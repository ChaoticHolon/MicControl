using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MicControl.Settings;
using MicControl.Views;

namespace MicControl.Tests.Ui;

public sealed class AppModeWindowTests
{
    [AvaloniaFact]
    public void TrayMode_CloseHidesToTray_AndDoesNotExit()
    {
        using var harness = new TestHarness(s => s.Window.TrayNoticeDismissed = true);
        var exited = false;
        var window = new MainWindow(harness.ViewModel, harness.Settings) { ExitApplication = _ => exited = true };
        window.Show();

        window.Close();

        Assert.False(window.IsVisible);
        Assert.False(exited);
        window.Show();
        Assert.True(window.IsVisible, "the window was hidden, not closed");
    }

    [AvaloniaFact]
    public void TrayMode_MinimizeHidesToTray()
    {
        using var harness = new TestHarness(s => s.Window.TrayNoticeDismissed = true);
        var window = new MainWindow(harness.ViewModel, harness.Settings) { ExitApplication = _ => { } };
        window.Show();

        window.WindowState = WindowState.Minimized;

        Assert.False(window.IsVisible);
        Assert.Equal(WindowState.Normal, window.WindowState);
    }

    [AvaloniaFact]
    public void WindowMode_CloseExits_AndMinimizeGoesToTaskbar()
    {
        using var harness = new TestHarness(s => s.Window.AppMode = AppMode.Window);
        var exited = false;
        var window = new MainWindow(harness.ViewModel, harness.Settings) { ExitApplication = _ => exited = true };
        window.Show();

        window.WindowState = WindowState.Minimized;
        Assert.True(window.IsVisible);
        Assert.Equal(WindowState.Minimized, window.WindowState);

        window.WindowState = WindowState.Normal;
        window.Close();
        Assert.True(exited);
    }

    [AvaloniaFact]
    public void TrayIconClick_OpensTheFullWindow_WithoutChangingTheMuteState()
    {
        using var harness = new TestHarness();
        var opened = 0;
        using var tray = new MicControl.Views.TrayIcon(Avalonia.Application.Current!, harness.ViewModel, () => opened++, () => { },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MicControl.Views.TrayIcon>.Instance);
        var muted = harness.ViewModel.IsMuted;

        tray.ClickCommand!.Execute(null);
        tray.ClickCommand.Execute(null); // a double-click arrives as two clicks; showing an open window again is harmless

        Assert.Equal(2, opened);
        Assert.Equal(muted, harness.ViewModel.IsMuted);
    }
}
