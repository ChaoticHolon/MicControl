using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MicControl.Installation;
using MicControl.Platform;
using MicControl.ViewModels;
using MicControl.Views;
using Microsoft.Extensions.Logging.Abstractions;

namespace MicControl.Tests.Ui;

public sealed class InstallerViewModelTests
{
    private static InstallerViewModel Create(FakeInstaller installer, SetupCommand? command = null) =>
        new(installer, command ?? new SetupCommand(SetupMode.Install), NullLogger<InstallerViewModel>.Instance);

    [AvaloniaFact]
    public async Task Install_DefaultsToAllUsers_AndInstallsWhenElevated()
    {
        var installer = new FakeInstaller { IsElevated = true };
        var vm = Create(installer);

        Assert.True(vm.ForAllUsers);
        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Equal([InstallScope.AllUsers], installer.Installs);
        Assert.True(vm.IsDonePage);

        vm.FinishCommand.Execute(null);
        Assert.Equal([InstallScope.AllUsers], installer.Launches);
    }

    [AvaloniaFact]
    public async Task Install_ForAllUsers_WithoutAdmin_ContinuesInAnElevatedCopy()
    {
        var installer = new FakeInstaller();
        var vm = Create(installer);
        var closed = false;
        vm.CloseRequested += (_, _) => closed = true;

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Equal(new SetupCommand(SetupMode.Install, Scope: InstallScope.AllUsers), Assert.Single(installer.ElevatedRestarts));
        Assert.Empty(installer.Installs);
        Assert.True(closed);
    }

    [AvaloniaFact]
    public async Task Install_AdminPromptDeclined_StaysAndExplains()
    {
        var installer = new FakeInstaller { AcceptElevation = false };
        var vm = Create(installer);

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.True(vm.IsStartPage);
        Assert.Contains("Just for me", vm.Error, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Install_JustMe_NeedsNoAdmin()
    {
        var installer = new FakeInstaller();
        var vm = Create(installer);
        vm.ForJustMe = true;

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Empty(installer.ElevatedRestarts);
        Assert.Equal([InstallScope.CurrentUser], installer.Installs);
        vm.LaunchAfterInstall = false;
        vm.FinishCommand.Execute(null);
        Assert.Empty(installer.Launches);
    }

    [AvaloniaFact]
    public async Task ElevatedRestart_CarriesStraightOn()
    {
        var installer = new FakeInstaller { IsElevated = true };
        var vm = Create(installer, new SetupCommand(SetupMode.Install, Scope: InstallScope.AllUsers));

        await vm.StartAsync();

        Assert.Equal([InstallScope.AllUsers], installer.Installs);
        Assert.True(vm.IsDonePage);
    }

    [AvaloniaFact]
    public async Task Install_Failure_ShowsTheReason_AndCanGoBack()
    {
        var installer = new FakeInstaller { IsElevated = true, Failure = new IOException("The disk is full.") };
        var vm = Create(installer);

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.True(vm.IsFailedPage);
        Assert.Equal("The disk is full.", vm.Error);
        vm.BackCommand.Execute(null);
        Assert.True(vm.IsStartPage);
        Assert.Null(vm.Error);
    }

    [AvaloniaFact]
    public void Notes_ExistingAndOtherCopies()
    {
        var installer = new FakeInstaller();
        installer.Installed[InstallScope.AllUsers] = new InstalledCopy(InstallScope.AllUsers, @"C:\Program Files\MicControl", "2.0.0");
        var vm = Create(installer);

        Assert.Contains("2.0.0 is already installed", vm.ExistingNote, StringComparison.Ordinal);
        Assert.Null(vm.OtherCopyNote);
        vm.ForJustMe = true;
        Assert.Null(vm.ExistingNote);
        Assert.Contains("also installed for all users", vm.OtherCopyNote, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Silent_UpdateInstallsInItsScope_AndRelaunches()
    {
        var installer = new FakeInstaller();
        var vm = Create(installer, new SetupCommand(SetupMode.Install, Silent: true, Scope: InstallScope.CurrentUser, Relaunch: true));

        Assert.True(await vm.RunSilentAsync());
        Assert.Equal([InstallScope.CurrentUser], installer.Installs);
        Assert.Equal([InstallScope.CurrentUser], installer.Launches);
    }

    [AvaloniaFact]
    public async Task Uninstall_AllUsersCopy_WithoutAdmin_AsksForIt()
    {
        var installer = new FakeInstaller { Current = new InstalledCopy(InstallScope.AllUsers, @"C:\Program Files\MicControl", "2.0.0") };
        var vm = Create(installer, new SetupCommand(SetupMode.Uninstall));

        Assert.Contains("for everyone on this PC", vm.UninstallDescription, StringComparison.Ordinal);
        await vm.UninstallCommand.ExecuteAsync(null);

        Assert.Equal(SetupMode.Uninstall, Assert.Single(installer.ElevatedRestarts).Mode);
        Assert.Equal(0, installer.Uninstalls);
    }

    [AvaloniaFact]
    public async Task Uninstall_JustMeCopy_Removes()
    {
        var installer = new FakeInstaller { Current = new InstalledCopy(InstallScope.CurrentUser, @"C:\Users\me\AppData\Local\Programs\MicControl", "2.0.0") };
        var vm = Create(installer, new SetupCommand(SetupMode.Uninstall));

        await vm.UninstallCommand.ExecuteAsync(null);

        Assert.Equal(1, installer.Uninstalls);
        Assert.True(vm.IsDonePage);
        Assert.Equal("Close", vm.FinishText);
    }

    [AvaloniaFact]
    public void Window_ShowsTheChoice()
    {
        var window = new InstallerWindow(Create(new FakeInstaller()));
        window.Show();

        Assert.Equal("MicControl Setup", window.Title);
        Assert.Equal(2, window.GetLogicalDescendantsOfType<RadioButton>().Count(r => r.IsEffectivelyVisible));
        window.Close();
    }
}

internal static class LogicalTreeExtensions
{
    public static IEnumerable<T> GetLogicalDescendantsOfType<T>(this Avalonia.LogicalTree.ILogical root) =>
        Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root).OfType<T>();
}
