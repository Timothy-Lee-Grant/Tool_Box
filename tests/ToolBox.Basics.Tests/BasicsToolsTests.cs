using ToolBox.Core;

namespace ToolBox.Basics.Tests;

public class BasicsToolsTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = FixedNow;
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc; // deterministic on any machine
    }

    /// <summary>
    /// Tools are plain methods — no MCP server, no transport, no process needed to test
    /// their behavior. That's the boundary rule paying its first dividend.
    /// </summary>
    private static BasicsTools CreateTools(TestClock? clock = null)
    {
        clock ??= new TestClock();
        var provider = new ServerInfoProvider(clock, [new ToolsetDescriptor("Basics", "test")]);
        return new BasicsTools(provider, clock);
    }

    [Fact]
    public void Ping_ReturnsPong_WithoutMessage()
        => Assert.Equal("pong", CreateTools().Ping());

    [Fact]
    public void Ping_EchoesMessage()
        => Assert.Equal("pong: hello", CreateTools().Ping("hello"));

    [Fact]
    public void Ping_TreatsWhitespaceAsNoMessage()
        => Assert.Equal("pong", CreateTools().Ping("   "));

    [Fact]
    public void Ping_TruncatesOversizedEcho()
    {
        // Proves the OutputLimiter is actually wired in, not just talked about.
        string huge = new('a', OutputLimiter.DefaultMaxChars + 5_000);

        string result = CreateTools().Ping(huge);

        Assert.Contains("truncated", result, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Length < huge.Length);
    }

    [Fact]
    public void ServerInfo_ReportsLoadedToolsets()
        => Assert.Contains("Basics", CreateTools().GetServerInfo().Toolsets);

    [Fact]
    public void ServerInfo_UptimeAdvancesWithClock()
    {
        var clock = new TestClock();
        var tools = CreateTools(clock);

        clock.Now += TimeSpan.FromSeconds(90);

        Assert.Equal(TimeSpan.FromSeconds(90), tools.GetServerInfo().Uptime);
    }

    [Fact]
    public void CurrentTime_UsesInjectedClock()
    {
        var result = CreateTools().GetCurrentTime();

        Assert.Equal(FixedNow.ToString("O"), result.Utc);
        Assert.Equal(FixedNow.ToString("O"), result.Local); // TestClock's zone is UTC
        Assert.Equal(TimeZoneInfo.Utc.Id, result.TimeZone);
    }
}
