using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MicControlNG.Settings;
using MicControlNG.Views;

namespace MicControlNG.Tests.Ui;

public sealed class AppModeWindowTests
{
    [AvaloniaFact]
    public void TrayMode_CloseHidesToTray_AndDoesNotExit()
    {
        using var harness = new TestHarness(s => s.Window.TrayNoticeDismissed = true);
        var exited = false;
        var window = new MainWindow(harness.ViewModel, harness.Settings) { ExitApplication = () => exited = true };
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
        var window = new MainWindow(harness.ViewModel, harness.Settings) { ExitApplication = () => { } };
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
        var window = new MainWindow(harness.ViewModel, harness.Settings) { ExitApplication = () => exited = true };
        window.Show();

        window.WindowState = WindowState.Minimized;
        Assert.True(window.IsVisible);
        Assert.Equal(WindowState.Minimized, window.WindowState);

        window.WindowState = WindowState.Normal;
        window.Close();
        Assert.True(exited);
    }

    [AvaloniaFact]
    public void TrayPopup_ShowsQuickControls_AndOpensFullWindow()
    {
        using var harness = new TestHarness(s => s.Microphone.MuteMode = MuteMode.ToggleMute);
        var opened = false;
        var popup = new TrayPopupWindow(harness.ViewModel, () => opened = true);

        popup.Toggle();
        Assert.True(popup.IsVisible);
        Assert.True(harness.ViewModel.IsPopupVisible);
        Assert.Equal(1, harness.LevelMonitor.ActiveSessions);
        Assert.Equal(2, popup.GetVisualDescendants().OfType<ComboBox>().Count());
        Assert.NotNull(popup.GetVisualDescendants().OfType<ProgressBar>().SingleOrDefault());

        var buttons = popup.GetVisualDescendants().OfType<Button>().ToList();
        var mute = buttons.Single(b => Equals(b.Content, "Mute"));
        mute.Command!.Execute(null);
        Assert.True(harness.ViewModel.IsMuted);
        Assert.Equal("Unmute", mute.Content);

        buttons.Single(b => Equals(b.Content, "Open full window")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(opened);
        Assert.False(popup.IsVisible);
        Assert.False(harness.ViewModel.IsPopupVisible);
        Assert.Equal(0, harness.LevelMonitor.ActiveSessions);
    }

    [AvaloniaFact]
    public void TrayPopup_ToggleClosesWhenOpen()
    {
        using var harness = new TestHarness();
        var popup = new TrayPopupWindow(harness.ViewModel, () => { });

        popup.Toggle();
        popup.Toggle();

        Assert.False(popup.IsVisible);
    }

    [Theory]
    [InlineData(0, 0, 1920, 1040, 1920 - 300, 1040 - 200)] // taskbar at the bottom
    [InlineData(0, 40, 1920, 1040, 1920 - 300, 40)] // taskbar at the top
    [InlineData(60, 0, 1860, 1080, 60, 1080 - 200)] // taskbar on the left
    [InlineData(0, 0, 1860, 1080, 1860 - 300, 1080 - 200)] // taskbar on the right
    public void PopupPlacement_SitsNextToTheTaskbar(int x, int y, int width, int height, int expectedX, int expectedY)
    {
        var point = PopupPlacement.Compute(new PixelRect(0, 0, 1920, 1080), new PixelRect(x, y, width, height), new PixelSize(300, 200), 0);

        Assert.Equal(new PixelPoint(expectedX, expectedY), point);
    }
}
