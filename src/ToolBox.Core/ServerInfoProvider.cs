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

    private readonly ToolBox.Core.Toolsets.ToolsetGate? _gate;

    /// <param name="gate">When given, only toolsets the gate reports on are listed (descriptor name = toolset id, case-insensitive).</param>
    public ServerInfoProvider(TimeProvider clock, IEnumerable<ToolsetDescriptor> toolsets,
        ToolBox.Core.Toolsets.ToolsetGate? gate = null)
    {
        _gate = gate;
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(toolsets);

        _clock = clock;
        _startedAt = clock.GetUtcNow();
        _toolsetNames = toolsets.Select(t => t.Name).ToArray();
    }

    public ServerInfo Get() => new(
        Version: typeof(ServerInfoProvider).Assembly.GetName().Version?.ToString() ?? "unknown",
        Toolsets: _gate is null ? _toolsetNames : _toolsetNames.Where(_gate.IsOn).ToArray(),
        Uptime: _clock.GetUtcNow() - _startedAt);
}
