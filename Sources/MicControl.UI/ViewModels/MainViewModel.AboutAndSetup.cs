using System.Reflection;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicControl.Settings;
using Microsoft.Extensions.Logging;

namespace MicControl.ViewModels;

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

    public IReadOnlyList<Choice> LogLevels { get; } =
    [
        new(LogDetail.Trace, "Everything (trace)"),
        new(LogDetail.Debug, "Detailed (debug)"),
        new(LogDetail.Information, "Normal"),
        new(LogDetail.Warning, "Warnings and errors"),
        new(LogDetail.Error, "Errors only"),
    ];

    /// <summary>How much goes into the log files; the app applies changes immediately.</summary>
    public LogDetail LogDetail
    {
        get => Settings.LogLevel;
        set => SetAndSave(Settings.LogLevel, value, v => Settings.LogLevel = v);
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
            LogUpdateCheck(UpdateVersion ?? "none");
            UpdateStatus = UpdateVersion is null ? "You're up to date." : $"Version {UpdateVersion} is available.";
        }
        catch (Exception ex) when (IsUpdateFailure(ex))
        {
            LogUpdateCheckFailed(ex);
            UpdateStatus = $"Update check failed: {ex.Message}";
        }
    }

    /// <summary>Raised once the update installer is running: the app must exit so it can replace the files.</summary>
    public event EventHandler? ExitRequested;

    [RelayCommand]
    private async Task InstallUpdate()
    {
        try
        {
            LogInstallingUpdate(UpdateVersion);
            UpdateStatus = $"Downloading version {UpdateVersion}…";
            if (await updates.InstallAsync())
            {
                UpdateStatus = $"Installing version {UpdateVersion}. {AppName} will start again when it's done.";
                ExitRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex) when (IsUpdateFailure(ex))
        {
            LogUpdateFailed(ex);
            UpdateStatus = $"Version {UpdateVersion} is available.";
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
            LogUpdateCheck(UpdateVersion ?? "none");
            if (UpdateVersion is not null)
            {
                UpdateStatus = $"Version {UpdateVersion} is available.";
            }
        }
        catch (Exception ex) when (IsUpdateFailure(ex))
        {
            LogUpdateCheckFailed(ex);
        }
    }

    /// <summary>
    /// What a check or update can fail with: no network, a timeout (HttpClient reports it as a cancellation), a reply
    /// that isn't the feed (a captive portal's page), a file that can't be written or run, or a declined admin prompt
    /// for an all-users update (Win32Exception).
    /// </summary>
    private static bool IsUpdateFailure(Exception ex) => ex is HttpRequestException or OperationCanceledException
        or System.Text.Json.JsonException or IOException or UnauthorizedAccessException or InvalidOperationException
        or System.ComponentModel.Win32Exception;

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
        LogSetupStep(value);
        OnPropertyChanged(nameof(IsSetupStep1));
        OnPropertyChanged(nameof(IsSetupStep2));
        OnPropertyChanged(nameof(IsSetupStep3));
        OnPropertyChanged(nameof(IsSetupDone));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(SetupProgressText));
        OnPropertyChanged(nameof(SetupNextText));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Update check: newer version {Version}")]
    private partial void LogUpdateCheck(string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Installing update {Version} and restarting")]
    private partial void LogInstallingUpdate(string? version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Update failed")]
    private partial void LogUpdateFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Setup step {Step}")]
    private partial void LogSetupStep(int step);
}
