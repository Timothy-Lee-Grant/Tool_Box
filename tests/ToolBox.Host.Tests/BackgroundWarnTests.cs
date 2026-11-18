using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ToolBox.Host.Tests;

/// <summary>
/// Issue #102 (gap from #38): the non-loopback warning is not changed by <c>--mode background</c>.
/// Unit-level on purpose: the warning has no mode-specific code path, so we check Resolve + LogStartup with Mode=background set.
/// </summary>
public sealed class BackgroundWarnTests
{
    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static List<(LogLevel Level, string Message)> Run(string urls, params (string Key, string Value)[] extra)
    {
        var settings = new Dictionary<string, string?> { [BackgroundMode.ModeKey] = "background" };
        foreach (var (k, v) in extra) settings[k] = v;
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        Assert.True(BackgroundMode.IsBackground(config));

        var logger = new CapturingLogger();
        McpGuardOptions.Resolve(config, urls).LogStartup(logger);
        return logger.Entries;
    }

    [Fact]
    public void Non_loopback_bind_with_default_protections_warns_in_background_mode()
    {
        var entries = Run("http://0.0.0.0:5000");

        var warning = Assert.Single(entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("non-loopback", warning.Message);
    }

    [Fact]
    public void Loopback_bind_does_not_warn_in_background_mode()
    {
        Assert.DoesNotContain(Run("http://127.0.0.1:5000"), e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void Non_loopback_bind_with_an_allowed_host_does_not_warn_in_background_mode()
    {
        var entries = Run("http://0.0.0.0:5000", (McpGuardOptions.HostsKey + ":0", "toolbox"));

        Assert.DoesNotContain(entries, e => e.Level == LogLevel.Warning);
    }
}
