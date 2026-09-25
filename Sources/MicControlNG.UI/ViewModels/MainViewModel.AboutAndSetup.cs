using System.Reflection;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicControlNG.Settings;

namespace MicControlNG.ViewModels;

public sealed partial class MainViewModel
{
    private const string ReleasesUrl = RepositoryUrl + "/releases/latest";

    // ---------------------------------------------------------------- About

    public string VersionText { get; } = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "Development build";

    public string RuntimeText { get; } = RuntimeInformation.FrameworkDescription;

    public string OsText { get; } = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";

    public string UiFrameworkText { get; } = $"Avalonia {typeof(Avalonia.Application).Assembly.GetName().Version?.ToString(3)}";

    public string HotkeyAccessText => Capabilities.LimitationNotice is null
        ? "Full: hotkeys work in every app, including elevated games"
        : "Limited: not running as administrator";

    public string SettingsFolder => settingsService.Directory;

    public string LogsFolder => Path.Combine(settingsService.Directory, "logs");

    public bool CheckForUpdates
    {
        get => Settings.CheckForUpdates;
        set => SetAndSave(Settings.CheckForUpdates, value, v => Settings.CheckForUpdates = v);
    }

    [ObservableProperty]
    public partial string? UpdateVersion { get; private set; }

    [ObservableProperty]
    public partial string UpdateStatus { get; private set; } = "Not checked yet";

    [RelayCommand]
    private async Task CheckUpdates()
    {
        if (!updates.IsInstalled)
        {
            UpdateStatus = "This copy isn't installed, so it can't update itself.";
            await dialogs.OpenAsync(ReleasesUrl);
            return;
        }

        try
        {
            UpdateStatus = "Checking…";
            UpdateVersion = await updates.CheckAsync();
            UpdateStatus = UpdateVersion is null ? "You're up to date." : $"Version {UpdateVersion} is available.";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            UpdateStatus = $"Update check failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task InstallUpdate()
    {
        try
        {
            await updates.DownloadAndRestartAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            StatusMessage = $"Update failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private Task OpenSettingsFolder() => dialogs.OpenAsync(SettingsFolder);

    [RelayCommand]
    private Task OpenLogsFolder()
    {
        Directory.CreateDirectory(LogsFolder);
        return dialogs.OpenAsync(LogsFolder);
    }

    [RelayCommand]
    private Task OpenProjectPage() => dialogs.OpenAsync(RepositoryUrl);

    /// <summary>Runs an update check on startup when enabled; failures are logged only.</summary>
    public async Task CheckForUpdatesOnStartupAsync()
    {
        if (!CheckForUpdates || !updates.IsInstalled)
        {
            return;
        }

        try
        {
            UpdateVersion = await updates.CheckAsync();
            if (UpdateVersion is not null)
            {
                UpdateStatus = $"Version {UpdateVersion} is available.";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            LogUpdateCheckFailed(ex);
        }
    }

    // ---------------------------------------------------------------- First-run setup

    public const int SetupStepCount = 3;

    // Boxed once so XAML radio buttons can compare against them.
    public static readonly object PushToTalkMode = MuteMode.PushToTalk;
    public static readonly object ToggleMode = MuteMode.ToggleMute;
    public static readonly object PushToMuteMode = MuteMode.PushToMute;

    public bool SetupCompleted
    {
        get => Settings.SetupCompleted;
        private set
        {
            if (SetAndSave(Settings.SetupCompleted, value, v => Settings.SetupCompleted = v))
            {
                OnPropertyChanged(nameof(IsSetupVisible));
                UpdateLevelMonitoring();
            }
        }
    }

    public bool IsSetupVisible => !SetupCompleted;

    /// <summary>0-based step; <see cref="SetupStepCount"/> is the closing "you're all set" page.</summary>
    [ObservableProperty]
    public partial int SetupStep { get; private set; }

    public bool IsSetupStep1 => SetupStep == 0;

    public bool IsSetupStep2 => SetupStep == 1;

    public bool IsSetupStep3 => SetupStep == 2;

    public bool IsSetupDone => SetupStep == SetupStepCount;

    public bool CanGoBack => SetupStep is > 0 and < SetupStepCount;

    public string SetupProgressText => SetupStep < SetupStepCount ? $"Step {SetupStep + 1} of {SetupStepCount}" : "All set";

    public string SetupNextText => SetupStep switch
    {
        SetupStepCount - 1 => "Finish",
        SetupStepCount => $"Start using {AppName}",
        _ => "Next",
    };

    [RelayCommand]
    private void SetupNext()
    {
        if (SetupStep >= SetupStepCount)
        {
            SetupCompleted = true;
            Navigate(Page.Home);
            return;
        }

        SetupStep++;
    }

    [RelayCommand]
    private void SetupBack()
    {
        if (SetupStep > 0)
        {
            SetupStep--;
        }
    }

    [RelayCommand]
    private void SetMode(object? mode)
    {
        if (mode is MuteMode value)
        {
            MuteMode = value;
        }
    }

    // Boxed once so XAML radio buttons can compare against them.
    public static readonly object TrayAppMode = AppMode.Tray;
    public static readonly object WindowAppMode = AppMode.Window;

    [RelayCommand]
    private void SetAppMode(object? mode)
    {
        if (mode is AppMode value)
        {
            AppMode = value;
        }
    }

    [RelayCommand]
    private void SkipSetup() => SetupCompleted = true;

    [RelayCommand]
    private void RunSetupAgain()
    {
        SetupStep = 0;
        SetupCompleted = false;
    }

    partial void OnSetupStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsSetupStep1));
        OnPropertyChanged(nameof(IsSetupStep2));
        OnPropertyChanged(nameof(IsSetupStep3));
        OnPropertyChanged(nameof(IsSetupDone));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(SetupProgressText));
        OnPropertyChanged(nameof(SetupNextText));
    }
}
