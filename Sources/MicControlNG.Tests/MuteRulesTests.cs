using MicControlNG.Settings;

namespace MicControlNG.Tests;

public sealed class MuteRulesTests
{
    [Theory]
    [InlineData(MuteMode.PushToTalk, MicrophoneState.Unmute, true)]
    [InlineData(MuteMode.PushToMute, MicrophoneState.Mute, false)]
    [InlineData(MuteMode.ToggleMute, MicrophoneState.Mute, true)]
    [InlineData(MuteMode.ToggleMute, MicrophoneState.Unmute, false)]
    [InlineData(MuteMode.ToggleMute, MicrophoneState.Any, null)]
    public void InitialMute(MuteMode mode, MicrophoneState state, bool? expected)
    {
        Assert.Equal(expected, MuteRules.InitialMute(mode, state));
    }

    [Theory]
    [InlineData(MuteMode.PushToTalk, true, true, false)]
    [InlineData(MuteMode.PushToTalk, false, false, true)]
    [InlineData(MuteMode.PushToMute, true, false, true)]
    [InlineData(MuteMode.PushToMute, false, true, false)]
    [InlineData(MuteMode.ToggleMute, true, true, false)]
    [InlineData(MuteMode.ToggleMute, true, false, true)]
    [InlineData(MuteMode.ToggleMute, false, true, null)]
    public void OnMainHotkey(MuteMode mode, bool pressed, bool current, bool? expected)
    {
        Assert.Equal(expected, MuteRules.OnMainHotkey(mode, pressed, current));
    }

    [Theory]
    [InlineData(OverlayVisibilityMode.Always, true, true)]
    [InlineData(OverlayVisibilityMode.Never, false, false)]
    [InlineData(OverlayVisibilityMode.WhenMuted, true, true)]
    [InlineData(OverlayVisibilityMode.WhenMuted, false, false)]
    [InlineData(OverlayVisibilityMode.WhenUnmuted, false, true)]
    public void IsOverlayVisible(OverlayVisibilityMode mode, bool muted, bool expected)
    {
        Assert.Equal(expected, MuteRules.IsOverlayVisible(mode, muted));
    }

    [Theory]
    [InlineData(HotkeyAction.ToggleMute, true, false, true)]
    [InlineData(HotkeyAction.ToggleMute, false, false, null)]
    [InlineData(HotkeyAction.Mute, true, false, true)]
    [InlineData(HotkeyAction.Unmute, true, true, false)]
    [InlineData(HotkeyAction.PushToTalk, true, true, false)]
    [InlineData(HotkeyAction.PushToMute, false, true, false)]
    [InlineData(HotkeyAction.SpeakerMute, true, false, null)]
    public void OnAction(HotkeyAction action, bool pressed, bool current, bool? expected)
    {
        Assert.Equal(expected, MuteRules.OnAction(action, pressed, current));
    }

    [Theory]
    [InlineData(MuteMode.PushToTalk, HotkeyAction.PushToTalk)]
    [InlineData(MuteMode.ToggleMute, HotkeyAction.ToggleMute)]
    [InlineData(MuteMode.PushToMute, HotkeyAction.PushToMute)]
    public void MainActionFor(MuteMode mode, HotkeyAction expected)
    {
        Assert.Equal(expected, MuteRules.MainActionFor(mode));
    }
}
