using MicControlNG.Settings;

namespace MicControlNG;

public enum MuteMode
{
    ToggleMute,
    PushToTalk,
    PushToMute,
}

public enum MicrophoneState
{
    Any,
    Mute,
    Unmute,
}

public enum OverlayVisibilityMode
{
    Always,
    Never,
    WhenMuted,
    WhenUnmuted,
}

/// <summary>Pure decision logic for how hotkeys and settings affect the microphone.</summary>
public static class MuteRules
{
    /// <summary>Mute state to apply when the app starts or the mode changes; <c>null</c> means leave it alone.</summary>
    public static bool? InitialMute(MuteMode mode, MicrophoneState initialState) => mode switch
    {
        MuteMode.PushToTalk => true,
        MuteMode.PushToMute => false,
        _ => initialState switch
        {
            MicrophoneState.Mute => true,
            MicrophoneState.Unmute => false,
            _ => null,
        },
    };

    /// <summary>Mute state after the main hotkey is pressed or released; <c>null</c> means no change.</summary>
    public static bool? OnMainHotkey(MuteMode mode, bool isPressed, bool? currentMute) => mode switch
    {
        MuteMode.PushToTalk => !isPressed,
        MuteMode.PushToMute => isPressed,
        _ => isPressed ? !(currentMute ?? false) : null,
    };

    public static bool IsOverlayVisible(OverlayVisibilityMode mode, bool isMuted) => mode switch
    {
        OverlayVisibilityMode.Always => true,
        OverlayVisibilityMode.WhenMuted => isMuted,
        OverlayVisibilityMode.WhenUnmuted => !isMuted,
        _ => false,
    };

    /// <summary>Microphone mute state after an extra hotkey is pressed or released; <c>null</c> means no change.</summary>
    public static bool? OnAction(HotkeyAction action, bool isPressed, bool? currentMute) => action switch
    {
        HotkeyAction.ToggleMute => isPressed ? !(currentMute ?? false) : null,
        HotkeyAction.Mute => isPressed ? true : null,
        HotkeyAction.Unmute => isPressed ? false : null,
        HotkeyAction.PushToTalk => !isPressed,
        HotkeyAction.PushToMute => isPressed,
        _ => null,
    };

    public static bool IsSpeakerAction(HotkeyAction action) => action is >= HotkeyAction.SpeakerToggleMute and <= HotkeyAction.SpeakerVolumeDown;

    /// <summary>The extra-hotkey action that would duplicate what the main hotkey already does in this mode.</summary>
    public static HotkeyAction MainActionFor(MuteMode mode) => mode switch
    {
        MuteMode.PushToTalk => HotkeyAction.PushToTalk,
        MuteMode.PushToMute => HotkeyAction.PushToMute,
        _ => HotkeyAction.ToggleMute,
    };
}
