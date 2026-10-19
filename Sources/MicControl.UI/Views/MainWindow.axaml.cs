using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using MicControl.Services;
using MicControl.Settings;
using MicControl.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MicControl.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly SettingsService settings;
    private readonly ILogger logger;
    private readonly AppShutdown shutdown;
    private bool isExiting;

    public MainWindow(MainViewModel viewModel, SettingsService settings, ILogger<MainWindow>? logger = null, AppShutdown? shutdown = null)
    {
        this.viewModel = viewModel;
        this.settings = settings;
        this.shutdown = shutdown ?? new AppShutdown();
        this.logger = logger ?? NullLogger<MainWindow>.Instance;
        DataContext = viewModel;
        InitializeComponent();
        UpdateIcon();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        if (settings.Current.Window.Bounds is { } b)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            (Width, Height) = (b.Width, b.Height);
            Position = new PixelPoint((int)b.Left, (int)b.Top);
            Opened += (_, _) =>
            {
                if (!WindowPlacement.IsOnScreen(this, Bounds.Size))
                {
                    LogOffScreen(this.logger, Position);
                    Position = WindowPlacement.CenterOfPrimary(this, Bounds.Size);
                }
            };
        }

        // After the handler above, so a window moved back from a disconnected monitor is fitted to its new one.
        Opened += (_, _) =>
        {
            if (WindowPlacement.FitToWorkingArea(this))
            {
                LogFitted(this.logger, Width, Height, Position);
            }
        };
    }

    /// <summary>Set while the app is shutting down (or the session is ending) so closing isn't turned into hiding.</summary>
    public bool IsExiting
    {
        get => isExiting || shutdown.IsExiting;
        set => isExiting = value;
    }

    /// <summary>Exits the whole app with a reason for the log (Window-mode closes exit it). Set by the app; tests set a probe.</summary>
    public Action<string>? ExitApplication { get; set; }

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && logger is not null)
        {
            LogVisible(logger, IsVisible);
            viewModel.IsWindowVisible = IsVisible && WindowState != WindowState.Minimized;
        }
        else if (change.Property == WindowStateProperty && logger is not null)
        {
            LogWindowState(logger, WindowState);
            if (WindowState == WindowState.Minimized && viewModel.IsTrayMode)
            {
                // Tray mode: minimize hides to the tray instead of the taskbar.
                WindowState = WindowState.Normal;
                HideToTray();
            }

            viewModel.IsWindowVisible = IsVisible && WindowState != WindowState.Minimized;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // A hidden window's position isn't the user's (it may never have been shown).
        if (WindowState == WindowState.Normal && IsVisible)
        {
            settings.Current.Window.Bounds = new WindowBounds(Position.X, Position.Y, Width, Height);
            settings.ScheduleSave();
        }

        LogClosing(logger, IsExiting, viewModel.AppMode, IsVisible);
        if (!IsExiting)
        {
            if (!IsVisible)
            {
                // Another program closing our hidden window: the user didn't close anything, so neither hide with
                // the tray notice nor exit.
                e.Cancel = true;
                LogHiddenCloseRefused(logger);
            }
            else if (viewModel.IsTrayMode)
            {
                // Tray mode: closing hides to the tray; exit from the tray icon's menu.
                e.Cancel = true;
                HideToTray();
            }
            else if (ExitApplication is { } exit)
            {
                // Window mode: closing exits the app (which closes this window for real).
                e.Cancel = true;
                exit("window closed with Close to the tray off");
            }
        }

        base.OnClosing(e);
    }

    private void HideToTray()
    {
        Hide();
        if (viewModel.ShouldShowTrayNotice())
        {
            new TrayHintWindow(viewModel).Show();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentIcon))
        {
            UpdateIcon();
        }
    }

    private void UpdateIcon() => Icon = viewModel.CurrentWindowIcon;

    [LoggerMessage(Level = LogLevel.Debug, Message = "Main window visible: {Visible}")]
    private static partial void LogVisible(ILogger logger, bool visible);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Main window state: {State}")]
    private static partial void LogWindowState(ILogger logger, WindowState state);

    [LoggerMessage(Level = LogLevel.Information, Message = "Main window closing (exiting: {Exiting}, app mode: {AppMode}, visible: {Visible})")]
    private static partial void LogClosing(ILogger logger, bool exiting, AppMode appMode, bool visible);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a request to close the hidden main window while the app is running")]
    private static partial void LogHiddenCloseRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Main window didn't fit its monitor; now {Width:0}x{Height:0} at {Position}")]
    private static partial void LogFitted(ILogger logger, double width, double height, PixelPoint position);

    [LoggerMessage(Level = LogLevel.Information, Message = "Main window's saved position {Position} is off every monitor; centring it on the primary one")]
    private static partial void LogOffScreen(ILogger logger, PixelPoint position);
}
