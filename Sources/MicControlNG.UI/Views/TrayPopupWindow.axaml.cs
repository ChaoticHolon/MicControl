using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using MicControlNG.ViewModels;

namespace MicControlNG.Views;

/// <summary>
/// Borderless quick-controls flyout shown above the notification area when the tray icon is clicked.
/// Positioned from the screen work area only (no native tray geometry), so it can be reused on KDE Plasma.
/// </summary>
public partial class TrayPopupWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly Action openFullWindow;

    public TrayPopupWindow(MainViewModel viewModel, Action openFullWindow)
    {
        this.viewModel = viewModel;
        this.openFullWindow = openFullWindow;
        DataContext = viewModel;
        InitializeComponent();
        OpenWindowButton.Click += (_, _) => OpenFullWindow();
        Deactivated += (_, _) => Hide();
        Opened += (_, _) => Reposition();
    }

    /// <summary>Shows the popup near the tray, or hides it if it's already open.</summary>
    public void Toggle()
    {
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
}
