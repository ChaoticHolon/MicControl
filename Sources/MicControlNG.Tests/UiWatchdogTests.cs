using MicControlNG.Diagnostics;

namespace MicControlNG.Tests;

public sealed class UiWatchdogTests
{
    [Fact]
    public void Healthy_WhileEveryPingIsHandled()
    {
        var watchdog = new UiWatchdog(ping => ping(), missesBeforeFailure: 3);

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(UiWatchdogState.Healthy, watchdog.Check());
        }
    }

    [Fact]
    public void Fails_AfterConsecutiveUnansweredPings()
    {
        var queued = new List<Action>();
        var watchdog = new UiWatchdog(queued.Add, missesBeforeFailure: 3);

        Assert.Equal(UiWatchdogState.Healthy, watchdog.Check());
        Assert.Equal(UiWatchdogState.Late, watchdog.Check());
        Assert.Equal(UiWatchdogState.Late, watchdog.Check());
        Assert.Equal(UiWatchdogState.Failed, watchdog.Check());
        Assert.Equal(3, watchdog.Misses);
    }

    [Fact]
    public void Recovers_WhenTheUiThreadCatchesUp()
    {
        var queued = new List<Action>();
        var watchdog = new UiWatchdog(queued.Add, missesBeforeFailure: 3);
        watchdog.Check();
        watchdog.Check();

        // The UI thread was only busy: it runs everything that was queued.
        queued.ForEach(ping => ping());
        queued.Clear();

        Assert.Equal(UiWatchdogState.Healthy, watchdog.Check());
        Assert.Equal(0, watchdog.Misses);
    }
}
