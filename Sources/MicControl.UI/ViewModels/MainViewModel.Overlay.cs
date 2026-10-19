using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicControl.Settings;
using Microsoft.Extensions.Logging;

namespace MicControl.ViewModels;

public sealed partial class MainViewModel
{
    private const double SpeakingThreshold = 0.06;
    private static readonly Bitmap DefaultMutedIcon = LoadAsset("microphoneDisabled.png");
    private static readonly Bitmap DefaultUnmutedIcon = LoadAsset("microphoneEnabled.png");
    private static readonly Lazy<WindowIcon> DefaultMutedWindowIcon = new(() => LoadWindowIcon("microphoneDisabled.ico"));
    private static readonly Lazy<WindowIcon> DefaultUnmutedWindowIcon = new(() => LoadWindowIcon("microphoneEnabled.ico"));
    private readonly DispatcherTimer levelTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private IDisposable? levelSession;
    private float latestPeak;
    private bool isWindowVisible;

    /// <summary>Raised when the overlay should move to its configured corner.</summary>
    public event EventHandler? OverlayPlacementRequested;

    public IReadOnlyList<Choice> OverlayVisibilityModes { get; } =
    [
        new(OverlayVisibilityMode.Always, "Always"),
        new(OverlayVisibilityMode.WhenMuted, "When muted"),
        new(OverlayVisibilityMode.WhenUnmuted, "When live"),
        new(OverlayVisibilityMode.Never, "Never"),
    ];

    public OverlayVisibilityMode OverlayVisibility
    {
        get => Settings.Overlay.Visibility;
        set
        {
            if (!SetAndSave(Settings.Overlay.Visibility, value, v => Settings.Overlay.Visibility = v))
            {
                return;
            }

            if (value != OverlayVisibilityMode.Never)
            {
                Settings.Overlay.LastVisibleMode = value;
            }

            OnPropertyChanged(nameof(OverlayEnabled));
            OnPropertyChanged(nameof(OverlayVisibilityStep));
            OnPropertyChanged(nameof(OverlayVisibilityName));
            OnOverlayVisibilityInputsChanged();
        }
    }

    /// <summary>The Overlay page's "Show overlay" slider, from least to most shown.</summary>
    private static readonly OverlayVisibilityMode[] VisibilitySteps =
        [OverlayVisibilityMode.Never, OverlayVisibilityMode.WhenMuted, OverlayVisibilityMode.WhenUnmuted, OverlayVisibilityMode.Always];

    /// <summary>The slider's position: Never (0) → When muted → When live → Always (3).</summary>
    public double OverlayVisibilityStep
    {
        get => Array.IndexOf(VisibilitySteps, OverlayVisibility);
        set => OverlayVisibility = VisibilitySteps[Math.Clamp((int)Math.Round(value), 0, VisibilitySteps.Length - 1)];
    }

    /// <summary>The name of the slider's current stop, shown beside it.</summary>
    public string OverlayVisibilityName => OverlayVisibilityModes.First(m => Equals(m.Value, OverlayVisibility)).Name;

    /// <summary>Quick on/off switch; turning it back on restores the last visibility mode.</summary>
    public bool OverlayEnabled
    {
        get => OverlayVisibility != OverlayVisibilityMode.Never;
        set => OverlayVisibility = value ? Settings.Overlay.LastVisibleMode : OverlayVisibilityMode.Never;
    }

    /// <summary>While editing, the overlay is visible, draggable and not click-through.</summary>
    [ObservableProperty]
    public partial bool IsOverlayEditing { get; set; }

    public bool IsOverlayVisible => OverlayEnabled && (IsOverlayEditing || MuteRules.IsOverlayVisible(OverlayVisibility, IsMutedState));

    public double OverlayOpacity
    {
        get => Settings.Overlay.Opacity;
        set => SetAndSave(Settings.Overlay.Opacity, Math.Clamp(value, OverlaySettings.MinOpacity, OverlaySettings.MaxOpacity), v => Settings.Overlay.Opacity = v, isSlider: true);
    }

    public double OverlayScale
    {
        get => Settings.Overlay.Scale;
        set => SetAndSave(Settings.Overlay.Scale, Math.Clamp(value, OverlaySettings.MinScale, OverlaySettings.MaxScale), v => Settings.Overlay.Scale = v, isSlider: true);
    }

    public bool OverlayShowLabel
    {
        get => Settings.Overlay.ShowLabel;
        set => SetAndSave(Settings.Overlay.ShowLabel, value, v => Settings.Overlay.ShowLabel = v);
    }

    public bool ShowSpeakingIndicator
    {
        get => Settings.Overlay.ShowSpeakingIndicator;
        set
        {
            if (SetAndSave(Settings.Overlay.ShowSpeakingIndicator, value, v => Settings.Overlay.ShowSpeakingIndicator = v))
            {
                UpdateLevelMonitoring();
            }
        }
    }

    public OverlayCorner OverlayCorner => Settings.Overlay.Corner;

    [ObservableProperty]
    public partial Bitmap MutedIcon { get; private set; } = DefaultMutedIcon;

    [ObservableProperty]
    public partial Bitmap UnmutedIcon { get; private set; } = DefaultUnmutedIcon;

    public bool HasCustomIcons => Settings.Overlay.MutedIconPath is not null || Settings.Overlay.UnmutedIconPath is not null;

    // ---------------------------------------------------------------- Level meter

    /// <summary>Smoothed input level 0..1 while monitoring (Home page or speaking indicator), otherwise 0.</summary>
    [ObservableProperty]
    public partial double InputLevel { get; private set; }

    public bool IsLevelMeterActive => levelSession is not null;

    public bool IsSpeaking => ShowSpeakingIndicator && IsMuted == false && InputLevel > SpeakingThreshold;

    /// <summary>Set by the main window so the level meter only runs while it can be seen.</summary>
    public bool IsWindowVisible
    {
        get => isWindowVisible;
        set
        {
            if (SetProperty(ref isWindowVisible, value))
            {
                UpdateLevelMonitoring();
            }
        }
    }

    [RelayCommand]
    private void PlaceOverlay(OverlayCorner corner)
    {
        LogOverlayCornerChosen(corner, Settings.Overlay.Corner);
        Settings.Overlay.Corner = corner;
        Settings.Overlay.Bounds = null;
        OnPropertyChanged(nameof(OverlayCorner));
        OverlayPlacementRequested?.Invoke(this, EventArgs.Empty);
        Save();
    }

    [RelayCommand]
    private void ToggleOverlayEditing() => IsOverlayEditing = !IsOverlayEditing;

    [RelayCommand]
    private Task SelectMutedIcon() => SelectIcon("muted", p => Settings.Overlay.MutedIconPath = p);

    [RelayCommand]
    private Task SelectUnmutedIcon() => SelectIcon("unmuted", p => Settings.Overlay.UnmutedIconPath = p);

    [RelayCommand]
    private void ResetIcons()
    {
        LogSettingChanged("OverlayIcons", "default");
        Settings.Overlay.MutedIconPath = null;
        Settings.Overlay.UnmutedIconPath = null;
        RefreshIcons();
        Save();
    }

    partial void OnIsOverlayEditingChanged(bool value)
    {
        LogOverlayEditing(value);
        OnOverlayVisibilityInputsChanged();
    }

    partial void OnInputLevelChanged(double value) => OnPropertyChanged(nameof(IsSpeaking));

    private void InitializeOverlay()
    {
        outputIndicatorTimer.Tick += (_, _) =>
        {
            outputIndicatorTimer.Stop();
            ShowOutputIndicator = false;
        };
        volumeRepeatTimer.Tick += (_, _) => speakers.ChangeVolume(volumeRepeatStep);
    }

    private void OnOverlayVisibilityInputsChanged()
    {
        OnPropertyChanged(nameof(IsOverlayVisible));
        UpdateLevelMonitoring();
    }

    private void InitializeLevelMeter() => levelTimer.Tick += (_, _) =>
    {
        // Fast attack, slow release, like a hardware meter.
        var peak = Volatile.Read(ref latestPeak);
        InputLevel = Math.Max(peak, InputLevel * 0.85);
        if (InputLevel < 0.005)
        {
            InputLevel = 0;
        }
    };

    private void UpdateLevelMonitoring()
    {
        var wanted = (IsWindowVisible && (IsHomePage || !SetupCompleted)) || (ShowSpeakingIndicator && IsOverlayVisible);
        if (wanted == (levelSession is not null))
        {
            return;
        }

        LogLevelMonitoring(wanted, IsWindowVisible && IsHomePage, !SetupCompleted, ShowSpeakingIndicator && IsOverlayVisible);
        if (wanted)
        {
            levelSession = levelMonitor.Start(SelectedMicrophoneId, peak => Volatile.Write(ref latestPeak, peak));
            levelTimer.Start();
        }
        else
        {
            StopLevelMonitoring();
        }

        OnPropertyChanged(nameof(IsLevelMeterActive));
    }

    private void RestartLevelMonitoring()
    {
        if (levelSession is not null)
        {
            StopLevelMonitoring();
            UpdateLevelMonitoring();
        }
    }

    private void StopLevelMonitoring()
    {
        levelSession?.Dispose();
        levelSession = null;
        levelTimer.Stop();
        Volatile.Write(ref latestPeak, 0);
        InputLevel = 0;
    }

    private void RefreshIcons()
    {
        MutedIcon = LoadImage(Settings.Overlay.MutedIconPath) ?? DefaultMutedIcon;
        UnmutedIcon = LoadImage(Settings.Overlay.UnmutedIconPath) ?? DefaultUnmutedIcon;
        OnPropertyChanged(nameof(CurrentIcon));
        OnPropertyChanged(nameof(HasCustomIcons));
    }

    private async Task SelectIcon(string name, Action<string> assign)
    {
        var file = await dialogs.PickFileAsync("Choose an icon", "Images", ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.ico"]);
        if (file is null)
        {
            return;
        }

        if (LoadImage(file) is not { } preview)
        {
            StatusMessage = "That file isn't an image MicControl can read.";
            return;
        }

        preview.Dispose();

        // Copy so the icon keeps working if the original file is moved.
        var directory = Path.Combine(settingsService.Directory, "Icons");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, name + Path.GetExtension(file));
        try
        {
            // Picking the copy that's already in use (it's in the settings folder) needs no copy.
            if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(file, target, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogOverlayIconCopyFailed(file, ex);
            StatusMessage = $"Couldn't use that image: {ex.Message}";
            return;
        }

        assign(target);
        LogOverlayIconChanged(name, target);
        RefreshIcons();
        Save();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Overlay corner preset {Corner} chosen (was {Previous})")]
    private partial void LogOverlayCornerChosen(OverlayCorner corner, OverlayCorner previous);

    [LoggerMessage(Level = LogLevel.Information, Message = "Overlay {Which} icon set to {Path}")]
    private partial void LogOverlayIconChanged(string which, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't copy the overlay icon {File}")]
    private partial void LogOverlayIconCopyFailed(string file, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Overlay edit position {Editing}")]
    private partial void LogOverlayEditing(bool editing);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Level meter {Running} (Home visible: {Home}, setup: {Setup}, speaking indicator: {Speaking})")]
    private partial void LogLevelMonitoring(bool running, bool home, bool setup, bool speaking);
}
