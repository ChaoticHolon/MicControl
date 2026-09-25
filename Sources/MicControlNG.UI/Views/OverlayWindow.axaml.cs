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
        viewModel.OverlayPlacementRequested += (_, _) => Place();
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

    private void Place()
    {
        if (settings.Current.Overlay.Bounds is { } b)
        {
            Position = new PixelPoint((int)b.Left, (int)b.Top);
            if (WindowPlacement.IsOnScreen(this, Bounds.Size))
            {
                return;
            }

            settings.Current.Overlay.Bounds = null;
        }

        var area = WindowPlacement.PrimaryWorkingArea(this);
        var size = PixelSize.FromSize(Bounds.Size, RenderScaling);
        var margin = (int)(16 * RenderScaling);
        var corner = settings.Current.Overlay.Corner;
        var x = corner is OverlayCorner.TopLeft or OverlayCorner.BottomLeft ? area.X + margin : area.Right - size.Width - margin;
        var y = corner is OverlayCorner.TopLeft or OverlayCorner.TopRight ? area.Y + margin : area.Bottom - size.Height - margin;
        Position = new PixelPoint(x, y);
    }

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
