using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ToolBox.Core.Toolsets;

namespace ToolBox.Host;

/// <summary>The manual trigger. The admin API (#35) will call this; there is no HTTP endpoint in #32.</summary>
internal interface IToolsetReconciler
{
    /// <summary>Asks for a reconcile now (not debounced). Returns immediately; the work runs on the service's loop.</summary>
    void RequestReconcile();
}

/// <summary>
/// The Host's driver for <see cref="ToolsetReconciler"/> (ADR-010 hosted service). Triggers:
/// startup (reconcile to the effective roster), desired-state file change (debounced on the
/// <see cref="TimeProvider"/> so N rapid edits give one reconcile) and manual request. Shutdown
/// reconciles to nothing so every lifecycle is stopped. Logs on stderr (ADR-004).
/// </summary>
internal sealed class ToolsetReconcilerService : IHostedService, IToolsetReconciler, IDisposable
{
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(300);

    private readonly ToolsetReconciler _reconciler;
    private readonly DesiredRosterSource _roster;
    private readonly EnabledResolution _resolution;
    private readonly ILogger<ToolsetReconcilerService> _logger;
    private readonly TimeProvider _time;
    private readonly TimeSpan _debounce;
    private readonly bool _watchFile;
    // true = manual (skip the debounce), false = file change (debounce).
    private readonly Channel<bool> _signals = Channel.CreateUnbounded<bool>();
    private DesiredStateWatcher? _watcher;
    private CancellationTokenSource? _cts;
    private Task _loop = Task.CompletedTask;

    public ToolsetReconcilerService(
        ToolsetReconciler reconciler,
        DesiredRosterSource roster,
        EnabledResolution resolution,
        ILogger<ToolsetReconcilerService> logger,
        TimeProvider time,
        TimeSpan? debounce = null,
        bool watchFile = true)
    {
        _reconciler = reconciler;
        _roster = roster;
        _resolution = resolution;
        _logger = logger;
        _time = time;
        _debounce = debounce ?? DefaultDebounce;
        _watchFile = watchFile;
    }

    public void RequestReconcile() => _signals.Writer.TryWrite(true);

    /// <summary>What the file watcher calls; also the test seam for debounce.</summary>
    internal void NotifyDesiredStateChanged() => _signals.Writer.TryWrite(false);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var result = await _reconciler.ReconcileAsync(_resolution.Effective, cancellationToken);
        var on = _resolution.Effective
            .Where(id => !result.Failed.ContainsKey(id))
            .Select(id => id.ToLowerInvariant()).Distinct().ToArray();
        _logger.LogInformation("Enabled toolsets: {Toolsets}", on.Length == 0 ? "(none)" : string.Join(", ", on));
        LogFailures(result, "start");

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
        if (_watchFile)
        {
            _watcher = new DesiredStateWatcher(_roster.FilePath, NotifyDesiredStateChanged, _logger);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher?.Dispose();
        _watcher = null;
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            try { await _loop.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { /* expected on shutdown */ }
        }

        // Deliberately NOT the host's token: by now it may already be cancelled, which would make
        // the reconciler throw before stopping anything. Own timeout instead.
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var result = await _reconciler.ReconcileAsync([], stopCts.Token);
            LogFailures(result, "stop");
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Shutdown reconcile timed out; some toolsets may not have stopped cleanly.");
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (await _signals.Reader.WaitToReadAsync(ct))
            {
                _signals.Reader.TryRead(out var manual);
                if (!manual)
                {
                    // Coalesce: edits (and atomic-save temp renames) arriving in this window become one run.
                    await Task.Delay(_debounce, _time, ct);
                }
                while (_signals.Reader.TryRead(out _)) { }

                await ReconcileOnceAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // shutting down
        }
    }

    private async Task ReconcileOnceAsync(CancellationToken ct)
    {
        try
        {
            var roster = _roster.Refresh();
            var result = await _reconciler.ReconcileAsync(roster, ct);
            if (result.Started.Count > 0 || result.Stopped.Count > 0)
            {
                _logger.LogInformation("Reconciled: started [{Started}], stopped [{Stopped}]",
                    string.Join(", ", result.Started), string.Join(", ", result.Stopped));
            }
            LogFailures(result, "start");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reconcile failed unexpectedly; the next trigger will retry.");
        }
    }

    private void LogFailures(ReconcileResult result, string verb)
    {
        foreach (var (id, reason) in result.Failed)
        {
            _logger.LogError("Toolset '{Toolset}' failed to {Verb}: {Reason}", id, verb, reason);
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _cts?.Dispose();
    }
}
