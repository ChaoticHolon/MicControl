using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using MicControl.ViewModels;
using Microsoft.Extensions.Logging;

namespace MicControl.Views;

/// <summary>Notification-area icon, always shown. Left-click (or double-click) opens the full window; right-click shows the menu.</summary>
public sealed partial class TrayIcon : IDisposable
{
    private static readonly HashSet<string> MenuProperties =
    [
        nameof(MainViewModel.IsMuted), nameof(MainViewModel.MuteMode), nameof(MainViewModel.SelectedMicrophoneId),
        nameof(MainViewModel.OverlayVisibility), nameof(MainViewModel.SoundsEnabled), nameof(MainViewModel.RunAtStartup),
        nameof(MainViewModel.AppMode), nameof(MainViewModel.IsOverlayEditing),
    ];

    private readonly Avalonia.Controls.TrayIcon icon;
    private readonly MainViewModel viewModel;
    private readonly Action showWindow;
    private readonly Action exit;
    private readonly ILogger logger;

    /// <param name="showWindow">Left-click: never changes state, only opens the full window.</param>
    public TrayIcon(Application application, MainViewModel viewModel, Action showWindow, Action exit, ILogger<TrayIcon> logger)
    {
        this.viewModel = viewModel;
        this.showWindow = showWindow;
        this.exit = exit;
        this.logger = logger;
        icon = new Avalonia.Controls.TrayIcon
        {
            Command = new RelayCommand(() =>
            {
                LogClicked(logger);
                showWindow();
            }),
            IsVisible = true,
        };
        UpdateIcon();
        RebuildMenu();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.Microphones.CollectionChanged += (_, _) => RebuildMenu();
        Avalonia.Controls.TrayIcon.SetIcons(application, [icon]);
    }

    /// <summary>What a left-click on the icon runs (for tests).</summary>
    internal System.Windows.Input.ICommand? ClickCommand => icon.Command;

    public void Dispose()
    {
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        icon.Dispose();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.CurrentIcon) or nameof(MainViewModel.MicrophoneStatus))
        {
            UpdateIcon();
        }

        if (e.PropertyName is not null && MenuProperties.Contains(e.PropertyName))
        {
            RebuildMenu();
        }
    }

    private void UpdateIcon()
    {
        icon.Icon = viewModel.CurrentWindowIcon;
        icon.ToolTipText = $"{MainViewModel.AppName}: {viewModel.MicrophoneStatus}";
    }

    private void RebuildMenu()
    {
        var vm = viewModel;
        icon.Menu =
        [
            Item($"Open {MainViewModel.AppName}", showWindow),
            Item(vm.IsMuted == true ? "Unmute microphone" : "Mute microphone", vm.ToggleMuteCommand.Execute),
            new NativeMenuItemSeparator(),
            Submenu("Mode", vm.MuteModes.Select(m => Radio(m.Name, Equals(m.Value, vm.MuteMode), () => vm.MuteMode = (MuteMode)m.Value!))),
            Submenu("Microphone", vm.Microphones.Select(d => Radio(d.Name, d.Id == vm.SelectedMicrophoneId, () => vm.SelectedMicrophoneId = d.Id))),
            Submenu("Overlay",
            [
                .. vm.OverlayVisibilityModes.Select(m => Radio(m.Name, Equals(m.Value, vm.OverlayVisibility), () => vm.OverlayVisibility = (OverlayVisibilityMode)m.Value!)),
                new NativeMenuItemSeparator(),
                Check("Edit position", vm.IsOverlayEditing, () => vm.IsOverlayEditing = !vm.IsOverlayEditing),
            ]),
            Check("Play sounds", vm.SoundsEnabled, () => vm.SoundsEnabled = !vm.SoundsEnabled),
            new NativeMenuItemSeparator(),
            Submenu("App mode", vm.AppModes.Select(m => Radio(m.Name, Equals(m.Value, vm.AppMode), () => vm.AppMode = (Settings.AppMode)m.Value!))),
            Check("Start with Windows", vm.RunAtStartup, () => vm.RunAtStartup = !vm.RunAtStartup),
            new NativeMenuItemSeparator(),
            Item("Exit", exit),
        ];
    }

    private NativeMenuItem Item(string header, Action action) => new(header) { Command = Logged(header, action) };

    private NativeMenuItem Item(string header, Action<object?> action) => new(header) { Command = Logged(header, () => action(null)) };

    private NativeMenuItem Radio(string header, bool isChecked, Action action) => new(header)
    {
        ToggleType = MenuItemToggleType.Radio,
        IsChecked = isChecked,
        Command = Logged(header, action),
    };

    private NativeMenuItem Check(string header, bool isChecked, Action action) => new(header)
    {
        ToggleType = MenuItemToggleType.CheckBox,
        IsChecked = isChecked,
        Command = Logged(header, action),
    };

    private RelayCommand Logged(string header, Action action) => new(() =>
    {
        LogMenuItem(logger, header);
        action();
    });

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tray icon clicked")]
    private static partial void LogClicked(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tray menu: {Item}")]
    private static partial void LogMenuItem(ILogger logger, string item);

    private static NativeMenuItem Submenu(string header, IEnumerable<NativeMenuItemBase> items)
    {
        var menu = new NativeMenu();
        foreach (var item in items)
        {
            menu.Items.Add(item);
        }

        return new NativeMenuItem(header) { Menu = menu };
    }
}
