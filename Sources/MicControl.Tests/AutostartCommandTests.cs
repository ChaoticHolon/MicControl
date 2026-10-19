using MicControl.Platform;

namespace MicControl.Tests;

public sealed class AutostartCommandTests
{
    [Theory]
    [InlineData(@"""C:\Apps\MicSwitch\MicSwitch.exe"" --autostart", @"C:\Apps\MicSwitch\MicSwitch.exe")]
    [InlineData(@"C:\Users\Me\AppData\Local\MicSwitch\MicSwitch.exe --autostart", @"C:\Users\Me\AppData\Local\MicSwitch\MicSwitch.exe")]
    [InlineData(@"C:\Program Files\MicSwitch\MicSwitch.EXE", @"C:\Program Files\MicSwitch\MicSwitch.EXE")]
    [InlineData(@"  ""C:\a b\x.exe""  ", @"C:\a b\x.exe")]
    [InlineData("/usr/bin/micswitch --autostart", "/usr/bin/micswitch")]
    [InlineData(@"C:\Users\alex.exeter\AppData\Local\MicSwitch\MicSwitch.exe --autostart", @"C:\Users\alex.exeter\AppData\Local\MicSwitch\MicSwitch.exe")]
    [InlineData(@"C:\Tools.exe.d\MicSwitch.exe", @"C:\Tools.exe.d\MicSwitch.exe")]
    [InlineData(@"C:\Apps\.exe\MicSwitch.exe /minimized", @"C:\Apps\.exe\MicSwitch.exe")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData(@"""unterminated", null)]
    public void GetExecutable(string? command, string? expected)
    {
        Assert.Equal(expected, AutostartCommand.GetExecutable(command));
    }

    [Fact]
    public void Entry_for_a_missing_program_is_stale()
    {
        Assert.True(AutostartCommand.IsStale(@"""C:\Old\MicSwitch.exe"" --autostart", _ => false));
    }

    [Fact]
    public void Entry_for_an_installed_program_is_kept()
    {
        string? checkedPath = null;
        Assert.False(AutostartCommand.IsStale(@"C:\Old\MicSwitch.exe --autostart", path => (checkedPath = path) is not null));
        Assert.Equal(@"C:\Old\MicSwitch.exe", checkedPath);
    }

    [Fact]
    public void Entry_in_a_folder_with_exe_in_its_name_is_kept_while_the_program_exists()
    {
        // "alex.exeter" used to read as C:\Users\alex.exe, which doesn't exist, so a working MicSwitch lost its autostart.
        const string Exe = @"C:\Users\alex.exeter\AppData\Local\MicSwitch\MicSwitch.exe";
        Assert.False(AutostartCommand.IsStale(Exe + " --autostart", path => path == Exe));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"""broken")]
    public void Missing_or_unreadable_entry_is_kept(string? command)
    {
        Assert.False(AutostartCommand.IsStale(command, _ => false));
    }
}
