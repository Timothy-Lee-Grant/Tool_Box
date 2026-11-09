using ToolBox.Core.Toolsets;

namespace ToolBox.Core.Tests;

public class ToolsetReconcilerTests
{
    private sealed class FakeLifecycle(string id, List<string> log) : IToolsetLifecycle
    {
        public string ToolsetId => id;
        public Exception? FailStart { get; set; }
        public Exception? FailStop { get; set; }
        public Task? StartBlocker { get; set; }
        public int Active;
        public int MaxActive;

        public async Task StartAsync(CancellationToken ct)
        {
            log.Add($"start:{id}");
            var now = Interlocked.Increment(ref Active);
            MaxActive = Math.Max(MaxActive, now);
            try
            {
                if (StartBlocker is not null) await StartBlocker;
                if (FailStart is not null) throw FailStart;
            }
            finally { Interlocked.Decrement(ref Active); }
        }

        public Task StopAsync(CancellationToken ct)
        {
            log.Add($"stop:{id}");
            return FailStop is null ? Task.CompletedTask : throw FailStop;
        }
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private readonly List<string> _log = new();
    private readonly ToolsetGate _gate = new();
    private readonly ToolsetStatusBoard _board = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero));

    private (ToolsetReconciler Reconciler, FakeLifecycle Voxel, FakeLifecycle Embedded) Make()
    {
        var voxel = new FakeLifecycle("voxel", _log);
        var embedded = new FakeLifecycle("embedded", _log);
        var r = new ToolsetReconciler(
            new[] { "basics", "voxel", "embedded" }, new IToolsetLifecycle[] { voxel, embedded }, _gate, _board, _clock);
        return (r, voxel, embedded);
    }

    [Fact]
    public async Task OffToOn_StartsThenOpensGate()
    {
        var (r, _, _) = Make();
        var result = await r.ReconcileAsync(new[] { "basics", "voxel" });
        Assert.Equal(new[] { "basics", "voxel" }, result.Started);
        Assert.True(_gate.IsOn("voxel"));
        Assert.True(_gate.IsOn("basics"));
        Assert.False(_gate.IsOn("embedded"));
        Assert.Equal(new[] { "start:voxel" }, _log); // Basics has no lifecycle: gate only
        Assert.Equal(ToolsetStatus.On, _board.Get("voxel").Status);
    }

    [Fact]
    public async Task OnToOff_ClosesGateAndStops()
    {
        var (r, _, _) = Make();
        await r.ReconcileAsync(new[] { "voxel" });
        var result = await r.ReconcileAsync(Array.Empty<string>());
        Assert.Equal(new[] { "voxel" }, result.Stopped);
        Assert.False(_gate.IsOn("voxel"));
        Assert.Equal(new[] { "start:voxel", "stop:voxel" }, _log);
        Assert.Equal(ToolsetStatus.Off, _board.Get("voxel").Status);
    }

    [Fact]
    public async Task NoOp_SecondRunMakesNoLifecycleCalls()
    {
        var (r, _, _) = Make();
        await r.ReconcileAsync(new[] { "basics", "voxel" });
        _log.Clear();
        var second = await r.ReconcileAsync(new[] { "basics", "voxel" });
        Assert.True(second.IsNoOp);
        Assert.Empty(_log);
        Assert.Equal(new[] { "basics", "voxel" }, _gate.Snapshot());
    }

    [Fact]
    public async Task StartFailure_IsReportedAndOthersContinue()
    {
        var (r, voxel, _) = Make();
        voxel.FailStart = new InvalidOperationException("port 8090 in use");
        var result = await r.ReconcileAsync(new[] { "basics", "voxel", "embedded" });

        Assert.Equal("port 8090 in use", result.Failed["voxel"]);
        Assert.Equal(new[] { "basics", "embedded" }, result.Started);
        Assert.False(_gate.IsOn("voxel"));
        var state = _board.Get("voxel");
        Assert.Equal(ToolsetStatus.Failed, state.Status);
        Assert.Equal("port 8090 in use", state.Reason);
        Assert.Equal(_clock.GetUtcNow(), state.AtUtc);
    }

    [Fact]
    public async Task FailedToolset_RetriedOnNextReconcile()
    {
        var (r, voxel, _) = Make();
        voxel.FailStart = new InvalidOperationException("boom");
        await r.ReconcileAsync(new[] { "voxel" });
        voxel.FailStart = null;
        var result = await r.ReconcileAsync(new[] { "voxel" });
        Assert.Equal(new[] { "voxel" }, result.Started);
        Assert.True(_gate.IsOn("voxel"));
    }

    [Fact]
    public async Task FailedToolset_NoLongerDesired_ClearsToOffWithoutStop()
    {
        var (r, voxel, _) = Make();
        voxel.FailStart = new InvalidOperationException("boom");
        await r.ReconcileAsync(new[] { "voxel" });
        _log.Clear();
        await r.ReconcileAsync(Array.Empty<string>());
        Assert.Empty(_log);
        Assert.Equal(ToolsetStatus.Off, _board.Get("voxel").Status);
    }

    [Fact]
    public async Task StopFailure_KeepsGateClosedAndReportsReason()
    {
        var (r, voxel, _) = Make();
        await r.ReconcileAsync(new[] { "voxel" });
        voxel.FailStop = new InvalidOperationException("stuck");
        var result = await r.ReconcileAsync(Array.Empty<string>());
        Assert.Equal("stuck", result.Failed["voxel"]);
        Assert.False(_gate.IsOn("voxel"));
        Assert.Equal(ToolsetStatus.Failed, _board.Get("voxel").Status);
    }

    [Fact]
    public async Task FailedStop_IsRetriedOnNextReconcile_EvenWhenNoLongerDesired()
    {
        var (r, voxel, _) = Make();
        await r.ReconcileAsync(new[] { "voxel" });
        voxel.FailStop = new InvalidOperationException("stuck");
        await r.ReconcileAsync(Array.Empty<string>());
        voxel.FailStop = null;
        _log.Clear();

        var result = await r.ReconcileAsync(Array.Empty<string>());

        Assert.Equal(new[] { "stop:voxel" }, _log);
        Assert.Equal(new[] { "voxel" }, result.Stopped);
        Assert.Equal(ToolsetStatus.Off, _board.Get("voxel").Status);
    }

    [Fact]
    public async Task FailedStop_ThenDesiredAgain_StopsBeforeStarting()
    {
        var (r, voxel, _) = Make();
        await r.ReconcileAsync(new[] { "voxel" });
        voxel.FailStop = new InvalidOperationException("stuck");
        await r.ReconcileAsync(Array.Empty<string>());
        voxel.FailStop = null;
        _log.Clear();

        await r.ReconcileAsync(new[] { "voxel" });

        Assert.Equal(new[] { "stop:voxel", "start:voxel" }, _log);
        Assert.True(_gate.IsOn("voxel"));
    }

    [Fact]
    public async Task CancelledWaiter_DoesNotDiscardAnOlderRequest()
    {
        var (r, voxel, _) = Make();
        var release = new TaskCompletionSource();
        voxel.StartBlocker = release.Task;

        var first = r.ReconcileAsync(new[] { "voxel" });
        while (!_log.Contains("start:voxel")) await Task.Yield();
        var second = r.ReconcileAsync(new[] { "voxel", "basics" });
        using var cts = new CancellationTokenSource();
        var third = r.ReconcileAsync(new[] { "basics" }, cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => third);
        release.SetResult();

        await first;
        var secondResult = await second;
        Assert.False(secondResult.Superseded);
        Assert.Equal(new[] { "basics", "voxel" }, _gate.Snapshot());
    }

    [Fact]
    public async Task UnknownDesiredId_IsReportedFailed()
    {
        var (r, _, _) = Make();
        var result = await r.ReconcileAsync(new[] { "bogus" });
        Assert.Contains("bogus", result.Failed.Keys);
    }

    [Fact]
    public async Task RapidChanges_LastDesiredWins_NoOverlap()
    {
        var (r, voxel, _) = Make();
        var release = new TaskCompletionSource();
        voxel.StartBlocker = release.Task;

        var first = r.ReconcileAsync(new[] { "voxel" });
        // Give the first run time to take the lock and block inside Start.
        while (!_log.Contains("start:voxel")) await Task.Yield();
        var second = r.ReconcileAsync(new[] { "voxel", "basics" });
        var third = r.ReconcileAsync(new[] { "basics" });
        release.SetResult();

        var results = await Task.WhenAll(first, second, third);
        Assert.True(results[1].Superseded);
        Assert.False(results[2].Superseded);
        Assert.Equal(1, voxel.MaxActive);
        Assert.Equal(new[] { "basics" }, _gate.Snapshot());
        Assert.Equal(new[] { "start:voxel", "stop:voxel" }, _log);
    }

    [Fact]
    public async Task Cancellation_LeavesToolsetOff()
    {
        var (r, _, _) = Make();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => r.ReconcileAsync(new[] { "voxel" }, cts.Token));
        Assert.False(_gate.IsOn("voxel"));
    }
}
