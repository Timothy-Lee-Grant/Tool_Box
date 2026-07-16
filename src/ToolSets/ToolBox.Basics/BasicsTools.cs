using System.ComponentModel;
using ModelContextProtocol.Server;
using ToolBox.Core;

namespace ToolBox.Basics;

/// <summary>
/// Deliberately trivial tools (plan 001, Stage 2 decision 4). Their job is to prove
/// the platform's plumbing — round-tripping, DI into tools, result serialization —
/// with no interesting logic to hide architectural mistakes behind.
///
/// Note: every [Description] below is a prompt. The model never sees this C# code;
/// it decides when and how to call a tool purely from those strings.
/// </summary>
[McpServerToolType]
public sealed class BasicsTools
{
    private readonly ServerInfoProvider _serverInfo;
    private readonly TimeProvider _clock;

    public BasicsTools(ServerInfoProvider serverInfo, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(serverInfo);
        ArgumentNullException.ThrowIfNull(clock);
        _serverInfo = serverInfo;
        _clock = clock;
    }

    [McpServerTool(Name = "ping")]
    [Description("Connectivity check. Returns 'pong', echoing back any message you provide. " +
                 "Use this to verify the ToolBox server is reachable and responding.")]
    public string Ping(
        [Description("Optional message to echo back, e.g. to verify round-tripping of text.")]
        string? message = null)
    {
        string reply = string.IsNullOrWhiteSpace(message) ? "pong" : $"pong: {message}";

        // Even a trivial tool routes string output through the limiter —
        // the discipline is the point, not this particular string.
        return OutputLimiter.Limit(reply);
    }

    [McpServerTool(Name = "server_info")]
    [Description("Describes this ToolBox server: its version, which toolsets are currently " +
                 "loaded (i.e. what capabilities are available), and how long it has been running.")]
    public ServerInfo GetServerInfo() => _serverInfo.Get();

    [McpServerTool(Name = "current_time")]
    [Description("Returns the server's current date and time: UTC and local, both in ISO-8601 " +
                 "format, plus the server's time zone identifier. Use this instead of guessing " +
                 "the time or date.")]
    public CurrentTime GetCurrentTime()
    {
        DateTimeOffset utcNow = _clock.GetUtcNow();
        TimeZoneInfo zone = _clock.LocalTimeZone;
        DateTimeOffset localNow = TimeZoneInfo.ConvertTime(utcNow, zone);

        return new CurrentTime(
            Utc: utcNow.ToString("O"),
            Local: localNow.ToString("O"),
            TimeZone: zone.Id);
    }
}
