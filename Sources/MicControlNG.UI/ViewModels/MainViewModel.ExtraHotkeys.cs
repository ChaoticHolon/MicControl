using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicControlNG.Platform;
using MicControlNG.Settings;

namespace MicControlNG.ViewModels;

public sealed partial class MainViewModel
{
    private const float VolumeStep = 0.02f;
    private readonly DispatcherTimer volumeRepeatTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly DispatcherTimer outputIndicatorTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private float volumeRepeatStep;
    private float? lastSpeakerVolume;
    private bool? lastSpeakerMute;

    public static IReadOnlyList<Choice> AllActions { get; } =
    [
        new(HotkeyAction.ToggleMute, "Toggle microphone"),
        new(HotkeyAction.Mute, "Mute microphone"),
        new(HotkeyAction.Unmute, "Unmute microphone"),
        new(HotkeyAction.PushToTalk, "Push-to-talk"),
        new(HotkeyAction.PushToMute, "Push-to-mute"),
        new(HotkeyAction.SpeakerToggleMute, "Toggle speaker mute"),
        new(HotkeyAction.SpeakerMute, "Mute speakers"),
        new(HotkeyAction.SpeakerUnmute, "Unmute speakers"),
        new(HotkeyAction.SpeakerVolumeUp, "Speaker volume up"),
        new(HotkeyAction.SpeakerVolumeDown, "Speaker volume down"),
        new(HotkeyAction.ToggleOverlay, "Show or hide the overlay"),
    ];

    public ObservableCollection<ExtraHotkeyViewModel> ExtraHotkeys { get; } = [];

    public bool HasExtraHotkeys => ExtraHotkeys.Count > 0;

    public bool HasSpeakerActions => Settings.ExtraHotkeys.Any(e => MuteRules.IsSpeakerAction(e.Action));

    public ObservableCollection<AudioDeviceInfo> Speakers { get; } = [];

    public string SelectedSpeakerId
    {
        get => Settings.SpeakerDeviceId;
        set
        {
            if (value is not null && SetAndSave(Settings.SpeakerDeviceId, value, v => Settings.SpeakerDeviceId = v))
            {
                Rebind(speakers, value);
            }
        }
    }

    public double SpeakerVolume
    {
        get => (speakers.Volume ?? 0) * 100;
        set => speakers.Volume = (float)(value / 100);
    }

    [ObservableProperty]
    public partial bool ShowOutputIndicator { get; private set; }

    public string OutputIndicatorGlyph => speakers.Mute == true ? "" : "";

    public string OutputIndicatorText => speakers.Mute == true ? "Muted" : $"{SpeakerVolume:0}%";

    /// <summary>Actions offered for an extra hotkey: everything except what the main key already does in this mode.</summary>
    internal IReadOnlyList<Choice> ActionsFor(HotkeyAction current)
    {
        var main = MuteRules.MainActionFor(MuteMode);
        return [.. AllActions.Where(a => !Equals(a.Value, main) || Equals(a.Value, current))];
    }

    [RelayCommand]
    private void AddExtraHotkey()
    {
        var main = MuteRules.MainActionFor(MuteMode);
        var action = new[] { HotkeyAction.ToggleMute, HotkeyAction.Mute, HotkeyAction.PushToTalk }.First(a => a != main);
        var extra = new ExtraHotkey { Action = action };
        Settings.ExtraHotkeys.Add(extra);
        ExtraHotkeys.Add(CreateExtraHotkey(extra));
        OnExtraHotkeysChanged();
    }

    internal void RemoveExtraHotkey(ExtraHotkeyViewModel item)
    {
        Settings.ExtraHotkeys.Remove(item.Model);
        ExtraHotkeys.Remove(item);
        OnExtraHotkeysChanged();
    }

    private ExtraHotkeyViewModel CreateExtraHotkey(ExtraHotkey model) => new(this, model, Capabilities, OnExtraHotkeysChanged);

    private void OnExtraHotkeysChanged()
    {
        OnPropertyChanged(nameof(HasExtraHotkeys));
        OnPropertyChanged(nameof(HasSpeakerActions));
        ApplyHotkeys();
        Save();
    }

    private void RunAction(HotkeyAction action, bool pressed)
    {
        if (MuteRules.OnAction(action, pressed, microphone.Mute) is { } mute)
        {
            microphone.Mute = mute;
            return;
        }

        switch (action)
        {
            case HotkeyAction.SpeakerToggleMute when pressed:
                speakers.Mute = !(speakers.Mute ?? false);
                break;
            case HotkeyAction.SpeakerMute when pressed:
                speakers.Mute = true;
                break;
            case HotkeyAction.SpeakerUnmute when pressed:
                speakers.Mute = false;
                break;
            case HotkeyAction.SpeakerVolumeUp:
                RepeatVolumeStep(pressed, VolumeStep);
                break;
            case HotkeyAction.SpeakerVolumeDown:
                RepeatVolumeStep(pressed, -VolumeStep);
                break;
            case HotkeyAction.ToggleOverlay when pressed:
                OverlayEnabled = !OverlayEnabled;
                break;
        }
    }

    private void RepeatVolumeStep(bool pressed, float step)
    {
        volumeRepeatTimer.Stop();
        if (!pressed)
        {
            return;
        }

        volumeRepeatStep = step;
        speakers.Volume = (speakers.Volume ?? 0) + step;
        volumeRepeatTimer.Start();
    }

    private void OnSpeakerStateChanged()
    {
        var (volume, mute) = (speakers.Volume, speakers.Mute);
        var changed = volume != lastSpeakerVolume || mute != lastSpeakerMute;
        if (changed && !isRebinding && lastSpeakerVolume is not null && HasSpeakerActions)
        {
            ShowOutputIndicator = true;
            outputIndicatorTimer.Stop();
            outputIndicatorTimer.Start();
        }

        (lastSpeakerVolume, lastSpeakerMute) = (volume, mute);
        OnPropertyChanged(nameof(SpeakerVolume));
        OnPropertyChanged(nameof(OutputIndicatorGlyph));
        OnPropertyChanged(nameof(OutputIndicatorText));
    }
}

/// <summary>One row on the Extra hotkeys page.</summary>
public sealed partial class ExtraHotkeyViewModel : ObservableObject
{
    private readonly MainViewModel owner;
    private readonly Action changed;

    internal ExtraHotkeyViewModel(MainViewModel owner, ExtraHotkey model, PlatformCapabilities capabilities, Action changed)
    {
        this.owner = owner;
        this.changed = changed;
        Model = model;
        Editor = new HotkeyEditorViewModel("Extra hotkey", model.Hotkey, changed, capabilities.CanSuppressHotkeys, capabilities.SupportsMouseHotkeys);
        Actions = owner.ActionsFor(model.Action);
    }

    public ExtraHotkey Model { get; }

    public HotkeyEditorViewModel Editor { get; }

    [ObservableProperty]
    public partial IReadOnlyList<Choice> Actions { get; private set; }

    public HotkeyAction Action
    {
        get => Model.Action;
        set
        {
            if (value != Model.Action)
            {
                Model.Action = value;
                OnPropertyChanged();
                changed();
            }
        }
    }

    public bool IsEnabled
    {
        get => Model.IsEnabled;
        set
        {
            if (value != Model.IsEnabled)
            {
                Model.IsEnabled = value;
                OnPropertyChanged();
                changed();
            }
        }
    }

    internal void RefreshActions()
    {
        var action = Action;
        Actions = owner.ActionsFor(action);
        OnPropertyChanged(nameof(Action));
    }

    [RelayCommand]
    private void Remove() => owner.RemoveExtraHotkey(this);
}
