using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace MicControl.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<Choice> SoundOptions { get; } = [];

    public ObservableCollection<Choice> PlaybackDevices { get; } = [];

    public bool SoundsEnabled
    {
        get => Settings.Notifications.Enabled;
        set => SetAndSave(Settings.Notifications.Enabled, value, v => Settings.Notifications.Enabled = v);
    }

    // Dropdown values use "" for "None"/"Default device": a null SelectedValue would show an empty selection.
    // So null is never a choice; it's a ComboBox clearing its selection while it builds, and is ignored.
    public string SoundWhenMuted
    {
        get => Settings.Notifications.WhenMuted ?? string.Empty;
        set
        {
            if (value is not null)
            {
                SetAndSave(Settings.Notifications.WhenMuted, NullIfEmpty(value), v => Settings.Notifications.WhenMuted = v);
            }
        }
    }

    public string SoundWhenUnmuted
    {
        get => Settings.Notifications.WhenUnmuted ?? string.Empty;
        set
        {
            if (value is not null)
            {
                SetAndSave(Settings.Notifications.WhenUnmuted, NullIfEmpty(value), v => Settings.Notifications.WhenUnmuted = v);
            }
        }
    }

    /// <summary>Notification volume in percent.</summary>
    public double NotificationVolume
    {
        get => Settings.Notifications.Volume * 100;
        set => SetAndSave(Settings.Notifications.Volume, (float)(value / 100), v => Settings.Notifications.Volume = v, isSlider: true);
    }

    public string PlaybackDeviceId
    {
        get => Settings.Notifications.OutputDeviceId ?? string.Empty;
        set
        {
            if (value is not null)
            {
                SetAndSave(Settings.Notifications.OutputDeviceId, NullIfEmpty(value), v => Settings.Notifications.OutputDeviceId = v);
            }
        }
    }

    [RelayCommand]
    private Task PlaySound(string? name) => PlayNotification(NullIfEmpty(name) ?? Settings.Notifications.WhenMuted ?? Settings.Notifications.WhenUnmuted);

    [RelayCommand]
    private async Task AddSound()
    {
        var file = await dialogs.PickFileAsync("Add notification sound", "Audio files", ["*.wav", "*.mp3", "*.m4a", "*.wma", "*.aac"]);
        if (file is null)
        {
            return;
        }

        try
        {
            var name = await Task.Run(() => sounds.Add(file));
            LogSoundAdded(name, file);
            RefreshSounds();
            if (Settings.Notifications.WhenMuted is null)
            {
                SoundWhenMuted = name;
            }

            StatusMessage = $"Added sound \"{name}\".";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            LogSoundAddFailed(file, ex);
            StatusMessage = $"Could not add sound: {ex.Message}";
        }
    }

    private Task PlayNotification(string? name)
    {
        LogPlayingSound(name ?? "(none)", Settings.Notifications.Volume * 100, Settings.Notifications.OutputDeviceId ?? "default");
        return sounds.PlayAsync(name, Settings.Notifications.Volume, Settings.Notifications.OutputDeviceId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sound \"{Name}\" added from {File}")]
    private partial void LogSoundAdded(string name, string file);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not add sound {File}")]
    private partial void LogSoundAddFailed(string file, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Playing sound {Name} at {Volume:0}% on {Device}")]
    private partial void LogPlayingSound(string name, float volume, string device);

    private void RefreshSounds()
    {
        var (muted, unmuted) = (Settings.Notifications.WhenMuted, Settings.Notifications.WhenUnmuted);
        var names = sounds.GetNames();
        SoundOptions.Clear();
        SoundOptions.Add(new(string.Empty, "None"));
        names.ToList().ForEach(n => SoundOptions.Add(new(n, n)));

        // Sound names are case-insensitive (they're file names); use the listed spelling so the dropdown matches.
        (Settings.Notifications.WhenMuted, Settings.Notifications.WhenUnmuted) = (Canonical(muted), Canonical(unmuted));
        OnPropertyChanged(nameof(SoundWhenMuted));
        OnPropertyChanged(nameof(SoundWhenUnmuted));

        string? Canonical(string? name) => names.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) ?? name;
    }
}
