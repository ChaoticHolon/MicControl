using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicControl.Platform;
using MicControl.Settings;
using Microsoft.Extensions.Logging;

namespace MicControl.ViewModels;

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

    public ObservableCollection<CustomHotkeyViewModel> CustomHotkeys { get; } = [];

    /// <summary>"Custom hotkeys" (About → Advanced): shows the Hotkeys page and makes them work; off pauses them.</summary>
    public bool CustomHotkeysEnabled
    {
        get => Settings.CustomHotkeysEnabled;
        set
        {
            if (SetAndSave(Settings.CustomHotkeysEnabled, value, v => Settings.CustomHotkeysEnabled = v))
            {
                ApplyHotkeys();
                UpdateNavItems();
            }
        }
    }

    /// <summary>Setup offers a MicSwitch import's custom hotkeys; they stay off until the user says yes.</summary>
    public bool OffersImportedCustomHotkeys => settingsService.MigratedFromLegacy && Settings.CustomHotkeys.Count > 0;

    /// <summary>Setup offers a MicSwitch import's sounds, volume and device when they differ from the defaults.</summary>
    public bool OffersImportedSounds => settingsService.MigratedFromLegacy && Settings.Notifications.HasCustomChoices;

    public bool OffersImports => OffersImportedCustomHotkeys || OffersImportedSounds;

    public bool HasCustomHotkeys => CustomHotkeys.Count > 0;

    public bool HasSpeakerActions => Settings.CustomHotkeys.Any(e => MuteRules.IsSpeakerAction(e.Action));

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
        // Moves every controlled speaker by the same step and keeps nothing: "All speakers" must not flatten
        // every output to one level, and a level kept for plugged-in devices would undo the user's own changes.
        set => speakers.ChangeVolume((float)(value / 100) - (speakers.Volume ?? 0));
    }

    [ObservableProperty]
    public partial bool ShowOutputIndicator { get; private set; }

    public string OutputIndicatorGlyph => speakers.Mute == true ? "" : "";

    public string OutputIndicatorText => speakers.Mute == true ? "Muted" : $"{SpeakerVolume:0}%";

    /// <summary>Actions offered for an custom hotkey: everything except what the main key already does in this mode.</summary>
    internal IReadOnlyList<Choice> ActionsFor(HotkeyAction current)
    {
        var main = MuteRules.MainActionFor(MuteMode);
        return [.. AllActions.Where(a => !Equals(a.Value, main) || Equals(a.Value, current))];
    }

    /// <summary>False once the list holds <see cref="AppSettings.MaxCustomHotkeys"/> rows.</summary>
    public bool CanAddCustomHotkey => CustomHotkeys.Count < AppSettings.MaxCustomHotkeys;

    [RelayCommand(CanExecute = nameof(CanAddCustomHotkey))]
    private void AddCustomHotkey()
    {
        var main = MuteRules.MainActionFor(MuteMode);
        var action = new[] { HotkeyAction.ToggleMute, HotkeyAction.Mute, HotkeyAction.PushToTalk }.First(a => a != main);
        var custom = new CustomHotkey { Action = action };
        LogCustomHotkeyAdded(action);
        Settings.CustomHotkeys.Add(custom);
        CustomHotkeys.Add(CreateCustomHotkey(custom));
        OnCustomHotkeysChanged();
    }

    internal void RemoveCustomHotkey(CustomHotkeyViewModel item)
    {
        LogCustomHotkeyRemoved(item.Model.Hotkey.Key, item.Model.Action);
        Settings.CustomHotkeys.Remove(item.Model);
        CustomHotkeys.Remove(item);
        OnCustomHotkeysChanged();
    }

    private CustomHotkeyViewModel CreateCustomHotkey(CustomHotkey model) => new(this, model, Capabilities, OnCustomHotkeysChanged);

    private void OnCustomHotkeysChanged()
    {
        OnPropertyChanged(nameof(HasCustomHotkeys));
        OnPropertyChanged(nameof(HasSpeakerActions));
        OnPropertyChanged(nameof(CanAddCustomHotkey));
        AddCustomHotkeyCommand.NotifyCanExecuteChanged();
        ApplyHotkeys();
        Save();
    }

    private void RunAction(HotkeyAction action, bool pressed)
    {
        LogRunAction(action, pressed);
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
        speakers.ChangeVolume(step);
        volumeRepeatTimer.Start();
    }

    private void OnSpeakerStateChanged()
    {
        var (volume, mute) = (speakers.Volume, speakers.Mute);
        var changed = volume != lastSpeakerVolume || mute != lastSpeakerMute;
        if (changed)
        {
            LogSpeakerState(mute, volume * 100);
        }

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Custom hotkey added ({Action})")]
    private partial void LogCustomHotkeyAdded(HotkeyAction action);

    [LoggerMessage(Level = LogLevel.Information, Message = "Custom hotkey removed ({Key} -> {Action})")]
    private partial void LogCustomHotkeyRemoved(Hotkeys.HotkeyGesture key, HotkeyAction action);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Action {Action} ({Pressed})")]
    private partial void LogRunAction(HotkeyAction action, bool pressed);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Speakers: muted {Muted}, volume {Volume:0}%")]
    private partial void LogSpeakerState(bool? muted, float? volume);
}

/// <summary>One row on the Hotkeys page.</summary>
public sealed partial class CustomHotkeyViewModel : ObservableObject
{
    private readonly MainViewModel owner;
    private readonly Action changed;

    internal CustomHotkeyViewModel(MainViewModel owner, CustomHotkey model, PlatformCapabilities capabilities, Action changed)
    {
        this.owner = owner;
        this.changed = changed;
        Model = model;
        Editor = new HotkeyEditorViewModel("Custom hotkey", model.Hotkey, changed, capabilities.CanSuppressHotkeys, capabilities.SupportsMouseHotkeys);
        Actions = owner.ActionsFor(model.Action);
    }

    public CustomHotkey Model { get; }

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
    private void Remove() => owner.RemoveCustomHotkey(this);
}
