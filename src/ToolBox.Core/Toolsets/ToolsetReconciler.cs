namespace ToolBox.Core.Toolsets;

/// <summary>What one <see cref="ToolsetReconciler.ReconcileAsync"/> did.</summary>
/// <param name="Superseded">True when a newer request arrived while this one waited; nothing was applied.</param>
public sealed record ReconcileResult(
    IReadOnlyList<string> Started,
    IReadOnlyList<string> Stopped,
    IReadOnlyList<string> Unchanged,
    IReadOnlyDictionary<string, string> Failed,
    bool Superseded = false)
{
    public static readonly ReconcileResult SupersededResult =
        new([], [], [], new Dictionary<string, string>(), true);

    /// <summary>True when the run changed nothing and nothing failed.</summary>
    public bool IsNoOp => Started.Count == 0 && Stopped.Count == 0 && Failed.Count == 0;
}

/// <summary>
/// The Kubernetes-controller pattern in miniature: diff desired against actual, then act on
/// the difference. Pure logic: no MCP, no config, no wall clock (it takes a <see cref="TimeProvider"/>).
/// Idempotent: if actual already matches desired, no lifecycle is called.
/// Failure isolation: a throwing lifecycle marks only that toolset Failed.
/// Serialized: runs never overlap; if requests pile up, only the newest is applied.
/// </summary>
public sealed class ToolsetReconciler
{
    private readonly IReadOnlyList<string> _knownIds;
    private readonly Dictionary<string, IToolsetLifecycle> _lifecycles;
    private readonly ToolsetGate _gate;
    private readonly ToolsetStatusBoard _board;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private long _requestVersion;

    /// <param name="knownIds">Every toolset id in the catalog.</param>
    /// <param name="lifecycles">Lifecycles for toolsets that have side effects; others are gate-only.</param>
    public ToolsetReconciler(
        IEnumerable<string> knownIds,
        IEnumerable<IToolsetLifecycle> lifecycles,
        ToolsetGate gate,
        ToolsetStatusBoard board,
        TimeProvider? time = null)
    {
        _knownIds = knownIds.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        _lifecycles = lifecycles.ToDictionary(l => l.ToolsetId, StringComparer.OrdinalIgnoreCase);
        _gate = gate;
        _board = board;
        _time = time ?? TimeProvider.System;
    }

    public async Task<ReconcileResult> ReconcileAsync(IReadOnlyCollection<string> desired, CancellationToken ct = default)
    {
        var myVersion = Interlocked.Increment(ref _requestVersion);
        var want = new HashSet<string>(desired, StringComparer.OrdinalIgnoreCase);

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // A newer request is waiting behind us: last desired wins, skip the stale one.
            if (Volatile.Read(ref _requestVersion) != myVersion)
                return ReconcileResult.SupersededResult;

            var started = new List<string>();
            var stopped = new List<string>();
            var unchanged = new List<string>();
            var failed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var id in _knownIds)
            {
                ct.ThrowIfCancellationRequested();
                var shouldRun = want.Contains(id);
                var status = _board.Get(id).Status;

                if (shouldRun && status == ToolsetStatus.On) { unchanged.Add(id); continue; }
                if (!shouldRun && status != ToolsetStatus.On)
                {
                    // Off stays Off; a Failed toolset nobody wants any more is simply cleared.
                    if (status == ToolsetStatus.Failed) _board.Set(id, ToolsetState.Off);
                    unchanged.Add(id);
                    continue;
                }

                var error = shouldRun ? await StartAsync(id, ct).ConfigureAwait(false)
                                      : await StopAsync(id, ct).ConfigureAwait(false);
                if (error is not null) failed[id] = error;
                else (shouldRun ? started : stopped).Add(id);
            }

            // Ids asked for but absent from the catalog are reported, never silently ignored.
            foreach (var id in want.Where(w => !_knownIds.Contains(w, StringComparer.OrdinalIgnoreCase)))
                failed[id] = $"unknown toolset '{id}'";

            return new ReconcileResult(started, stopped, unchanged, failed);
        }
        finally
        {
            _lock.Release();
        }
    }

    // Start order: Start succeeds, then the gate opens, so a visible tool is always a working one.
    private async Task<string?> StartAsync(string id, CancellationToken ct)
    {
        _board.Set(id, new ToolsetState(ToolsetStatus.Starting));
        try
        {
            if (_lifecycles.TryGetValue(id, out var lifecycle))
                await lifecycle.StartAsync(ct).ConfigureAwait(false);
            _gate.Open(id);
            _board.Set(id, new ToolsetState(ToolsetStatus.On));
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _board.Set(id, ToolsetState.Off);
            throw;
        }
        catch (Exception ex)
        {
            _gate.Close(id);
            return Fail(id, ex);
        }
    }

    // Stop order: close the gate first, so no new call reaches a toolset that is shutting down.
    private async Task<string?> StopAsync(string id, CancellationToken ct)
    {
        _gate.Close(id);
        _board.Set(id, new ToolsetState(ToolsetStatus.Stopping));
        try
        {
            if (_lifecycles.TryGetValue(id, out var lifecycle))
                await lifecycle.StopAsync(ct).ConfigureAwait(false);
            _board.Set(id, ToolsetState.Off);
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _board.Set(id, ToolsetState.Off);
            throw;
        }
        catch (Exception ex)
        {
            return Fail(id, ex);
        }
    }

    private string Fail(string id, Exception ex)
    {
        var reason = ex.Message;
        _board.Set(id, new ToolsetState(ToolsetStatus.Failed, reason, _time.GetUtcNow()));
        return reason;
    }
}
