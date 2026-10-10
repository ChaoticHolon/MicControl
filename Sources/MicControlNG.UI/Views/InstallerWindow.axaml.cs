using Avalonia.Controls;
using MicControlNG.ViewModels;

namespace MicControlNG.Views;

public partial class InstallerWindow : Window
{
    public InstallerWindow(InstallerViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
        InitializeComponent();
        viewModel.CloseRequested += (_, _) => Close();
        // While working, closing would leave a half-finished install.
        Closing += (_, e) => e.Cancel = viewModel.IsWorkingPage;
    }
}
