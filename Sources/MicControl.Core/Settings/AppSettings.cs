using System.Text.Json.Serialization;
using MicControl.Hotkeys;

namespace MicControl.Settings;

public sealed class AppSettings
{
    /// <summary>Device id meaning "every active device of this kind".</summary>
    public const string AllDevices = "all";

    /// <summary>The settings file format; 1 is the first MicControl format.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>False until the first-run setup has been completed or skipped.</summary>
    public bool SetupCompleted { get; set; }

    public MicrophoneSettings Microphone { get; set; } = new();

    /// <summary>
    /// The "Custom hotkeys" switch (About → Advanced). Off by default: <see cref="CustomHotkeys"/> are kept but paused,
    /// and their page is hidden.
    /// </summary>
    public bool CustomHotkeysEnabled { get; set; }

    /// <summary>
    /// The most <see cref="CustomHotkeys"/> kept. A hand-edited file with thousands froze the Hotkeys page (each row is
    /// a few controls) and made every key press check them all in the keyboard hook.
    /// </summary>
    public const int MaxCustomHotkeys = 100;

    /// <summary>Additional hotkeys, each bound to one action; they work only while <see cref="CustomHotkeysEnabled"/>.</summary>
    public List<CustomHotkey> CustomHotkeys { get; set; } = [];

    /// <summary>Output device that speaker actions in <see cref="CustomHotkeys"/> control.</summary>
    public string SpeakerDeviceId { get; set; } = AllDevices;

    public NotificationSettings Notifications { get; set; } = new();

    public OverlaySettings Overlay { get; set; } = new();

    public WindowSettings Window { get; set; } = new();

    public bool CheckForUpdates { get; set; } = true;

    /// <summary>How much goes into the log files. Debug for now, while the rewrite is being tested.</summary>
    public LogDetail LogLevel { get; set; } = LogDetail.Debug;

    /// <summary>
    /// Puts defaults where a hand-edited file has <c>null</c> (a section, a hotkey row, a device id), so the app
    /// never meets a missing section. Everything else in the file is kept.
    /// </summary>
    public AppSettings Normalize()
    {
        Microphone ??= new();
        Microphone.DeviceId ??= AllDevices;
        Microphone.Hotkey = Normalize(Microphone.Hotkey);
        CustomHotkeys = [.. (CustomHotkeys ?? []).OfType<CustomHotkey>()];
        CustomHotkeys.ForEach(h => h.Hotkey = Normalize(h.Hotkey));
        SpeakerDeviceId ??= AllDevices;
        Notifications ??= new();
        Overlay ??= new();
        Window ??= new();

        // Out-of-range numbers (hand edits, or a float too large such as 1e39, which reads as infinity and then can't be
        // saved) get the nearest value the UI allows, and unknown enum values their defaults.
        var defaults = new AppSettings();
        Microphone.MuteMode = Defined(Microphone.MuteMode, defaults.Microphone.MuteMode);
        Microphone.InitialState = Defined(Microphone.InitialState, defaults.Microphone.InitialState);
        Microphone.Volume = Microphone.Volume is { } volume && !float.IsNaN(volume) ? Math.Clamp(volume, 0, 1) : null;
        CustomHotkeys.RemoveAll(h => !System.Enum.IsDefined(h.Action));
        if (CustomHotkeys.Count > MaxCustomHotkeys)
        {
            CustomHotkeys.RemoveRange(MaxCustomHotkeys, CustomHotkeys.Count - MaxCustomHotkeys);
        }
        Notifications.Volume = float.IsNaN(Notifications.Volume) ? defaults.Notifications.Volume : Math.Clamp(Notifications.Volume, 0, 1);
        Overlay.Visibility = Defined(Overlay.Visibility, defaults.Overlay.Visibility);
        Overlay.LastVisibleMode = Defined(Overlay.LastVisibleMode, defaults.Overlay.LastVisibleMode);
        Overlay.Corner = Defined(Overlay.Corner, defaults.Overlay.Corner);
        Overlay.Opacity = Clamp(Overlay.Opacity, OverlaySettings.MinOpacity, OverlaySettings.MaxOpacity, defaults.Overlay.Opacity);
        Overlay.Scale = Clamp(Overlay.Scale, OverlaySettings.MinScale, OverlaySettings.MaxScale, defaults.Overlay.Scale);
        Overlay.Bounds = Valid(Overlay.Bounds);
        Overlay.Monitor = Valid(Overlay.Monitor);
        Window.Bounds = Valid(Window.Bounds);
        Window.AppMode = Defined(Window.AppMode, defaults.Window.AppMode);
        Window.Theme = Defined(Window.Theme, defaults.Window.Theme);
        LogLevel = Defined(LogLevel, defaults.LogLevel);
        return this;

        static T Defined<T>(T value, T fallback) where T : struct, System.Enum => System.Enum.IsDefined(value) ? value : fallback;

        static double Clamp(double value, double min, double max, double fallback) => double.IsNaN(value) ? fallback : Math.Clamp(value, min, max);

        // A window can't have a negative size (Avalonia throws), and infinity can't be saved.
        static WindowBounds? Valid(WindowBounds? b) =>
            b is not null && double.IsFinite(b.Left) && double.IsFinite(b.Top) && double.IsFinite(b.Width) && double.IsFinite(b.Height)
            && b.Width > 0 && b.Height > 0 ? b : null;
    }

    private static HotkeySettings Normalize(HotkeySettings? hotkey)
    {
        hotkey ??= new();
        hotkey.Key ??= HotkeyGesture.Empty;
        hotkey.AlternativeKey ??= HotkeyGesture.Empty;
        return hotkey;
    }
}

/// <summary>Log file detail. Same names and values as Microsoft.Extensions.Logging's LogLevel (Core doesn't reference it).</summary>
public enum LogDetail
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
}

public sealed class HotkeySettings
{
    public HotkeyGesture Key { get; set; } = HotkeyGesture.Empty;

    public HotkeyGesture AlternativeKey { get; set; } = HotkeyGesture.Empty;

    /// <summary>"Exclusive hotkey": other applications don't receive the key while MicControl runs.</summary>
    public bool Suppress { get; set; }

    public bool IgnoreModifiers { get; set; }

    [JsonIgnore]
    public bool IsEmpty => Key.IsEmpty && AlternativeKey.IsEmpty;
}

public enum HotkeyAction
{
    ToggleMute,
    Mute,
    Unmute,
    PushToTalk,
    PushToMute,
    SpeakerToggleMute,
    SpeakerMute,
    SpeakerUnmute,
    SpeakerVolumeUp,
    SpeakerVolumeDown,
    ToggleOverlay,
}

public sealed class CustomHotkey
{
    public HotkeyAction Action { get; set; }

    public bool IsEnabled { get; set; } = true;

    public HotkeySettings Hotkey { get; set; } = new();
}

public sealed class MicrophoneSettings
{
    public string DeviceId { get; set; } = AppSettings.AllDevices;

    public MuteMode MuteMode { get; set; } = MuteMode.PushToTalk;

    /// <summary>Only used in <see cref="MuteMode.ToggleMute"/>.</summary>
    public MicrophoneState InitialState { get; set; } = MicrophoneState.Mute;

    public HotkeySettings Hotkey { get; set; } = new();

    public bool VolumeControlEnabled { get; set; }

    public float? Volume { get; set; }
}

public sealed class NotificationSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The "Custom sounds" switch (About → Advanced). Off by default: the built-in cues play at full volume on the default
    /// device, and the sound, volume and device choices below are kept for when it's turned on.
    /// </summary>
    public bool CustomSoundsEnabled { get; set; }

    /// <summary>The built-in cues, generated by <c>tools/GenerateCueSounds.cs</c>.</summary>
    public const string DefaultMutedSound = "Relay off";

    public const string DefaultUnmutedSound = "Relay on";

    // Always written, even when null ("None"): with the null-skipping JSON default, a missing value would load back as
    // the default sound, so choosing None would undo itself on the next start.
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? WhenMuted { get; set; } = DefaultMutedSound;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? WhenUnmuted { get; set; } = DefaultUnmutedSound;

    public float Volume { get; set; } = 1;

    /// <summary>Playback device id; <c>null</c> uses the system default.</summary>
    public string? OutputDeviceId { get; set; }

    /// <summary>The sound that plays on mute: <see cref="WhenMuted"/> with custom sounds on, else the built-in cue.</summary>
    [JsonIgnore]
    public string? PlayedWhenMuted => CustomSoundsEnabled ? WhenMuted : DefaultMutedSound;

    [JsonIgnore]
    public string? PlayedWhenUnmuted => CustomSoundsEnabled ? WhenUnmuted : DefaultUnmutedSound;

    [JsonIgnore]
    public float PlayedVolume => CustomSoundsEnabled ? Volume : 1;

    [JsonIgnore]
    public string? PlayedOutputDeviceId => CustomSoundsEnabled ? OutputDeviceId : null;

    /// <summary>True when any sound choice differs from the defaults (used to offer a MicSwitch import's sounds).</summary>
    [JsonIgnore]
    public bool HasCustomChoices =>
        WhenMuted != DefaultMutedSound || WhenUnmuted != DefaultUnmutedSound || Math.Abs(Volume - 1) > 0.001f || OutputDeviceId is not null;
}

public enum OverlayCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

public sealed class OverlaySettings
{
    /// <summary>The range the Overlay page's sliders offer.</summary>
    public const double MinOpacity = 0.2, MaxOpacity = 1, MinScale = 0.6, MaxScale = 2.5;

    public OverlayVisibilityMode Visibility { get; set; } = OverlayVisibilityMode.Always;

    /// <summary>Visibility to restore when the overlay is switched back on from a quick toggle.</summary>
    public OverlayVisibilityMode LastVisibleMode { get; set; } = OverlayVisibilityMode.Always;

    /// <summary>Opacity of the whole overlay; the background is translucent on top of this.</summary>
    public double Opacity { get; set; } = 0.85;

    /// <summary>Size multiplier (1 = default).</summary>
    public double Scale { get; set; } = 1;

    /// <summary>Show "Muted"/"Live" next to the icon.</summary>
    public bool ShowLabel { get; set; } = true;

    /// <summary>
    /// Light up the overlay while you speak. Off by default: it keeps the microphone open for level
    /// monitoring, which shows the OS "microphone in use" indicator.
    /// </summary>
    public bool ShowSpeakingIndicator { get; set; }

    /// <summary>Position in screen pixels (top-left corner); <c>null</c> uses <see cref="Corner"/>.</summary>
    public WindowBounds? Bounds { get; set; }

    public OverlayCorner Corner { get; set; } = OverlayCorner.TopRight;

    /// <summary>
    /// Bounds (screen pixels) of the monitor the overlay was last placed on, so corner presets stay on it across
    /// restarts. <c>null</c> or a disconnected monitor uses the primary one; the value is kept for when it returns.
    /// </summary>
    public WindowBounds? Monitor { get; set; }

    public string? MutedIconPath { get; set; }

    public string? UnmutedIconPath { get; set; }
}

public sealed class WindowSettings
{
    public WindowBounds? Bounds { get; set; }

    public AppMode AppMode { get; set; } = AppMode.Tray;

    /// <summary>The user ticked "Don't show again" on the "still running in the tray" notice.</summary>
    public bool TrayNoticeDismissed { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.System;
}

/// <summary>How the app runs: in the notification area, or as a normal window.</summary>
public enum AppMode
{
    /// <summary>Starts hidden in the tray; closing or minimizing the window hides it to the tray.</summary>
    [JsonStringEnumMemberName("tray")]
    Tray,

    /// <summary>A normal app: closing the window exits, minimizing goes to the taskbar.</summary>
    [JsonStringEnumMemberName("window")]
    Window,
}

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public sealed record WindowBounds(double Left, double Top, double Width, double Height);
