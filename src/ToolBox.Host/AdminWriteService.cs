using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using ToolBox.Core.Desired;
using ToolBox.Core.Toolsets;

namespace ToolBox.Host;

/// <summary>
/// The single writer of the desired-state file (ADR-017). Every change is one critical section:
/// Load, modify one toolset's intent, Save, request a reconcile. The store's own lock covers only Save, so
/// without this lock two simultaneous PUTs could both Load the same state and one change would be lost.
/// The lock is per process; a hand edit during a PUT can still race (accepted, ADR-017).
/// </summary>
internal sealed class AdminWriteService
{
    private readonly DesiredStateStore _store;
    private readonly DesiredRosterSource _roster;
    private readonly IConfiguration _configuration;
    private readonly ToolsetStatusBoard _board;
    private readonly IToolsetReconciler _reconciler;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public AdminWriteService(
        DesiredStateStore store, DesiredRosterSource roster, IConfiguration configuration,
        ToolsetStatusBoard board, IToolsetReconciler reconciler)
    {
        _store = store;
        _roster = roster;
        _configuration = configuration;
        _board = board;
        _reconciler = reconciler;
    }

    /// <summary>The catalog's spelling of an id, or null when it is not a known toolset.</summary>
    public string? Canonical(string id) =>
        _roster.KnownIds.FirstOrDefault(k => string.Equals(k, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>404 for an unknown id, 409 when the env pins <c>enabled</c>; otherwise null (go on).</summary>
    public IResult? CheckEnabledWritable(string id, out string canonical)
    {
        canonical = Canonical(id) ?? string.Empty;
        if (canonical.Length == 0) return AdminProblems.UnknownToolset(id);
        return _roster.Current.PinnedBy.TryGetValue(canonical, out string? pin)
            ? AdminProblems.PinnedByEnv(pin)
            : null;
    }

    /// <summary>404 for an unknown id; otherwise null. Settings are writable even when the env pins <c>enabled</c>.</summary>
    public IResult? CheckSettingsWritable(string id, out string canonical)
    {
        canonical = Canonical(id) ?? string.Empty;
        return canonical.Length == 0 ? AdminProblems.UnknownToolset(id) : null;
    }

    public Task<IResult> SetEnabledAsync(string canonicalId, bool enabled, CancellationToken ct) =>
        MutateAsync(canonicalId, intent =>
        {
            if (intent.Enabled == enabled) return false;
            intent.Enabled = enabled;
            return true;
        }, ct);

    /// <summary>Replaces the toolset's settings object. Never touches <c>Enabled</c>.</summary>
    public Task<IResult> SetSettingsAsync(string canonicalId, Dictionary<string, JsonElement> settings, CancellationToken ct)
    {
        string incoming = JsonSerializer.Serialize(settings);
        return MutateAsync(canonicalId, intent =>
        {
            if (intent.Settings is not null && JsonSerializer.Serialize(intent.Settings) == incoming) return false;
            intent.Settings = settings;
            return true;
        }, ct);
    }

    /// <summary>The one critical section: Load, apply <paramref name="apply"/> to one toolset, Save, reconcile.</summary>
    private async Task<IResult> MutateAsync(string canonicalId, Func<DesiredToolset, bool> apply, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            DesiredStateLoadResult loaded = _store.Load();
            if (loaded.Status == DesiredStateLoadStatus.Unsupported)
                return AdminProblems.UnsupportedFile(loaded.FoundVersion, DesiredState.CurrentSchemaVersion);

            DesiredState state = loaded.State;
            if (!state.Toolsets.TryGetValue(canonicalId, out DesiredToolset? intent))
            {
                intent = new DesiredToolset();
                state.Toolsets[canonicalId] = intent;
            }

            string? backupPath = null;
            // Unchanged: skip the write, but still reconcile below (cheap, and it self-heals).
            // A missing or corrupt file always needs a write so the file ends up holding the intent.
            bool changed = apply(intent);
            if (loaded.Status != DesiredStateLoadStatus.Ok || changed)
            {
                DesiredStateSaveResult saved = _store.Save(state);
                switch (saved.Outcome)
                {
                    case DesiredStateSaveOutcome.RefusedUnsupported:
                        return AdminProblems.UnsupportedFile(loaded.FoundVersion, DesiredState.CurrentSchemaVersion);
                    case DesiredStateSaveOutcome.Failed:
                        return AdminProblems.WriteFailed(saved.Reason);
                }
                backupPath = saved.BackupPath;
            }

            _reconciler.RequestReconcile();

            // Describe the state we just saved, not the roster's cache (stale until the loop refreshes).
            var resolution = EnabledResolution.Resolve(_configuration, state, _roster.KnownIds);
            return Results.Json(new
            {
                saved = true,
                reconcile = "requested",
                backupPath,
                toolset = DesiredRosterSource.Describe(resolution, _board, canonicalId),
            }, statusCode: StatusCodes.Status202Accepted);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
