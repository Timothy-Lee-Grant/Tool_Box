using ToolBox.Core;

namespace ToolBox.Core.Tests;

public class ServerInfoProviderTests
{
    /// <summary>
    /// The payoff of injecting TimeProvider instead of calling DateTimeOffset.UtcNow:
    /// tests own the clock. No Thread.Sleep, no flaky timing assertions.
    /// </summary>
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Uptime_MeasuresTimeSinceConstruction()
    {
        var clock = new TestClock();
        var provider = new ServerInfoProvider(clock, []);

        clock.Now += TimeSpan.FromMinutes(5);

        Assert.Equal(TimeSpan.FromMinutes(5), provider.Get().Uptime);
    }

    [Fact]
    public void Toolsets_ListsAllRegisteredDescriptorNames()
    {
        var clock = new TestClock();
        var provider = new ServerInfoProvider(clock,
        [
            new ToolsetDescriptor("Basics", "test"),
            new ToolsetDescriptor("Git", "test"),
        ]);

        Assert.Equal(["Basics", "Git"], provider.Get().Toolsets);
    }

    [Fact]
    public void Version_IsNeverEmpty()
    {
        var provider = new ServerInfoProvider(new TestClock(), []);
        Assert.False(string.IsNullOrWhiteSpace(provider.Get().Version));
    }
}
