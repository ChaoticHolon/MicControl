using MicControlNG.Installation;

namespace MicControlNG.Tests;

public sealed class InstallationTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "mc-install-tests");
    private static readonly string ProgramFiles = Path.Combine(Root, "Program Files");
    private static readonly string LocalAppData = Path.Combine(Root, "Users", "me", "AppData", "Local");

    [Theory]
    [InlineData(new string[0], "MicControlNG.exe", SetupMode.None)] // the installed app or a dev build
    [InlineData(new string[0], "MicControlNG-Setup-2.1.0.exe", SetupMode.Install)] // the download
    [InlineData(new string[0], "micontrolng-setup-2.1.0 (1).exe", SetupMode.None)] // not ours
    [InlineData(new string[0], "MicControlNG-Setup-2.1.0 (1).exe", SetupMode.Install)] // a browser's second copy
    [InlineData(new[] { "--install" }, "MicControlNG.exe", SetupMode.Install)]
    [InlineData(new[] { "--uninstall" }, "MicControlNG.exe", SetupMode.Uninstall)]
    [InlineData(new[] { "/UNINSTALL" }, "MicControlNG.exe", SetupMode.Uninstall)]
    [InlineData(new[] { "--autostart" }, "MicControlNG.exe", SetupMode.None)]
    public void Parse_DecidesWhatALaunchDoes(string[] args, string exe, SetupMode expected)
    {
        Assert.Equal(expected, SetupCommand.Parse(args, Path.Combine(Root, exe)).Mode);
    }

    [Fact]
    public void Parse_ReadsSilentScopeAndRelaunch()
    {
        var command = SetupCommand.Parse(["--install", "--silent", "--scope=user", "--relaunch"], "MicControlNG.exe");

        Assert.Equal(new SetupCommand(SetupMode.Install, true, InstallScope.CurrentUser, true), command);
        Assert.Equal(InstallScope.AllUsers, SetupCommand.Parse(["--scope=all"], "MicControlNG-Setup-1.exe").Scope);
        Assert.Null(SetupCommand.Parse(["--scope=nonsense"], "MicControlNG-Setup-1.exe").Scope);
        Assert.False(SetupCommand.Parse(["--silent"], "MicControlNG.exe").Silent); // silent only means something in setup
    }

    [Fact]
    public void ToArguments_RoundTrips()
    {
        var command = new SetupCommand(SetupMode.Uninstall, Silent: true, Scope: InstallScope.AllUsers);

        Assert.Equal(command, SetupCommand.Parse(command.ToArguments().Split(' '), "MicControlNG.exe"));
    }

    [Fact]
    public void Folders_AllUsersInProgramFiles_JustMeInLocalPrograms()
    {
        Assert.Equal(Path.Combine(ProgramFiles, "MicControlNG"), InstallLayout.DirectoryFor(InstallScope.AllUsers, ProgramFiles, LocalAppData));
        Assert.Equal(Path.Combine(LocalAppData, "Programs", "MicControlNG"), InstallLayout.DirectoryFor(InstallScope.CurrentUser, ProgramFiles, LocalAppData));
        Assert.Equal(Path.Combine(Root, "common", "MicControl.lnk"), InstallLayout.ShortcutFor(InstallScope.AllUsers, Path.Combine(Root, "common"), Path.Combine(Root, "user")));
        Assert.Equal(Path.Combine(Root, "user", "MicControl.lnk"), InstallLayout.ShortcutFor(InstallScope.CurrentUser, Path.Combine(Root, "common"), Path.Combine(Root, "user")));
    }

    [Fact]
    public void ScopeOf_RecognisesInstallFolders()
    {
        var allUsers = InstallLayout.DirectoryFor(InstallScope.AllUsers, ProgramFiles, LocalAppData);
        var justMe = InstallLayout.DirectoryFor(InstallScope.CurrentUser, ProgramFiles, LocalAppData);

        Assert.Equal(InstallScope.AllUsers, InstallLayout.ScopeOf(allUsers + Path.DirectorySeparatorChar, ProgramFiles, LocalAppData));
        Assert.Equal(InstallScope.CurrentUser, InstallLayout.ScopeOf(justMe.ToUpperInvariant(), ProgramFiles, LocalAppData));
        Assert.Null(InstallLayout.ScopeOf(Path.Combine(Root, "Downloads"), ProgramFiles, LocalAppData));
    }

    [Fact]
    public void UninstallEntry_RunsThisProgramToUninstall()
    {
        var folder = InstallLayout.DirectoryFor(InstallScope.AllUsers, ProgramFiles, LocalAppData);
        var entry = InstallLayout.UninstallEntry(folder, "2.1.0", 64 * 1024 * 1024);
        var exe = Path.Combine(folder, "MicControlNG.exe");

        Assert.Equal("MicControlNG", entry["DisplayName"]);
        Assert.Equal("2.1.0", entry["DisplayVersion"]);
        Assert.Equal(folder, entry["InstallLocation"]);
        Assert.Equal($"\"{exe}\" --uninstall", entry["UninstallString"]);
        Assert.Equal($"\"{exe}\" --uninstall --silent", entry["QuietUninstallString"]);
        Assert.Equal(64 * 1024, entry["EstimatedSize"]);
        Assert.Equal(SetupMode.Uninstall, SetupCommand.Parse(["--uninstall"], exe).Mode);
    }
}
