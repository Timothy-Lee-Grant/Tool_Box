using Microsoft.Extensions.Configuration;
using ToolBox.Core.Desired;
using ToolBox.Core.Health;

namespace ToolBox.Host;

/// <summary>
/// Answers "which toolsets should be on right now?" each time the reconciler is triggered (#32 step 7).
/// It reuses #31's types (<see cref="DesiredStateStore"/>, <see cref="EnabledResolution"/>) and does not
/// parse config itself. An env pin stays authoritative because <see cref="EnabledResolution.Resolve"/> applies it.
/// A corrupt or unsupported file keeps the last good roster and reports <c>components.desiredState</c>;
/// a good or missing file clears that report.
/// </summary>
internal sealed class DesiredRosterSource
{
    private readonly DesiredStateStore _store;
    private readonly IConfiguration _configuration;
    private readonly IReadOnlyList<string> _knownIds;
    private readonly ComponentStatusRegistry _registry;
    private readonly object _lock = new();
    private IReadOnlyList<string> _lastGood;

    public DesiredRosterSource(
        DesiredStateStore store,
        IConfiguration configuration,
        IEnumerable<string> knownIds,
        ComponentStatusRegistry registry,
        EnabledResolution initial)
    {
        _store = store;
        _configuration = configuration;
        _knownIds = knownIds.ToArray();
        _registry = registry;
        _lastGood = initial.Effective;
    }

    public string FilePath => _store.Path;

    /// <summary>Re-reads the file and returns the roster the reconciler should converge to.</summary>
    public IReadOnlyList<string> Refresh()
    {
        lock (_lock)
        {
            var loaded = _store.Load();
            switch (loaded.Status)
            {
                case DesiredStateLoadStatus.Corrupt:
                    _registry.Set("desiredState", $"corrupt: {loaded.Reason}");
                    return _lastGood;
                case DesiredStateLoadStatus.Unsupported:
                    _registry.Set("desiredState",
                        $"unsupported: schemaVersion {loaded.FoundVersion} (this build understands {DesiredState.CurrentSchemaVersion})");
                    return _lastGood;
                default:
                    _registry.Remove("desiredState");
                    _lastGood = EnabledResolution.Resolve(_configuration, loaded.State, _knownIds).Effective;
                    return _lastGood;
            }
        }
    }
}
