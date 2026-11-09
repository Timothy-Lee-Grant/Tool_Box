using System.Collections.Concurrent;

namespace ToolBox.Core.Toolsets;

public enum ToolsetStatus { Off, Starting, On, Stopping, Failed }

/// <summary>What a toolset is doing now. "Off" is normal; only "Failed" carries a reason.</summary>
public sealed record ToolsetState(ToolsetStatus Status, string? Reason = null, DateTimeOffset? AtUtc = null)
{
    public static readonly ToolsetState Off = new(ToolsetStatus.Off);
}

/// <summary>
/// The reconciler's record of actual state per toolset, read by /health, the admin API (#35)
/// and the GUI (S2). Kept apart from <c>ComponentStatusRegistry</c>, whose strings other than
/// "ok" mean degraded; an Off toolset is not a problem.
/// </summary>
public sealed class ToolsetStatusBoard
{
    private readonly ConcurrentDictionary<string, ToolsetState> _states = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Unknown ids read as Off.</summary>
    public ToolsetState Get(string toolsetId) =>
        _states.TryGetValue(toolsetId, out var s) ? s : ToolsetState.Off;

    public IReadOnlyDictionary<string, ToolsetState> Snapshot() =>
        _states.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
               .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

    internal void Set(string toolsetId, ToolsetState state) => _states[toolsetId] = state;
}
