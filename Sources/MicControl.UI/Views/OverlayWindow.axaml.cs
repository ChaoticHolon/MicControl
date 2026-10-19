using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using MicControl.Platform;
using MicControl.Services;
using MicControl.Settings;
using MicControl.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MicControl.Views;

/// <summary>Always-on-top microphone badge. Click-through unless its position is being edited.</summary>
public partial class OverlayWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly SettingsService settings;
    private readonly IWindowInterop interop;
    private readonly ILogger logger;
    private readonly bool isConstructed;
    private bool isClosed;
    private IDisposable? keepOnTop;

    public OverlayWindow(MainViewModel viewModel, SettingsService settings, IWindowInterop interop, ILogger<OverlayWindow>? logger = null, AppShutdown? shutdown = null)
    {
        var appShutdown = shutdown ?? new AppShutdown();
        this.viewModel = viewModel;
        this.settings = settings;
        this.interop = interop;
        this.logger = logger ?? NullLogger<OverlayWindow>.Instance;
        DataContext = viewModel;
        InitializeComponent();
        ToolTip.SetServiceEnabled(Badge, false);
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

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.OverlayPlacementRequested += OnPlacementRequested;
        Badge.PointerPressed += OnBadgePointerPressed;
        PositionChanged += (_, _) => SaveBounds();
        // A monitor was connected or disconnected: fall back to the primary one, and go back when it returns.
        Screens.Changed += (_, _) =>
        {
            if (!isClosed)
            {
                LogMonitors(changed: true);
                Place("monitors changed");
            }
        };
        // Closed for good (the app exits, or the window was destroyed and the app replaces it): stop following the
        // view model, so a closed window is never shown again.
        Closed += (_, _) =>
        {
            isClosed = true;
            keepOnTop?.Dispose();
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.OverlayPlacementRequested -= OnPlacementRequested;
        };
        Opened += (_, _) =>
        {
            LogMonitors(changed: false);
            Place("opened");
            ApplyEditState();
            keepOnTop ??= interop.KeepOnTop(TryGetPlatformHandle()?.Handle ?? 0);
            LogKeepOnTop(this.logger, keepOnTop is not null);
        };
        isConstructed = true;
    }

    public void SyncVisibility()
    {
        if (isClosed)
        {
            return;
        }

        LogVisibility(logger, viewModel.IsOverlayVisible, viewModel.OverlayVisibility, viewModel.IsMuted);
        if (viewModel.IsOverlayVisible)
        {
            Show();
        }
        else
        {
            Hide();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Property changes also arrive from the base constructor, before our fields are set.
        if (isConstructed && change.Property == ClientSizeProperty && settings.Current.Overlay.Bounds is null)
        {
            // Keep corner-anchored overlays in their corner when the size or label changes.
            Place("size changed");
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsOverlayVisible):
                SyncVisibility();
                break;
            case nameof(MainViewModel.IsOverlayEditing):
                ApplyEditState();
                if (!viewModel.IsOverlayEditing)
                {
                    // A dragged position remembers its monitor too.
                    RememberCurrentScreen();
                }

                break;
        }
    }

    private void OnPlacementRequested(object? sender, EventArgs e) => Place("corner preset", fromCurrentScreen: true);

    private void OnBadgePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!viewModel.IsOverlayEditing || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            viewModel.IsOverlayEditing = false;
        }
        else
        {
            BeginMoveDrag(e);
        }
    }

    private void ApplyEditState()
    {
        ToolTip.SetServiceEnabled(Badge, viewModel.IsOverlayEditing);
        interop.ConfigureOverlay(TryGetPlatformHandle()?.Handle ?? 0, clickThrough: !viewModel.IsOverlayEditing);
    }

    /// <param name="fromCurrentScreen">
    /// True for a corner preset click: use the monitor the overlay is on now (if it's visible) and remember it.
    /// Otherwise (startup, size changes) use the remembered monitor, falling back to the primary one.
    /// </param>
    private void Place(string reason, bool fromCurrentScreen = false)
    {
        var overlay = settings.Current.Overlay;
        if (overlay.Bounds is { } b)
        {
            Position = new PixelPoint((int)b.Left, (int)b.Top);
            if (WindowPlacement.IsOnScreen(this, ClientSize))
            {
                LogPlacedAtDraggedPosition(logger, reason, Position);
                return;
            }

            LogDraggedPositionOffScreen(logger, Position);
            overlay.Bounds = null;
        }

        var current = fromCurrentScreen && IsVisible ? CurrentRect() : (PixelRect?)null;
        if (OverlayPlacement.ChooseScreen(GetScreens(), overlay.Monitor, current) is not { } screen)
        {
            LogNoScreens(logger, reason);
            return;
        }

        if (current is not null)
        {
            // Keep the saved monitor if it's merely disconnected; only an actual placement replaces it.
            overlay.Monitor = OverlayPlacement.ToBounds(screen.Bounds);
        }

        // ClientSize, not Bounds: while the window sizes itself to its content (e.g. on open), Bounds still holds
        // the previous layout size. Scaled for the target monitor, which can differ from the one the overlay is on.
        var size = PixelSize.FromSize(ClientSize, screen.Scaling);
        var margin = (int)(16 * screen.Scaling);
        Position = OverlayPlacement.CornerPosition(overlay.Corner, screen.WorkingArea, size, margin);
        var choice = current is not null ? "overlay's current"
            : OverlayPlacement.ToBounds(screen.Bounds) == overlay.Monitor ? "remembered"
            : overlay.Monitor is null ? "primary" : "primary; remembered one not connected";
        LogPlacedInCorner(logger, reason, overlay.Corner, Position, size, screen.Bounds, choice);
    }

    private void RememberCurrentScreen()
    {
        if (!IsVisible || OverlayPlacement.ChooseScreen(GetScreens(), null, CurrentRect()) is not { } screen)
        {
            return;
        }

        settings.Current.Overlay.Monitor = OverlayPlacement.ToBounds(screen.Bounds);
        LogMonitorRemembered(logger, screen.Bounds);
        settings.ScheduleSave();
    }

    private void LogMonitors(bool changed)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        var screens = string.Join("; ", Screens.All.Select(s =>
            string.Create(CultureInfo.InvariantCulture, $"{s.Bounds} work {s.WorkingArea} scale {s.Scaling:0.##}{(s.IsPrimary ? " primary" : "")}")));
        if (changed)
        {
            LogScreensChanged(logger, screens);
        }
        else
        {
            LogScreens(logger, screens);
        }
    }

    private PixelRect CurrentRect() => new(Position, PixelSize.FromSize(ClientSize, RenderScaling));

    private List<OverlayScreen> GetScreens() =>
        [.. Screens.All.Select(s => new OverlayScreen(s.Bounds, s.WorkingArea, s.Scaling, s.IsPrimary))];

    private void SaveBounds()
    {
        // Only a user drag (edit mode) pins the overlay to an exact position.
        if (!IsVisible || !viewModel.IsOverlayEditing)
        {
            return;
        }

        settings.Current.Overlay.Bounds = new WindowBounds(Position.X, Position.Y, Width, Height);
        LogDragged(logger, Position);
        settings.ScheduleSave();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Overlay kept on top when other windows come to the front: {Watching}")]
    private static partial void LogKeepOnTop(ILogger logger, bool watching);

    [LoggerMessage(Level = LogLevel.Information, Message = "Monitors: {Screens}")]
    private static partial void LogScreens(ILogger logger, string screens);

    [LoggerMessage(Level = LogLevel.Information, Message = "Monitors changed: {Screens}")]
    private static partial void LogScreensChanged(ILogger logger, string screens);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Overlay {Visible} (visibility {Mode}, muted {Muted})")]
    private static partial void LogVisibility(ILogger logger, bool visible, OverlayVisibilityMode mode, bool? muted);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Overlay placed ({Reason}) at its dragged position {Position}")]
    private static partial void LogPlacedAtDraggedPosition(ILogger logger, string reason, PixelPoint position);

    [LoggerMessage(Level = LogLevel.Information, Message = "Overlay's dragged position {Position} is off every monitor; using its corner instead")]
    private static partial void LogDraggedPositionOffScreen(ILogger logger, PixelPoint position);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Overlay not placed ({Reason}): no monitors reported")]
    private static partial void LogNoScreens(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Overlay placed ({Reason}) in corner {Corner} at {Position}, size {Size}, on monitor {Monitor} ({Choice} monitor)")]
    private static partial void LogPlacedInCorner(ILogger logger, string reason, OverlayCorner corner, PixelPoint position, PixelSize size, PixelRect monitor, string choice);

    [LoggerMessage(Level = LogLevel.Information, Message = "Overlay monitor remembered: {Monitor}")]
    private static partial void LogMonitorRemembered(ILogger logger, PixelRect monitor);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Overlay dragged to {Position}")]
    private static partial void LogDragged(ILogger logger, PixelPoint position);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a request to close the overlay window while the app is running")]
    private static partial void LogCloseRefused(ILogger logger);
}
