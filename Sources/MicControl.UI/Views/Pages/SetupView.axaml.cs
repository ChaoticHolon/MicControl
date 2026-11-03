using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MicControl.ViewModels;

namespace MicControl.Views.Pages;

public partial class SetupView : UserControl
{
    public SetupView()
    {
        InitializeComponent();

        // "Hold to test" is live only while held: pointer down to up (or lost capture), or Space/Enter down to up.
        // Tunnel handlers, as the button handles the bubbling pointer events itself.
        MicTestButton.AddHandler(PointerPressedEvent, (_, _) => ViewModel?.BeginMicTest(), RoutingStrategies.Tunnel);
        MicTestButton.AddHandler(PointerReleasedEvent, (_, _) => ViewModel?.EndMicTest(), RoutingStrategies.Tunnel);
        MicTestButton.PointerCaptureLost += (_, _) => ViewModel?.EndMicTest();
        MicTestButton.LostFocus += (_, _) => ViewModel?.EndMicTest();
        MicTestButton.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Space or Key.Enter)
            {
                ViewModel?.BeginMicTest();
            }
        }, RoutingStrategies.Tunnel);
        MicTestButton.AddHandler(KeyUpEvent, (_, e) =>
        {
            if (e.Key is Key.Space or Key.Enter)
            {
                ViewModel?.EndMicTest();
            }
        }, RoutingStrategies.Tunnel);
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;
}
