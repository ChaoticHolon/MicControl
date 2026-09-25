using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicControlNG.Platform;
using MicControlNG.Services;
using MicControlNG.Settings;
using Microsoft.Extensions.Logging;

namespace MicControlNG.ViewModels;

public enum Page
{
    Home,
    ExtraHotkeys,
    Sounds,
    Overlay,
    About,
}

public sealed record NavItem(Page Page, string Title, string Glyph);

/// <summary>
/// The app's single view model. Split into partial files per page: Home (this file), ExtraHotkeys, Sounds,
/// Overlay, About and Setup.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    /// <summary>Product name shown in the UI; change here to rename the app.</summary>
    public const string AppName = "MicControl";

    public const string ProductFullName = "MicControl Next Generation";
    public const string RepositoryUrl = "https://github.com/ChaoticHolon/MicControlNG";

    private readonly SettingsService settingsService;
    private readonly IAudioDevices deviceService;
    private readonly IGlobalHotkeys hotkeys;
    private readonly NotificationSounds sounds;
    private readonly IStartupRegistration startup;
    private readonly IUpdateService updates;
    private readonly IDialogService dialogs;
    private readonly IInputLevelMonitor levelMonitor;
    private readonly ILogger<MainViewModel> logger;
    private readonly IAudioEndpointGroup microphone;
    private readonly IAudioEndpointGroup speakers;
    private readonly List<IDisposable> hotkeyRegistrations = [];
    private bool? lastMute;
    private bool isRebinding;
    private bool trayNoticeShownThisSession;

    public MainViewModel(
        SettingsService settingsService,
        IAudioDevices deviceService,
        IGlobalHotkeys hotkeys,
        NotificationSounds sounds,
        IStartupRegistration startup,
        IUpdateService updates,
        IDialogService dialogs,
        IInputLevelMonitor levelMonitor,
        PlatformCapabilities capabilities,
        ILogger<MainViewModel> logger)
    {
        this.settingsService = settingsService;
        this.deviceService = deviceService;
        this.hotkeys = hotkeys;
        this.sounds = sounds;
        this.startup = startup;
        this.updates = updates;
        this.dialogs = dialogs;
        this.levelMonitor = levelMonitor;
        this.logger = logger;
        Capabilities = capabilities;

        MainHotkey = Editor("Main hotkey", Settings.Microphone.Hotkey);
        foreach (var extra in Settings.ExtraHotkeys)
        {
            ExtraHotkeys.Add(CreateExtraHotkey(extra));
        }

        SelectedNav = NavItems[0];
        microphone = deviceService.CreateGroup(AudioFlow.Capture);
        speakers = deviceService.CreateGroup(AudioFlow.Render);
        microphone.StateChanged += (_, _) => OnMicrophoneStateChanged();
        speakers.StateChanged += (_, _) => OnSpeakerStateChanged();
        deviceService.DevicesChanged += (_, _) => RefreshDeviceLists();
        InitializeOverlay();
        InitializeLevelMeter();

        RefreshDeviceLists();
        RefreshSounds();
        RefreshIcons();

        // No notification sounds for state set up at launch (e.g. push-to-talk starting muted).
        isRebinding = true;
        Rebind(microphone, Settings.Microphone.DeviceId);
        Rebind(speakers, Settings.SpeakerDeviceId);
        if (MuteRules.InitialMute(Settings.Microphone.MuteMode, Settings.Microphone.InitialState) is { } initialMute)
        {
            microphone.Mute = initialMute;
        }

        ApplyMicrophoneVolume();
        isRebinding = false;
        ApplyHotkeys();
        lastMute = microphone.Mute;
        OnMicrophoneStateChanged();
    }

    public AppSettings Settings => settingsService.Current;

    public PlatformCapabilities Capabilities { get; }

    public string Title => AppName;

    // ---------------------------------------------------------------- Navigation

    public IReadOnlyList<NavItem> NavItems { get; } =
    [
        new(Page.Home, "Home", ""),
        new(Page.ExtraHotkeys, "Extra hotkeys", ""),
        new(Page.Sounds, "Sounds", ""),
        new(Page.Overlay, "Overlay", ""),
        new(Page.About, "About", ""),
    ];

    [ObservableProperty]
    public partial NavItem SelectedNav { get; set; }

    public Page CurrentPage => SelectedNav.Page;

    public bool IsHomePage => CurrentPage == Page.Home;

    public bool IsExtraHotkeysPage => CurrentPage == Page.ExtraHotkeys;

    public bool IsSoundsPage => CurrentPage == Page.Sounds;

    public bool IsOverlayPage => CurrentPage == Page.Overlay;

    public bool IsAboutPage => CurrentPage == Page.About;

    public void Navigate(Page page) => SelectedNav = NavItems.First(n => n.Page == page);

    [RelayCommand]
    private void GoToExtraHotkeys() => Navigate(Page.ExtraHotkeys);

    partial void OnSelectedNavChanged(NavItem value)
    {
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(IsHomePage));
        OnPropertyChanged(nameof(IsExtraHotkeysPage));
        OnPropertyChanged(nameof(IsSoundsPage));
        OnPropertyChanged(nameof(IsOverlayPage));
        OnPropertyChanged(nameof(IsAboutPage));
        UpdateLevelMonitoring();
    }

    // ---------------------------------------------------------------- Microphone status

    public ObservableCollection<AudioDeviceInfo> Microphones { get; } = [];

    public string SelectedMicrophoneId
    {
        get => Settings.Microphone.DeviceId;
        set
        {
            if (value is null || value == Settings.Microphone.DeviceId)
            {
                return;
            }

            Settings.Microphone.DeviceId = value;
            Rebind(microphone, value);
            ApplyMicrophoneVolume();
            RestartLevelMonitoring();
            Save();
        }
    }

    public bool? IsMuted => microphone.Mute;

    public bool IsMicrophoneConnected => microphone.IsConnected;

    public bool IsMutedState => IsMuted == true;

    public bool IsLiveState => IsMuted == false;

    public string StatusGlyph => IsMuted == true ? "" : "";

    public string StateLabel => IsMuted switch
    {
        true => "Muted",
        false => "Live",
        null => "No microphone",
    };

    public string MicrophoneStatus => IsMuted switch
    {
        true => "Microphone muted",
        false => "Microphone live",
        null => "No microphone connected",
    };

    public string MicrophoneStatusDetail =>
        $"{Microphones.FirstOrDefault(m => m.Id == SelectedMicrophoneId)?.Name ?? "Unknown device"} · {MuteModes.First(m => Equals(m.Value, MuteMode)).Name}";

    public string ToggleMuteText => IsMuted == true ? "Unmute" : "Mute";

    public Bitmap CurrentIcon => IsMuted == true ? MutedIcon : UnmutedIcon;

    [RelayCommand]
    private void ToggleMute() => SetMute(!(microphone.Mute ?? false));

    // ---------------------------------------------------------------- Mode and main hotkey

    public IReadOnlyList<Choice> MuteModes { get; } =
    [
        new(MuteMode.PushToTalk, "Push-to-talk", "You're muted until you hold the key"),
        new(MuteMode.ToggleMute, "Toggle", "Each key press switches between muted and live"),
        new(MuteMode.PushToMute, "Push-to-mute", "You're live until you hold the key"),
    ];

    public MuteMode MuteMode
    {
        get => Settings.Microphone.MuteMode;
        set
        {
            if (!SetAndSave(Settings.Microphone.MuteMode, value, v => Settings.Microphone.MuteMode = v))
            {
                return;
            }

            OnPropertyChanged(nameof(IsToggleMode));
            OnPropertyChanged(nameof(MicrophoneStatusDetail));
            OnPropertyChanged(nameof(MainHotkeyLabel));
            OnPropertyChanged(nameof(MainHotkeyDescription));
            OnPropertyChanged(nameof(ModeDescription));
            OnPropertyChanged(nameof(GetStartedText));
            foreach (var extra in ExtraHotkeys)
            {
                extra.RefreshActions();
            }

            if (MuteRules.InitialMute(value, InitialState) is { } mute)
            {
                microphone.Mute = mute;
            }
        }
    }

    public string ModeDescription => MuteModes.First(m => Equals(m.Value, MuteMode)).Description ?? string.Empty;

    public bool IsToggleMode => MuteMode == MuteMode.ToggleMute;

    public HotkeyEditorViewModel MainHotkey { get; }

    public bool HasMainHotkey => !MainHotkey.Settings.IsEmpty;

    public string MainHotkeyLabel => MuteMode switch
    {
        MuteMode.PushToTalk => "Push-to-talk key",
        MuteMode.PushToMute => "Push-to-mute key",
        _ => "Toggle key",
    };

    public string MainHotkeyDescription => MuteMode switch
    {
        MuteMode.PushToTalk => "Hold to talk",
        MuteMode.PushToMute => "Hold to mute yourself",
        _ => "Press to switch between muted and live",
    };

    public string GetStartedText => $"Choose a {MainHotkeyLabel.ToLowerInvariant()} to get started. Click the box below, then press the key or mouse button you want to use.";

    public IReadOnlyList<Choice> InitialStates { get; } =
    [
        new(MicrophoneState.Any, "Leave as is"),
        new(MicrophoneState.Mute, "Muted"),
        new(MicrophoneState.Unmute, "Live"),
    ];

    public MicrophoneState InitialState
    {
        get => Settings.Microphone.InitialState;
        set => SetAndSave(Settings.Microphone.InitialState, value, v => Settings.Microphone.InitialState = v);
    }

    // ---------------------------------------------------------------- Input volume

    public bool MicrophoneVolumeControlEnabled
    {
        get => Settings.Microphone.VolumeControlEnabled;
        set
        {
            if (SetAndSave(Settings.Microphone.VolumeControlEnabled, value, v => Settings.Microphone.VolumeControlEnabled = v))
            {
                Settings.Microphone.Volume = value ? microphone.Volume : null;
            }
        }
    }

    /// <summary>Microphone volume in percent.</summary>
    public double MicrophoneVolume
    {
        get => (microphone.Volume ?? 0) * 100;
        set
        {
            microphone.Volume = (float)(value / 100);
            Settings.Microphone.Volume = microphone.Volume;
            Save();
        }
    }

    // ---------------------------------------------------------------- Quick settings (Home)

    public IReadOnlyList<Choice> Themes { get; } =
    [
        new(AppTheme.System, "Use system setting"),
        new(AppTheme.Light, "Light"),
        new(AppTheme.Dark, "Dark"),
    ];

    public AppTheme Theme
    {
        get => Settings.Window.Theme;
        set
        {
            if (SetAndSave(Settings.Window.Theme, value, v => Settings.Window.Theme = v))
            {
                App.ApplyTheme(value);
            }
        }
    }

    public bool RunAtStartup
    {
        get => startup.IsEnabled;
        set
        {
            try
            {
                startup.SetEnabled(value);
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                StatusMessage = ex.Message;
            }

            OnPropertyChanged();
        }
    }

    public IReadOnlyList<Choice> AppModes { get; } =
    [
        new(AppMode.Tray, "Tray", "Runs in the notification area. Closing or minimizing the window keeps it running there."),
        new(AppMode.Window, "Window", "A normal app. Closing the window exits; minimizing goes to the taskbar."),
    ];

    public AppMode AppMode
    {
        get => Settings.Window.AppMode;
        set
        {
            if (SetAndSave(Settings.Window.AppMode, value, v => Settings.Window.AppMode = v))
            {
                OnPropertyChanged(nameof(IsTrayMode));
                OnPropertyChanged(nameof(AppModeDescription));
            }
        }
    }

    public bool IsTrayMode => AppMode == AppMode.Tray;

    public string AppModeDescription => AppModes.First(m => Equals(m.Value, AppMode)).Description ?? string.Empty;

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [RelayCommand]
    private void DismissStatus() => StatusMessage = null;

    /// <summary>
    /// Whether to show the "still running in the tray" notice when the window hides to the tray:
    /// once per session, unless the user chose "Don't show again".
    /// </summary>
    public bool ShouldShowTrayNotice()
    {
        if (Settings.Window.TrayNoticeDismissed || trayNoticeShownThisSession)
        {
            return false;
        }

        trayNoticeShownThisSession = true;
        return true;
    }

    public void DismissTrayNoticeForever()
    {
        Settings.Window.TrayNoticeDismissed = true;
        Save();
    }

    public string TrayNoticeText => $"{AppName} is still running in the tray. Right-click the tray icon to exit.";

    public void Dispose()
    {
        hotkeyRegistrations.ForEach(r => r.Dispose());
        StopLevelMonitoring();
        microphone.Dispose();
        speakers.Dispose();
    }

    // ---------------------------------------------------------------- Internals

    private HotkeyEditorViewModel Editor(string title, HotkeySettings settings) => new(
        title,
        settings,
        () =>
        {
            OnPropertyChanged(nameof(HasMainHotkey));
            ApplyHotkeys();
            Save();
        },
        Capabilities.CanSuppressHotkeys,
        Capabilities.SupportsMouseHotkeys);

    private void ApplyHotkeys()
    {
        hotkeyRegistrations.ForEach(r => r.Dispose());
        hotkeyRegistrations.Clear();
        var mic = Settings.Microphone;
        Register(mic.Hotkey, pressed => SetMute(MuteRules.OnMainHotkey(mic.MuteMode, pressed, microphone.Mute)));
        foreach (var extra in Settings.ExtraHotkeys.Where(e => e.IsEnabled))
        {
            var action = extra.Action;
            Register(extra.Hotkey, pressed => RunAction(action, pressed));
        }

        var extraCount = hotkeyRegistrations.Count - 1;
        LogHotkeysApplied(mic.MuteMode, mic.Hotkey.Key, mic.Hotkey.AlternativeKey, extraCount);

        void Register(HotkeySettings settings, Action<bool> handler) => hotkeyRegistrations.Add(hotkeys.Register(settings, handler));
    }

    private void SetMute(bool? mute)
    {
        if (mute is { } value)
        {
            microphone.Mute = value;
        }
    }

    private void Rebind(IAudioEndpointGroup group, string deviceId)
    {
        var wasRebinding = isRebinding;
        isRebinding = true;
        try
        {
            if (group.DeviceId == deviceId)
            {
                group.Rebind();
            }
            else
            {
                group.DeviceId = deviceId;
            }
        }
        finally
        {
            isRebinding = wasRebinding;
        }
    }

    private void ApplyMicrophoneVolume()
    {
        if (Settings.Microphone is { VolumeControlEnabled: true, Volume: { } volume })
        {
            microphone.Volume = volume;
        }
    }

    private void OnMicrophoneStateChanged()
    {
        var mute = microphone.Mute;
        if (!isRebinding && lastMute is not null && mute is not null && mute != lastMute && Settings.Notifications.Enabled)
        {
            _ = PlayNotification(mute.Value ? Settings.Notifications.WhenMuted : Settings.Notifications.WhenUnmuted);
        }

        lastMute = mute ?? lastMute;
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(IsMicrophoneConnected));
        OnPropertyChanged(nameof(IsMutedState));
        OnPropertyChanged(nameof(IsLiveState));
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(MicrophoneStatus));
        OnPropertyChanged(nameof(MicrophoneStatusDetail));
        OnPropertyChanged(nameof(ToggleMuteText));
        OnPropertyChanged(nameof(CurrentIcon));
        OnPropertyChanged(nameof(IsOverlayVisible));
        OnPropertyChanged(nameof(MicrophoneVolume));
        OnPropertyChanged(nameof(IsSpeaking));
    }

    private void RefreshDeviceLists()
    {
        Replace(Microphones, [new(AppSettings.AllDevices, "All microphones"), .. deviceService.GetDevices(AudioFlow.Capture)], SelectedMicrophoneId, nameof(SelectedMicrophoneId));
        var outputs = deviceService.GetDevices(AudioFlow.Render);
        Replace(Speakers, [new(AppSettings.AllDevices, "All speakers"), .. outputs], SelectedSpeakerId, nameof(SelectedSpeakerId));
        var playbackId = Settings.Notifications.OutputDeviceId;
        PlaybackDevices.Clear();
        PlaybackDevices.Add(new(string.Empty, "Default device"));
        outputs.ToList().ForEach(d => PlaybackDevices.Add(new(d.Id, d.Name)));
        if (playbackId is not null && outputs.All(d => d.Id != playbackId))
        {
            PlaybackDevices.Add(new(playbackId, "Disconnected device"));
        }

        Settings.Notifications.OutputDeviceId = playbackId;
        OnPropertyChanged(nameof(PlaybackDeviceId));
        OnPropertyChanged(nameof(MicrophoneStatusDetail));
        LogDevices(Microphones.Count - 1, outputs.Count, SelectedMicrophoneId);

        void Replace(ObservableCollection<AudioDeviceInfo> target, AudioDeviceInfo[] items, string selectedId, string selectedProperty)
        {
            target.Clear();
            items.ToList().ForEach(target.Add);
            // Keep a remembered but currently unplugged device selectable so the choice isn't lost.
            if (target.All(d => d.Id != selectedId))
            {
                target.Add(new AudioDeviceInfo(selectedId, "Disconnected device"));
            }

            OnPropertyChanged(selectedProperty);
        }
    }

    private static Bitmap LoadAsset(string name)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://MicControlNG.UI/Assets/{name}"));
        return new Bitmap(stream);
    }

    private static Bitmap? LoadImage(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return new Bitmap(path);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private void Save() => settingsService.ScheduleSave();

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private bool SetAndSave<T>(T current, T value, Action<T> assign, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return false;
        }

        assign(value);
        OnPropertyChanged(propertyName);
        Save();
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Hotkeys applied: mode {Mode}, main {MainHotkey} / {AlternativeHotkey}, {ExtraCount} extra")]
    private partial void LogHotkeysApplied(MuteMode mode, Hotkeys.HotkeyGesture mainHotkey, Hotkeys.HotkeyGesture alternativeHotkey, int extraCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Audio devices: {Microphones} microphones, {Speakers} speakers; selected microphone {Selected}")]
    private partial void LogDevices(int microphones, int speakers, string selected);

    [LoggerMessage(Level = LogLevel.Information, Message = "Update check failed")]
    private partial void LogUpdateCheckFailed(Exception exception);
}
