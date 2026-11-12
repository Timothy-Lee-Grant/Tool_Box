using Microsoft.Extensions.Configuration;
using ToolBox.Core.Desired;
using ToolBox.Core.Health;
using ToolBox.Core.Toolsets;

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
    private EnabledResolution _lastGood;

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
        _lastGood = initial;
    }

    public string FilePath => _store.Path;

    /// <summary>
    /// Per known toolset: what was asked for (desired), what the reconciler is told to run
    /// (effective; differs only when an env pin overrides) and what is actually happening
    /// (the status board). Feeds /health (#32 step 8).
    /// </summary>
    public IReadOnlyDictionary<string, ToolsetDetail> Details(ToolsetStatusBoard board)
    {
        EnabledResolution current;
        lock (_lock) { current = _lastGood; }
        var result = new SortedDictionary<string, ToolsetDetail>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in _knownIds)
        {
            var state = board.Get(id);
            result[id.ToLowerInvariant()] = new ToolsetDetail(
                Desired: current.Desired.Contains(id, StringComparer.OrdinalIgnoreCase),
                Effective: current.Effective.Contains(id, StringComparer.OrdinalIgnoreCase),
                Actual: state.Status.ToString().ToLowerInvariant(),
                Reason: state.Status == ToolsetStatus.Failed ? state.Reason : null);
        }
        return result;
    }

    public IReadOnlyList<string> KnownIds => _knownIds;

    /// <summary>The roster as last resolved by the reconciler loop (may lag a just-saved file).</summary>
    public EnabledResolution Current { get { lock (_lock) { return _lastGood; } } }

    /// <summary>
    /// The admin API's view of one toolset (#35): <see cref="Details"/> plus the pin and settings.
    /// Takes the resolution explicitly so a write can describe the state it just saved instead of the
    /// cached roster, which stays stale until the reconciler loop runs.
    /// </summary>
    public static AdminToolsetView Describe(EnabledResolution resolution, ToolsetStatusBoard board, string id)
    {
        var state = board.Get(id);
        return new AdminToolsetView(
            Desired: resolution.Desired.Contains(id, StringComparer.OrdinalIgnoreCase),
            Effective: resolution.Effective.Contains(id, StringComparer.OrdinalIgnoreCase),
            Actual: state.Status.ToString().ToLowerInvariant(),
            Reason: state.Status == ToolsetStatus.Failed ? state.Reason : null,
            PinnedBy: resolution.PinnedBy.TryGetValue(id, out var pin) ? pin : null,
            Settings: resolution.Settings.TryGetValue(id, out var settings) ? settings : null);
    }

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
                    return _lastGood.Effective;
                case DesiredStateLoadStatus.Unsupported:
                    _registry.Set("desiredState",
                        $"unsupported: schemaVersion {loaded.FoundVersion} (this build understands {DesiredState.CurrentSchemaVersion})");
                    return _lastGood.Effective;
                default:
                    _registry.Remove("desiredState");
                    _lastGood = EnabledResolution.Resolve(_configuration, loaded.State, _knownIds);
                    return _lastGood.Effective;
            }
        }
    }
}

/// <summary>
/// One toolset as the admin API reports it. A separate type from <see cref="ToolsetDetail"/> on purpose:
/// /health is unauthenticated and settings may hold paths, so they must never appear there.
/// States use the /health vocabulary: off, starting, on, stopping, failed.
/// </summary>
internal sealed record AdminToolsetView(
    bool Desired,
    bool Effective,
    string Actual,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? Reason,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? PinnedBy,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyDictionary<string, System.Text.Json.JsonElement>? Settings);

/// <summary>One toolset's row in /health. <c>Reason</c> is set only when <c>Actual</c> is "failed".</summary>
internal sealed record ToolsetDetail(
    bool Desired,
    bool Effective,
    string Actual,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? Reason);
