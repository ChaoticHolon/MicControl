using Avalonia.Controls;
using Avalonia.Interactivity;
using MicControlNG.ViewModels;

namespace MicControlNG.Views;

/// <summary>"Still running in the tray" notice, shown when the window first hides to the tray.</summary>
public partial class TrayHintWindow : Window
{
    private readonly MainViewModel viewModel;

    public TrayHintWindow(MainViewModel viewModel)
    {
        this.viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        if (DontShowAgain.IsChecked == true)
        {
            viewModel.DismissTrayNoticeForever();
        }

        Close();
    }
}
