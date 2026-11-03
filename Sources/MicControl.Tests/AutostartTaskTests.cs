using System.Xml.Linq;
using MicControl.Platform;

namespace MicControl.Tests;

public sealed class AutostartTaskTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    // What "schtasks /Create /SC ONLOGON /RL HIGHEST /IT" registered before: the scheduler's defaults.
    private const string OldTask = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>true</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>true</StopIfGoingOnBatteries>
            <ExecutionTimeLimit>PT72H</ExecutionTimeLimit>
            <Priority>7</Priority>
          </Settings>
          <Actions Context="Author">
            <Exec><Command>C:\Apps\MicControl.exe</Command><Arguments>--autostart</Arguments></Exec>
          </Actions>
        </Task>
        """;

    [Fact]
    public void New_task_runs_without_a_time_limit_on_battery_at_normal_priority()
    {
        var settings = XDocument.Parse(AutostartTask.CreateXml(@"C:\Apps\MicControl.exe", "--autostart", @"PC\Me")).Root!.Element(Ns + "Settings")!;

        Assert.Equal("PT0S", (string?)settings.Element(Ns + "ExecutionTimeLimit"));
        Assert.Equal("false", (string?)settings.Element(Ns + "DisallowStartIfOnBatteries"));
        Assert.Equal("false", (string?)settings.Element(Ns + "StopIfGoingOnBatteries"));
        Assert.Equal("4", (string?)settings.Element(Ns + "Priority"));
    }

    [Fact]
    public void New_task_starts_the_program_at_the_users_sign_in_with_highest_privileges()
    {
        var task = XDocument.Parse(AutostartTask.CreateXml(@"C:\a & b\MicControl.exe", "--autostart", @"PC\Me")).Root!;

        Assert.Equal(@"C:\a & b\MicControl.exe", (string?)task.Descendants(Ns + "Command").Single());
        Assert.Equal("--autostart", (string?)task.Descendants(Ns + "Arguments").Single());
        Assert.Equal(@"PC\Me", (string?)task.Element(Ns + "Triggers")!.Element(Ns + "LogonTrigger")!.Element(Ns + "UserId"));
        var principal = task.Element(Ns + "Principals")!.Element(Ns + "Principal")!;
        Assert.Equal(@"PC\Me", (string?)principal.Element(Ns + "UserId"));
        Assert.Equal("InteractiveToken", (string?)principal.Element(Ns + "LogonType"));
        Assert.Equal("HighestAvailable", (string?)principal.Element(Ns + "RunLevel"));
    }

    [Fact]
    public void New_task_needs_no_repair()
    {
        Assert.False(AutostartTask.NeedsRepair(AutostartTask.CreateXml(@"C:\Apps\MicControl.exe", "--autostart", @"PC\Me")));
    }

    [Fact]
    public void Task_with_the_schedulers_defaults_needs_repair()
    {
        Assert.True(AutostartTask.NeedsRepair(OldTask));
    }

    // One setting changed from the new task's: a value, or a missing element (which takes the scheduler's default).
    [Theory]
    [InlineData("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", "<ExecutionTimeLimit>PT72H</ExecutionTimeLimit>")]
    [InlineData("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>", "")]
    [InlineData("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>", "")]
    [InlineData("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>", "<StopIfGoingOnBatteries>true</StopIfGoingOnBatteries>")]
    [InlineData("<Priority>4</Priority>", "<Priority>7</Priority>")]
    [InlineData("<Priority>4</Priority>", "")]
    public void Any_one_default_left_needs_repair(string newSetting, string oldSetting)
    {
        var good = AutostartTask.CreateXml(@"C:\Apps\MicControl.exe", "--autostart", @"PC\Me");
        var xml = good.Replace(newSetting, oldSetting, StringComparison.Ordinal);

        Assert.NotEqual(good, xml);
        Assert.True(AutostartTask.NeedsRepair(xml));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<not xml")]
    [InlineData("<Task xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\" />")]
    public void Missing_or_unreadable_task_is_left_alone(string? xml)
    {
        Assert.False(AutostartTask.NeedsRepair(xml));
    }
}
