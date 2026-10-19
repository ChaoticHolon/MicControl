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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"""broken")]
    public void Missing_or_unreadable_entry_is_kept(string? command)
    {
        Assert.False(AutostartCommand.IsStale(command, _ => false));
    }
}
