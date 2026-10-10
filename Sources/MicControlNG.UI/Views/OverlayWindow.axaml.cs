using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using MicControlNG.Platform;
using MicControlNG.Services;
using MicControlNG.Settings;
using MicControlNG.ViewModels;

namespace MicControlNG.Views;

/// <summary>Always-on-top microphone badge. Click-through unless its position is being edited.</summary>
public partial class OverlayWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly SettingsService settings;
    private readonly IWindowInterop interop;
    private readonly bool isConstructed;

    public OverlayWindow(MainViewModel viewModel, SettingsService settings, IWindowInterop interop)
    {
        this.viewModel = viewModel;
        this.settings = settings;
        this.interop = interop;
        DataContext = viewModel;
        InitializeComponent();
        ToolTip.SetServiceEnabled(Badge, false);

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.OverlayPlacementRequested += (_, _) => Place(fromCurrentScreen: true);
        Badge.PointerPressed += OnBadgePointerPressed;
        PositionChanged += (_, _) => SaveBounds();
        Opened += (_, _) =>
        {
            Place();
            ApplyEditState();
        };
        isConstructed = true;
    }

    public void SyncVisibility()
    {
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
            Place();
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
    private void Place(bool fromCurrentScreen = false)
    {
        var overlay = settings.Current.Overlay;
        if (overlay.Bounds is { } b)
        {
            Position = new PixelPoint((int)b.Left, (int)b.Top);
            if (WindowPlacement.IsOnScreen(this, ClientSize))
            {
                return;
            }

            overlay.Bounds = null;
        }

        var current = fromCurrentScreen && IsVisible ? CurrentRect() : (PixelRect?)null;
        if (OverlayPlacement.ChooseScreen(GetScreens(), overlay.Monitor, current) is not { } screen)
        {
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
    }

    private void RememberCurrentScreen()
    {
        if (!IsVisible || OverlayPlacement.ChooseScreen(GetScreens(), null, CurrentRect()) is not { } screen)
        {
            return;
        }

        settings.Current.Overlay.Monitor = OverlayPlacement.ToBounds(screen.Bounds);
        settings.ScheduleSave();
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
        settings.ScheduleSave();
    }
}
