using System.Collections.Concurrent;

namespace ToolBox.Core.Health;

/// <summary>
/// Where optional components report their health ("ok", "unavailable: port 8090 in use").
/// Core only stores strings; it knows no component (ADR-003). /health, the admin API (#35)
/// and the GUI (S2) read the same snapshot. Nothing writes to it yet (issue #37, step 4).
/// </summary>
public sealed class ComponentStatusRegistry
{
    private readonly ConcurrentDictionary<string, string> _statuses = new(StringComparer.Ordinal);

    public void Set(string component, string status) => _statuses[component] = status;

    /// <summary>Stop reporting a component, e.g. the viewer once its toolset is switched off.</summary>
    public void Remove(string component) => _statuses.TryRemove(component, out _);

    /// <summary>A sorted copy, safe to serialize while other threads call <see cref="Set"/>.</summary>
    public IReadOnlyDictionary<string, string> Snapshot() =>
        _statuses.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                 .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
}
