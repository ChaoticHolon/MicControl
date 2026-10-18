using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicControl.Installation;
using MicControl.Platform;
using Microsoft.Extensions.Logging;

namespace MicControl.ViewModels;

public enum InstallerPage
{
    /// <summary>Install: who to install for. Uninstall: confirm.</summary>
    Start,
    Working,
    Done,
    Failed,
}

/// <summary>
/// The setup and uninstall screens, shown by the app itself when it runs as MicControl-Setup-*.exe or with
/// --install/--uninstall. Talks to the OS only through <see cref="IInstaller"/>.
/// </summary>
public sealed partial class InstallerViewModel : ObservableObject
{
    private readonly IInstaller installer;
    private readonly SetupCommand command;
    private readonly ILogger logger;
    private InstallScope installedScope;

    public InstallerViewModel(IInstaller installer, SetupCommand command, ILogger<InstallerViewModel> logger)
    {
        this.installer = installer;
        this.command = command;
        this.logger = logger;
        // All users is the default; an elevated restart or a command line carries the choice already made.
        ForAllUsers = command.Scope != InstallScope.CurrentUser;
    }

    public bool IsUninstall => command.Mode == SetupMode.Uninstall;

    public bool IsInstall => !IsUninstall;

    public string Version { get; } = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "development build";

    public string WindowTitle => IsUninstall ? $"Uninstall {InstallLayout.ProductName}" : $"{InstallLayout.ProductName} Setup";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStartPage), nameof(IsWorkingPage), nameof(IsDonePage), nameof(IsFailedPage))]
    public partial InstallerPage Page { get; private set; }

    public bool IsStartPage => Page == InstallerPage.Start;

    public bool IsWorkingPage => Page == InstallerPage.Working;

    public bool IsDonePage => Page == InstallerPage.Done;

    public bool IsFailedPage => Page == InstallerPage.Failed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ForJustMe), nameof(ExistingNote), nameof(OtherCopyNote))]
    public partial bool ForAllUsers { get; set; }

    public bool ForJustMe
    {
        get => !ForAllUsers;
        set => ForAllUsers = !value;
    }

    private InstallScope ChosenScope => ForAllUsers ? InstallScope.AllUsers : InstallScope.CurrentUser;

    public string AllUsersDescription => $"Installs to {installer.DirectoryFor(InstallScope.AllUsers)}. Needs administrator permission.";

    public string JustMeDescription => $"Installs to {installer.DirectoryFor(InstallScope.CurrentUser)}. No administrator permission needed.";

    /// <summary>Shown when the chosen place already has a copy: installing updates it.</summary>
    public string? ExistingNote => installer.Find(ChosenScope) is { } copy
        ? $"MicControl {copy.Version ?? ""} is already installed here. Installing replaces it; settings are kept.".Replace("  ", " ", StringComparison.Ordinal)
        : null;

    /// <summary>Shown when the other place has a copy too, so there could be two.</summary>
    public string? OtherCopyNote
    {
        get
        {
            var other = ChosenScope == InstallScope.AllUsers ? InstallScope.CurrentUser : InstallScope.AllUsers;
            return installer.Find(other) is null
                ? null
                : other == InstallScope.AllUsers
                    ? "MicControl is also installed for all users. You only need one copy: remove the other from Settings → Apps."
                    : "MicControl is also installed just for you. You only need one copy: remove the other from Settings → Apps.";
        }
    }

    public string UninstallDescription => installer.Current is { } copy
        ? $"This removes MicControl {copy.Version ?? Version} {(copy.Scope == InstallScope.AllUsers ? "for everyone on this PC" : "for you")}, " +
          "with its Start menu shortcut and Start with Windows. Each user's settings stay in their AppData folder, so reinstalling picks them up."
        : "This copy of MicControl isn't installed, so there's nothing to remove.";

    public bool CanUninstall => installer.Current is not null;

    [ObservableProperty]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial string? Error { get; private set; }

    /// <summary>Done page: start MicControl when the window closes.</summary>
    [ObservableProperty]
    public partial bool LaunchAfterInstall { get; set; } = true;

    public string FinishText => IsInstall ? "Finish" : "Close";

    public string WorkingTitle => IsUninstall ? "Removing MicControl…" : "Installing MicControl…";

    public string DoneTitle => IsUninstall ? "MicControl was removed" : "MicControl is installed";

    public string DoneText => IsUninstall
        ? "Your settings are still in your AppData folder in case you install it again."
        : "It starts with Windows and lives in the notification area, near the clock. Settings are kept separately for each user.";

    /// <summary>Raised when the window should close (finished, cancelled, or continued in an elevated copy).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// After an elevated restart the choice was already made in the first window, so carry straight on.
    /// </summary>
    public async Task StartAsync()
    {
        if (command.Scope is not null && !command.Silent)
        {
            await (IsUninstall ? Uninstall() : Install());
        }
    }

    /// <summary>For --silent: does the job without a window. Returns false on failure (already logged).</summary>
    public async Task<bool> RunSilentAsync()
    {
        if (IsUninstall)
        {
            await Uninstall();
        }
        else
        {
            ForAllUsers = (command.Scope ?? (installer.IsElevated ? InstallScope.AllUsers : InstallScope.CurrentUser)) == InstallScope.AllUsers;
            if (ForAllUsers && !installer.IsElevated)
            {
                // A standard user's update of an all-users install: an administrator does the install, and this copy,
                // still running as the user, starts the app again. Relaunched by the elevated copy, it would run as
                // the administrator who approved the prompt.
                var installed = await installer.RunElevatedAsync(command with { Scope = InstallScope.AllUsers, Relaunch = false });
                if (installed && command.Relaunch)
                {
                    installer.Launch(InstallScope.AllUsers);
                }

                return installed;
            }

            await Install();
            if (Page == InstallerPage.Done && command.Relaunch)
            {
                installer.Launch(installedScope);
            }
        }

        return Page == InstallerPage.Done;
    }

    [RelayCommand]
    private async Task Install()
    {
        var scope = ChosenScope;
        if (scope == InstallScope.AllUsers && !installer.IsElevated)
        {
            ContinueElevated(command with { Mode = SetupMode.Install, Scope = scope },
                "Installing for all users needs administrator permission. Choose \"Just for me\" to install without it.");
            return;
        }

        installedScope = scope;
        await RunAsync(() => installer.InstallAsync(scope, new Progress<double>(p => Progress = p), CancellationToken.None));
    }

    [RelayCommand]
    private async Task Uninstall()
    {
        if (installer.Current is { Scope: InstallScope.AllUsers } && !installer.IsElevated)
        {
            ContinueElevated(command with { Mode = SetupMode.Uninstall, Scope = InstallScope.AllUsers },
                "Removing MicControl for all users needs administrator permission.");
            return;
        }

        await RunAsync(() => installer.UninstallAsync(new Progress<double>(p => Progress = p), CancellationToken.None));
    }

    [RelayCommand]
    private void Finish()
    {
        if (IsInstall && Page == InstallerPage.Done && LaunchAfterInstall)
        {
            installer.Launch(installedScope);
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Back()
    {
        Error = null;
        Page = InstallerPage.Start;
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void ContinueElevated(SetupCommand elevated, string declined)
    {
        if (installer.RestartElevated(elevated))
        {
            // The elevated copy carries on with its own window.
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        Error = declined;
    }

    private async Task RunAsync(Func<Task> work)
    {
        Error = null;
        Progress = 0;
        Page = InstallerPage.Working;
        try
        {
            await work();
            Progress = 1;
            Page = InstallerPage.Done;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception or System.Security.SecurityException or System.Runtime.InteropServices.COMException)
        {
            LogFailed(IsUninstall ? "Uninstall" : "Install", ex);
            Error = ex.Message;
            Page = InstallerPage.Failed;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Operation} failed")]
    private partial void LogFailed(string operation, Exception exception);
}
