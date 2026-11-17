using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ToolBox.Core.Desired;
using ToolBox.Core.Health;
using ToolBox.Core.Toolsets;

namespace ToolBox.Host.Tests;

/// <summary>
/// #34 step 3: the reconciler service notifies once per pass, and only when the gate snapshot
/// (the set of open toolsets) differs before vs after. No sleeps: we await a count, and prove
/// "nothing was sent" by following the quiet pass with a real change and checking the total.
/// </summary>
public sealed class ReconcileNotificationTests : IDisposable
{
    private sealed class CountingNotifier : IToolListChangedNotifier
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public ValueTask NotifyAsync(CancellationToken ct) { Interlocked.Increment(ref _count); return ValueTask.CompletedTask; }
    }

    private sealed class FakeLifecycle(string id, bool failToStart = false) : IToolsetLifecycle
    {
        public string ToolsetId => id;
        public Task StartAsync(CancellationToken ct) =>
            failToStart ? Task.FromException(new InvalidOperationException("boom")) : Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class ThrowingNotifier : IToolListChangedNotifier
    {
        public int Calls;
        public ValueTask NotifyAsync(CancellationToken ct) { Interlocked.Increment(ref Calls); throw new InvalidOperationException("send failed"); }
    }

    private readonly string _dir = Directory.CreateTempSubdirectory("toolbox-notify-").FullName;
    private string FilePath => Path.Combine(_dir, "desired-state.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string File_(bool voxel, bool embedded = false) =>
        """{"schemaVersion":1,"toolsets":{"voxel":{"enabled":""" + (voxel ? "true" : "false") +
        """},"embedded":{"enabled":""" + (embedded ? "true" : "false") + "}}}";

    private sealed record Rig(ToolsetReconcilerService Service, ToolsetGate Gate, ReconcileTriggerTests.ManualClock Clock);

    private Rig MakeRig(IToolListChangedNotifier notifier, string initialFile, bool voxelFails = false)
    {
        File.WriteAllText(FilePath, initialFile);
        var config = new ConfigurationBuilder().Build();
        var store = new DesiredStateStore(FilePath);
        string[] known = ["basics", "voxel", "embedded"];
        var resolution = EnabledResolution.Resolve(config, store.Load().State, known);
        var gate = new ToolsetGate();
        var reconciler = new ToolsetReconciler(
            known, [new FakeLifecycle("voxel", voxelFails), new FakeLifecycle("embedded")],
            gate, new ToolsetStatusBoard(), TimeProvider.System);
        var roster = new DesiredRosterSource(store, config, known, new ComponentStatusRegistry(), resolution);
        var clock = new ReconcileTriggerTests.ManualClock();
        var service = new ToolsetReconcilerService(
            reconciler, gate, notifier, roster, resolution,
            NullLogger<ToolsetReconcilerService>.Instance, clock, TimeSpan.FromMilliseconds(300), watchFile: false);
        return new Rig(service, gate, clock);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met in time");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task EnablingAToolset_NotifiesOnce()
    {
        var n = new CountingNotifier();
        var rig = MakeRig(n, File_(voxel: false));
        await rig.Service.StartAsync(default);

        File.WriteAllText(FilePath, File_(voxel: true));
        rig.Service.RequestReconcile();

        await Eventually(() => n.Count == 1);
        Assert.True(rig.Gate.IsOn("voxel"));
        await rig.Service.StopAsync(default);
    }

    [Fact]
    public async Task ReconcileWithNoChange_DoesNotNotify()
    {
        var n = new CountingNotifier();
        var rig = MakeRig(n, File_(voxel: false));
        await rig.Service.StartAsync(default);

        rig.Service.RequestReconcile(); // nothing changed
        File.WriteAllText(FilePath, File_(voxel: true));
        rig.Service.RequestReconcile(); // sentinel: a real change after the quiet pass

        await Eventually(() => n.Count >= 1);
        Assert.Equal(1, n.Count); // the quiet pass added nothing
        await rig.Service.StopAsync(default);
    }

    [Fact]
    public async Task FailedStart_DoesNotNotify()
    {
        var n = new CountingNotifier();
        var rig = MakeRig(n, File_(voxel: false), voxelFails: true);
        await rig.Service.StartAsync(default);

        File.WriteAllText(FilePath, File_(voxel: true));
        rig.Service.RequestReconcile(); // voxel start throws, gate stays closed
        File.WriteAllText(FilePath, File_(voxel: true, embedded: true));
        rig.Service.RequestReconcile(); // sentinel: embedded really opens

        await Eventually(() => n.Count >= 1);
        Assert.Equal(1, n.Count);
        Assert.False(rig.Gate.IsOn("voxel"));
        Assert.True(rig.Gate.IsOn("embedded"));
        await rig.Service.StopAsync(default);
    }

    [Fact]
    public async Task StartAndStopInOnePass_NotifiesOnce()
    {
        var n = new CountingNotifier();
        var rig = MakeRig(n, File_(voxel: true));
        await rig.Service.StartAsync(default);

        File.WriteAllText(FilePath, File_(voxel: false, embedded: true));
        rig.Service.RequestReconcile();

        await Eventually(() => n.Count >= 1);
        Assert.Equal(1, n.Count);
        Assert.True(rig.Gate.IsOn("embedded"));
        Assert.False(rig.Gate.IsOn("voxel"));
        await rig.Service.StopAsync(default);
    }

    [Fact]
    public async Task DebouncedBurstOfEdits_NotifiesOnce()
    {
        var n = new CountingNotifier();
        var rig = MakeRig(n, File_(voxel: false));
        await rig.Service.StartAsync(default);

        File.WriteAllText(FilePath, File_(voxel: true));
        rig.Service.NotifyDesiredStateChanged();
        File.WriteAllText(FilePath, File_(voxel: false));
        rig.Service.NotifyDesiredStateChanged();
        File.WriteAllText(FilePath, File_(voxel: true));
        rig.Service.NotifyDesiredStateChanged();

        await Eventually(() => rig.Clock.PendingTimers == 1);
        Assert.Equal(0, n.Count);
        rig.Clock.Advance(TimeSpan.FromMilliseconds(300));

        await Eventually(() => n.Count >= 1);
        Assert.Equal(1, n.Count);
        await rig.Service.StopAsync(default);
    }

    [Fact]
    public async Task StartupAndShutdown_NeverNotify()
    {
        var n = new CountingNotifier();
        var rig = MakeRig(n, File_(voxel: true));

        await rig.Service.StartAsync(default);
        Assert.True(rig.Gate.IsOn("voxel"));
        await rig.Service.StopAsync(default);

        Assert.False(rig.Gate.IsOn("voxel"));
        Assert.Equal(0, n.Count);
    }

    [Fact]
    public async Task ANotifierThatThrows_DoesNotBreakTheLoop()
    {
        var n = new ThrowingNotifier();
        var rig = MakeRig(n, File_(voxel: false));
        await rig.Service.StartAsync(default);

        File.WriteAllText(FilePath, File_(voxel: true));
        rig.Service.RequestReconcile();
        await Eventually(() => n.Calls == 1);

        // The loop survived: a second change is still reconciled and notified.
        File.WriteAllText(FilePath, File_(voxel: false));
        rig.Service.RequestReconcile();
        await Eventually(() => n.Calls == 2);
        Assert.False(rig.Gate.IsOn("voxel"));
        await rig.Service.StopAsync(default);
    }
}
