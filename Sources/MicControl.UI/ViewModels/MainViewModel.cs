using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicControl.Platform;
using MicControl.Services;
using MicControl.Settings;
using Microsoft.Extensions.Logging;

namespace MicControl.ViewModels;

public enum Page
{
    Home,
    Overlay,
    About,
    Hotkeys,
    Sounds,
}

public sealed record NavItem(Page Page, string Title, string Glyph)
{
    /// <summary>The first tab is the app's icon and name, and leads Home (like a browser's logo).</summary>
    public bool IsBrand => Page == Page.Home;
}

/// <summary>
/// The app's single view model. Split into partial files per page: Home (this file), CustomHotkeys, Sounds,
/// Overlay, About and Setup.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    /// <summary>Product name shown in the UI; change here to rename the app.</summary>
    public const string AppName = "MicControl";

    public const string RepositoryUrl = "https://github.com/ChaoticHolon/MicControl";

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
    private bool? lastConnected;
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
        foreach (var custom in Settings.CustomHotkeys)
        {
            CustomHotkeys.Add(CreateCustomHotkey(custom));
        }

        microphone = deviceService.CreateGroup(AudioFlow.Capture);
        speakers = deviceService.CreateGroup(AudioFlow.Render);
        // After the groups exist: the setter updates level monitoring, which reads the microphone state.
        UpdateNavItems();
        SelectedNav = NavItems[0];
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

    /// <summary>Every page, in tab order (the optional pages last, in the order of their switches); <see cref="NavItems"/> holds the ones currently shown.</summary>
    private static readonly NavItem[] AllNavItems =
    [
        new(Page.Home, AppName, string.Empty),
        new(Page.Overlay, "Overlay", ""),
        new(Page.About, "About", ""),
        new(Page.Hotkeys, "Hotkeys", ""),
        new(Page.Sounds, "Sounds", ""),
    ];

    /// <summary>The page tabs: Sounds and Hotkeys only appear while their switch (About → Advanced) is on.</summary>
    public ObservableCollection<NavItem> NavItems { get; } = [];

    [ObservableProperty]
    public partial NavItem SelectedNav { get; set; }

    public Page CurrentPage => SelectedNav.Page;

    public bool IsHomePage => CurrentPage == Page.Home;

    public bool IsHotkeysPage => CurrentPage == Page.Hotkeys;

    public bool IsSoundsPage => CurrentPage == Page.Sounds;

    public bool IsOverlayPage => CurrentPage == Page.Overlay;

    public bool IsAboutPage => CurrentPage == Page.About;

    /// <summary>Opens <paramref name="page"/>, or Home if that page is switched off.</summary>
    public void Navigate(Page page) => SelectedNav = NavItems.FirstOrDefault(n => n.Page == page) ?? NavItems[0];

    private bool IsPageShown(Page page) => page switch
    {
        Page.Sounds => Settings.Notifications.CustomSoundsEnabled,
        Page.Hotkeys => Settings.CustomHotkeysEnabled,
        _ => true,
    };

    /// <summary>Adds or removes the optional pages, leaving the rest (and the selection) alone.</summary>
    private void UpdateNavItems()
    {
        if (SelectedNav is not null && !IsPageShown(SelectedNav.Page))
        {
            Navigate(Page.Home);
        }

        var shown = AllNavItems.Where(n => IsPageShown(n.Page)).ToList();
        for (var i = NavItems.Count - 1; i >= 0; i--)
        {
            if (!shown.Contains(NavItems[i]))
            {
                NavItems.RemoveAt(i);
            }
        }

        for (var i = 0; i < shown.Count; i++)
        {
            if (i >= NavItems.Count || NavItems[i] != shown[i])
            {
                NavItems.Insert(i, shown[i]);
            }
        }
    }

    [RelayCommand]
    private void GoToHotkeys() => Navigate(Page.Hotkeys);

    partial void OnSelectedNavChanged(NavItem value)
    {
        LogNavigated(value.Page);
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(IsHomePage));
        OnPropertyChanged(nameof(IsHotkeysPage));
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

            LogSettingChanged(nameof(SelectedMicrophoneId), value);
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

    /// <summary>
    /// The window and tray icon for <see cref="CurrentIcon"/>: the built-in .ico, which has a drawing for each small
    /// size so the tray stays sharp, or the user's own overlay icon.
    /// </summary>
    public WindowIcon CurrentWindowIcon => IsMuted == true
        ? ReferenceEquals(MutedIcon, DefaultMutedIcon) ? DefaultMutedWindowIcon.Value : new WindowIcon(MutedIcon)
        : ReferenceEquals(UnmutedIcon, DefaultUnmutedIcon) ? DefaultUnmutedWindowIcon.Value : new WindowIcon(UnmutedIcon);

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

            OnPropertyChanged(nameof(IsPushToTalkMode));
            OnPropertyChanged(nameof(IsToggleMode));
            OnPropertyChanged(nameof(IsPushToMuteMode));
            OnPropertyChanged(nameof(MicrophoneStatusDetail));
            OnPropertyChanged(nameof(MainHotkeyLabel));
            OnPropertyChanged(nameof(MainHotkeyDescription));
            OnPropertyChanged(nameof(MainHotkeySummary));
            OnPropertyChanged(nameof(ModeDescription));
            OnPropertyChanged(nameof(GetStartedText));
            foreach (var custom in CustomHotkeys)
            {
                custom.RefreshActions();
            }

            if (MuteRules.InitialMute(value, InitialState) is { } mute)
            {
                microphone.Mute = mute;
            }
        }
    }

    public string ModeDescription => MuteModes.First(m => Equals(m.Value, MuteMode)).Description ?? string.Empty;

    // Home's segmented Mode control (like Theme): choosing one sets MuteMode; unchecking is ignored.
    public bool IsPushToTalkMode
    {
        get => MuteMode == MuteMode.PushToTalk;
        set => SelectMode(value, MuteMode.PushToTalk);
    }

    public bool IsToggleMode
    {
        get => MuteMode == MuteMode.ToggleMute;
        set => SelectMode(value, MuteMode.ToggleMute);
    }

    public bool IsPushToMuteMode
    {
        get => MuteMode == MuteMode.PushToMute;
        set => SelectMode(value, MuteMode.PushToMute);
    }

    private void SelectMode(bool selected, MuteMode mode)
    {
        if (selected)
        {
            MuteMode = mode;
        }
    }

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

    /// <summary>What the main key does, for the sidebar's status card, e.g. "Hold F13 to talk".</summary>
    public string MainHotkeySummary
    {
        get
        {
            var keys = MainHotkey.Settings;
            if (keys.IsEmpty)
            {
                return "No hotkey set";
            }

            var key = keys.Key.IsEmpty ? keys.AlternativeKey : keys.Key;
            return MuteMode switch
            {
                MuteMode.PushToTalk => $"Hold {key} to talk",
                MuteMode.PushToMute => $"Hold {key} to mute",
                _ => $"Press {key} to switch",
            };
        }
    }

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
            LogMicrophoneVolumeSet(value);
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
                OnPropertyChanged(nameof(IsSystemTheme));
                OnPropertyChanged(nameof(IsLightTheme));
                OnPropertyChanged(nameof(IsDarkTheme));
            }
        }
    }

    // Home's segmented Theme control: one of these is on; choosing one sets Theme (unchecking is ignored).
    public bool IsSystemTheme
    {
        get => Theme == AppTheme.System;
        set => SelectTheme(value, AppTheme.System);
    }

    public bool IsLightTheme
    {
        get => Theme == AppTheme.Light;
        set => SelectTheme(value, AppTheme.Light);
    }

    public bool IsDarkTheme
    {
        get => Theme == AppTheme.Dark;
        set => SelectTheme(value, AppTheme.Dark);
    }

    private void SelectTheme(bool selected, AppTheme theme)
    {
        if (selected)
        {
            Theme = theme;
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
                LogSwitchChanged(nameof(RunAtStartup), value);
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                LogAutostartChangeFailed(value, ex);
                StatusMessage = ex.Message;
            }

            OnPropertyChanged();
        }
    }

    public AppMode AppMode
    {
        get => Settings.Window.AppMode;
        set
        {
            if (SetAndSave(Settings.Window.AppMode, value, v => Settings.Window.AppMode = v))
            {
                OnPropertyChanged(nameof(IsTrayMode));
                OnPropertyChanged(nameof(CloseToTrayDescription));
            }
        }
    }

    /// <summary>Home's "Close to the tray" switch: on is Tray mode, off is Window mode.</summary>
    public bool IsTrayMode
    {
        get => AppMode == AppMode.Tray;
        set => AppMode = value ? AppMode.Tray : AppMode.Window;
    }

    /// <summary>
    /// A new user's first run of an installed copy starts with Windows by default (they can turn it off in setup).
    /// Development and portable builds never register themselves, and existing users keep their choice.
    /// </summary>
    public void ApplyFirstRunDefaults()
    {
        if (!settingsService.IsFirstRun || !updates.IsInstalled || startup.IsEnabled)
        {
            return;
        }

        LogFirstRunAutostart();
        RunAtStartup = true;
    }

    /// <summary>What the "Close to the tray" switch does right now.</summary>
    public string CloseToTrayDescription => IsTrayMode
        ? "Closing or minimizing the window keeps MicControl running in the tray."
        : "Closing the window exits MicControl; minimizing goes to the taskbar.";

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
        LogTrayNoticeShown();
        return true;
    }

    public void DismissTrayNoticeForever()
    {
        LogSwitchChanged(nameof(Settings.Window.TrayNoticeDismissed), true);
        Settings.Window.TrayNoticeDismissed = true;
        Save();
    }

    public string TrayNoticeText => "Right-click the tray icon for quick controls or to exit. Click or double-click it to get back to the full window.";

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
            OnPropertyChanged(nameof(MainHotkeySummary));
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
        Register(mic.Hotkey, "main", pressed => SetMute(MuteRules.OnMainHotkey(mic.MuteMode, pressed, microphone.Mute)));
        foreach (var custom in Settings.CustomHotkeys.Where(e => e.IsEnabled && Settings.CustomHotkeysEnabled))
        {
            var action = custom.Action;
            Register(custom.Hotkey, action.ToString(), pressed => RunAction(action, pressed));
            LogCustomHotkey(custom.Hotkey.Key, custom.Hotkey.AlternativeKey, action, custom.Hotkey.Suppress);
        }

        var customCount = hotkeyRegistrations.Count - 1;
        LogHotkeysApplied(mic.MuteMode, mic.Hotkey.Key, mic.Hotkey.AlternativeKey, customCount);

        void Register(HotkeySettings settings, string name, Action<bool> handler) => hotkeyRegistrations.Add(hotkeys.Register(settings, pressed =>
        {
            LogHotkeyEvent(name, pressed ? "pressed" : "released");
            handler(pressed);
        }));
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
            LogRebind(group == microphone ? "microphone" : "speakers", deviceId);
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
        if (mute != lastMute || microphone.IsConnected != lastConnected)
        {
            LogMicrophoneState(mute switch { true => "muted", false => "live", null => "unavailable" }, microphone.IsConnected, isRebinding);
            lastConnected = microphone.IsConnected;
        }

        if (!isRebinding && lastMute is not null && mute is not null && mute != lastMute && Settings.Notifications.Enabled)
        {
            _ = PlayNotification(mute.Value ? Settings.Notifications.PlayedWhenMuted : Settings.Notifications.PlayedWhenUnmuted);
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
        using var stream = AssetLoader.Open(new Uri($"avares://MicControl.UI/Assets/{name}"));
        return new Bitmap(stream);
    }

    private static WindowIcon LoadWindowIcon(string name)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://MicControl.UI/Assets/{name}"));
        return new WindowIcon(stream);
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

    /// <param name="isSlider">A slider sends a value per pixel dragged, so its changes are logged at Trace.</param>
    private bool SetAndSave<T>(T current, T value, Action<T> assign, bool isSlider = false, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return false;
        }

        assign(value);
        if (isSlider)
        {
            LogSliderChanged(propertyName, value);
        }
        else
        {
            LogSettingChanged(propertyName, value);
        }
        OnPropertyChanged(propertyName);
        Save();
        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Hotkeys applied: mode {Mode}, main {MainHotkey} / {AlternativeHotkey}, {CustomCount} custom")]
    private partial void LogHotkeysApplied(MuteMode mode, Hotkeys.HotkeyGesture mainHotkey, Hotkeys.HotkeyGesture alternativeHotkey, int customCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Audio devices: {Microphones} microphones, {Speakers} speakers; selected microphone {Selected}")]
    private partial void LogDevices(int microphones, int speakers, string selected);

    [LoggerMessage(Level = LogLevel.Information, Message = "Update check failed")]
    private partial void LogUpdateCheckFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Setting {Name} = {Value}")]
    private partial void LogSettingChanged(string? name, object? value);

    [LoggerMessage(Level = LogLevel.Information, Message = "Setting {Name} = {Value}")]
    private partial void LogSwitchChanged(string name, bool value);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Setting {Name} = {Value} (slider)")]
    private partial void LogSliderChanged(string? name, object? value);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Microphone volume set to {Percent:0}%")]
    private partial void LogMicrophoneVolumeSet(double percent);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not turn autostart {Enabled}")]
    private partial void LogAutostartChangeFailed(bool enabled, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Microphone {State} (connected: {Connected}, while rebinding: {Rebinding})")]
    private partial void LogMicrophoneState(string state, bool connected, bool rebinding);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hotkey {Name} {Event}")]
    private partial void LogHotkeyEvent(string name, string @event);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Custom hotkey {Key} / {AlternativeKey} -> {Action} (exclusive: {Suppress})")]
    private partial void LogCustomHotkey(Hotkeys.HotkeyGesture key, Hotkeys.HotkeyGesture alternativeKey, HotkeyAction action, bool suppress);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Binding {Group} to device {DeviceId}")]
    private partial void LogRebind(string group, string deviceId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Page {Page}")]
    private partial void LogNavigated(Page page);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Showing the 'still running in the tray' notice")]
    private partial void LogTrayNoticeShown();

    [LoggerMessage(Level = LogLevel.Information, Message = "First run of an installed copy: turning on Start with Windows")]
    private partial void LogFirstRunAutostart();
}
