namespace ToolBox.Core;

/// <summary>
/// Singleton that answers "what is this server and what can it do right now?".
/// Takes <see cref="TimeProvider"/> instead of calling <c>DateTimeOffset.UtcNow</c>
/// directly so tests can control the clock (see Core.Tests).
/// </summary>
public sealed class ServerInfoProvider
{
    private readonly TimeProvider _clock;
    private readonly DateTimeOffset _startedAt;
    private readonly IReadOnlyList<string> _toolsetNames;

    public ServerInfoProvider(TimeProvider clock, IEnumerable<ToolsetDescriptor> toolsets)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(toolsets);

        _clock = clock;
        _startedAt = clock.GetUtcNow();
        _toolsetNames = toolsets.Select(t => t.Name).ToArray();
    }

    public ServerInfo Get() => new(
        Version: typeof(ServerInfoProvider).Assembly.GetName().Version?.ToString() ?? "unknown",
        Toolsets: _toolsetNames,
        Uptime: _clock.GetUtcNow() - _startedAt);
}
