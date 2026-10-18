using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using MicControl.ViewModels;
using MicControl.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MicControl.Views;

/// <summary>
/// Borderless quick-controls flyout shown above the notification area when the tray icon is clicked.
/// Positioned from the screen work area only (no native tray geometry), so it can be reused on KDE Plasma.
/// </summary>
public partial class TrayPopupWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly Action openFullWindow;
    private readonly ILogger logger;
    private bool isClosed;

    public TrayPopupWindow(MainViewModel viewModel, Action openFullWindow, ILogger<TrayPopupWindow>? logger = null, AppShutdown? shutdown = null)
    {
        var appShutdown = shutdown ?? new AppShutdown();
        this.viewModel = viewModel;
        this.openFullWindow = openFullWindow;
        this.logger = logger ?? NullLogger<TrayPopupWindow>.Instance;
        DataContext = viewModel;
        InitializeComponent();
        // A closed window can't be shown again, so only an app exit may close it; anything else (e.g. another
        // program closing all our windows) is refused and logged.
        Closing += (_, e) =>
        {
            if (!appShutdown.IsExiting)
            {
                e.Cancel = true;
                LogCloseRefused(this.logger);
            }
        };
        Closed += (_, _) => isClosed = true;
        OpenWindowButton.Click += (_, _) => OpenFullWindow();
        Deactivated += (_, _) => Hide();
        Opened += (_, _) => Reposition();
    }

    /// <summary>Shows the popup near the tray, or hides it if it's already open.</summary>
    public void Toggle()
    {
        if (isClosed)
        {
            // Destroyed; the app is replacing it with a new popup.
            LogClosedToggle(logger);
            return;
        }

        if (IsVisible)
        {
            Hide();
            return;
        }

        Show();
        Reposition();
        Activate();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && viewModel is not null)
        {
            LogVisible(logger, IsVisible);
            viewModel.IsPopupVisible = IsVisible;
        }
        else if (change.Property == ClientSizeProperty && IsVisible)
        {
            Reposition();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Hide();
        }
    }

    private void OpenFullWindow()
    {
        LogOpenFullWindow(logger);
        Hide();
        openFullWindow();
    }

    private void Reposition()
    {
        var screen = Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
        if (screen is null)
        {
            return;
        }

        Position = PopupPlacement.Compute(screen.Bounds, screen.WorkingArea, PixelSize.FromSize(Bounds.Size, RenderScaling), 0);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tray popup visible: {Visible}")]
    private static partial void LogVisible(ILogger logger, bool visible);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tray popup: Open full window")]
    private static partial void LogOpenFullWindow(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tray popup not shown: this popup window was closed")]
    private static partial void LogClosedToggle(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a request to close the tray popup window while the app is running")]
    private static partial void LogCloseRefused(ILogger logger);
}
