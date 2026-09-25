using System.Text.Json.Serialization;
using MicControlNG.Hotkeys;

namespace MicControlNG.Settings;

public sealed class AppSettings
{
    /// <summary>Device id meaning "every active device of this kind".</summary>
    public const string AllDevices = "all";

    public const int CurrentVersion = 4;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>False until the first-run setup has been completed or skipped.</summary>
    public bool SetupCompleted { get; set; }

    public MicrophoneSettings Microphone { get; set; } = new();

    /// <summary>Additional hotkeys, each bound to one action.</summary>
    public List<ExtraHotkey> ExtraHotkeys { get; set; } = [];

    /// <summary>Output device that speaker actions in <see cref="ExtraHotkeys"/> control.</summary>
    public string SpeakerDeviceId { get; set; } = AllDevices;

    public NotificationSettings Notifications { get; set; } = new();

    public OverlaySettings Overlay { get; set; } = new();

    public WindowSettings Window { get; set; } = new();

    public bool CheckForUpdates { get; set; } = true;
}

public sealed class HotkeySettings
{
    public HotkeyGesture Key { get; set; } = HotkeyGesture.Empty;

    public HotkeyGesture AlternativeKey { get; set; } = HotkeyGesture.Empty;

    /// <summary>"Exclusive hotkey": other applications don't receive the key while MicControlNG runs.</summary>
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

public sealed class ExtraHotkey
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
    public MicrophoneState InitialState { get; set; } = MicrophoneState.Any;

    public HotkeySettings Hotkey { get; set; } = new();

    public bool VolumeControlEnabled { get; set; }

    public float? Volume { get; set; }
}

public sealed class NotificationSettings
{
    public bool Enabled { get; set; } = true;

    public string? WhenMuted { get; set; } = "Beep750";

    public string? WhenUnmuted { get; set; } = "Beep300";

    public float Volume { get; set; } = 1;

    /// <summary>Playback device id; <c>null</c> uses the system default.</summary>
    public string? OutputDeviceId { get; set; }
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

    public string? MutedIconPath { get; set; }

    public string? UnmutedIconPath { get; set; }
}

public sealed class WindowSettings
{
    public WindowBounds? Bounds { get; set; }

    public AppMode AppMode { get; set; } = AppMode.Tray;

    /// <summary>The user ticked "Don't show again" on the "still running in the tray" notice.</summary>
    public bool TrayNoticeDismissed { get; set; }

    /// <summary>Settings version 3 only; read to choose <see cref="AppMode"/> when upgrading.</summary>
    [JsonPropertyName("startInTray")]
    public bool? LegacyStartInTray { get; set; }

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
