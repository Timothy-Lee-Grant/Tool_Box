using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ToolBox.Core.Desired;
using ToolBox.Core.Health;
using ToolBox.Core.Toolsets;

namespace ToolBox.Host.Tests;

/// <summary>Step 7 of #32: file-change and manual triggers, debounce on a fake clock, live toggling.</summary>
[Collection(VoxelViewerCollection.Name)]
public sealed class ReconcileTriggerTests : IDisposable
{
    /// <summary>A clock whose timers fire only when the test calls <see cref="Advance"/>.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private sealed class FakeTimer(ManualClock owner, TimerCallback callback, object? state, TimeSpan due) : ITimer
        {
            public TimeSpan Due { get; set; } = due;
            public bool Disposed { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period) { Due = dueTime; return true; }
            public void Fire() => callback(state);
            public void Dispose() { Disposed = true; owner._timers.Remove(this); }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }

        private readonly List<FakeTimer> _timers = [];
        private TimeSpan _now = TimeSpan.Zero;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var t = new FakeTimer(this, callback, state, _now + dueTime);
            lock (_timers) { _timers.Add(t); }
            return t;
        }

        public int PendingTimers { get { lock (_timers) { return _timers.Count; } } }

        public void Advance(TimeSpan by)
        {
            FakeTimer[] due;
            lock (_timers)
            {
                _now += by;
                due = _timers.Where(t => t.Due <= _now).ToArray();
            }
            foreach (var t in due) { t.Dispose(); t.Fire(); }
        }
    }

    private sealed class CountingLifecycle(string id) : IToolsetLifecycle
    {
        public int Starts;
        public int Stops;
        public string ToolsetId => id;
        public Task StartAsync(CancellationToken ct) { Interlocked.Increment(ref Starts); return Task.CompletedTask; }
        public Task StopAsync(CancellationToken ct) { Interlocked.Increment(ref Stops); return Task.CompletedTask; }
    }

    private readonly string _dir = Directory.CreateTempSubdirectory("toolbox-trigger-").FullName;
    private string FilePath => Path.Combine(_dir, "desired-state.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static IConfiguration Config(string? pin = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            pin is null ? new Dictionary<string, string?>() : new Dictionary<string, string?> { [ToolBoxCatalog.EnvKey] = pin }).Build();

    private static string VoxelFile(bool on) =>
        """{"schemaVersion":1,"toolsets":{"voxel":{"enabled":""" + (on ? "true" : "false") + "}}}";

    private sealed record Rig(
        ToolsetReconcilerService Service, CountingLifecycle Voxel, ToolsetGate Gate,
        ComponentStatusRegistry Registry, ManualClock Clock,
        DesiredRosterSource Roster, ToolsetStatusBoard Board);

    private Rig MakeRig(string? pin = null, string? initialFile = null)
    {
        if (initialFile is not null) File.WriteAllText(FilePath, initialFile);
        var config = Config(pin);
        var registry = new ComponentStatusRegistry();
        var store = new DesiredStateStore(FilePath);
        string[] known = ["basics", "voxel", "embedded"];
        var resolution = EnabledResolution.Resolve(config, store.Load().State, known);
        var gate = new ToolsetGate();
        var board = new ToolsetStatusBoard();
        var voxel = new CountingLifecycle("voxel");
        var reconciler = new ToolsetReconciler(known, [voxel], gate, board, TimeProvider.System);
        var roster = new DesiredRosterSource(store, config, known, registry, resolution);
        var clock = new ManualClock();
        var service = new ToolsetReconcilerService(
            reconciler, roster, resolution, NullLogger<ToolsetReconcilerService>.Instance, clock,
            TimeSpan.FromMilliseconds(300), watchFile: false);
        return new Rig(service, voxel, gate, registry, clock, roster, board);
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
    public async Task RapidEdits_AreCoalescedIntoOneReconcile()
    {
        var rig = MakeRig(initialFile: VoxelFile(on: false));
        await rig.Service.StartAsync(default);
        Assert.False(rig.Gate.IsOn("voxel"));

        // Three edits inside one debounce window; the last one (on) must win with a single Start.
        File.WriteAllText(FilePath, VoxelFile(on: true));
        rig.Service.NotifyDesiredStateChanged();
        File.WriteAllText(FilePath, VoxelFile(on: false));
        rig.Service.NotifyDesiredStateChanged();
        File.WriteAllText(FilePath, VoxelFile(on: true));
        rig.Service.NotifyDesiredStateChanged();

        await Eventually(() => rig.Clock.PendingTimers == 1);
        Assert.Equal(0, rig.Voxel.Starts);
        Assert.False(rig.Gate.IsOn("voxel"));

        rig.Clock.Advance(TimeSpan.FromMilliseconds(300));
        await Eventually(() => rig.Gate.IsOn("voxel"));

        Assert.Equal(1, rig.Voxel.Starts);
        Assert.Equal(0, rig.Voxel.Stops);
        await rig.Service.StopAsync(default);
    }

    [Fact]
    public async Task ManualRequest_ReconcilesImmediately_WithoutWaitingForTheClock()
    {
        var rig = MakeRig(initialFile: VoxelFile(on: false));
        await rig.Service.StartAsync(default);

        File.WriteAllText(FilePath, VoxelFile(on: true));
        ((IToolsetReconciler)rig.Service).RequestReconcile();

        await Eventually(() => rig.Gate.IsOn("voxel"));
        Assert.Equal(0, rig.Clock.PendingTimers);
        await rig.Service.StopAsync(default);
    }

    [Fact]
    public async Task EnvPinnedToolset_IgnoresFileEdit()
    {
        var rig = MakeRig(pin: "basics", initialFile: VoxelFile(on: false));
        await rig.Service.StartAsync(default);
        bool Desired() => rig.Roster.Details(rig.Board)["voxel"].Desired;
        Assert.False(Desired());

        // Run 1: the file says on. Seeing the loop re-read it (Desired flips) proves the run happened.
        File.WriteAllText(FilePath, VoxelFile(on: true));
        ((IToolsetReconciler)rig.Service).RequestReconcile();
        await Eventually(Desired);

        // Run 2: back to off. The loop is serial, so once this flip is visible run 1 has fully finished.
        File.WriteAllText(FilePath, VoxelFile(on: false));
        ((IToolsetReconciler)rig.Service).RequestReconcile();
        await Eventually(() => !Desired());

        Assert.False(rig.Gate.IsOn("voxel"));
        Assert.Equal(0, rig.Voxel.Starts);
        Assert.False(rig.Roster.Details(rig.Board)["voxel"].Effective);
        await rig.Service.StopAsync(default);
    }

    [Fact]
    public async Task CorruptFile_KeepsPreviousState_AndReportsComponent()
    {
        var rig = MakeRig(initialFile: VoxelFile(on: true));
        await rig.Service.StartAsync(default);
        Assert.True(rig.Gate.IsOn("voxel"));

        File.WriteAllText(FilePath, "{ not json");
        ((IToolsetReconciler)rig.Service).RequestReconcile();

        await Eventually(() => rig.Registry.Snapshot().ContainsKey("desiredState"));
        Assert.StartsWith("corrupt", rig.Registry.Snapshot()["desiredState"]);
        Assert.True(rig.Gate.IsOn("voxel"));
        Assert.Equal(0, rig.Voxel.Stops);

        // Fixing the file clears the report.
        File.WriteAllText(FilePath, VoxelFile(on: true));
        ((IToolsetReconciler)rig.Service).RequestReconcile();
        await Eventually(() => !rig.Registry.Snapshot().ContainsKey("desiredState"));
        await rig.Service.StopAsync(default);
    }

    // ---- live, through the real app and a real file watcher ----

    [Fact]
    public async Task LiveReconcile_FileEditChangesToolsList()
    {
        File.WriteAllText(FilePath, VoxelFile(on: true));
        await using var app = ToolBoxHttpApp.Build([], overrideUrl: "http://127.0.0.1:0",
            settings: new Dictionary<string, string?> { ["Desired:Path"] = FilePath, ["Ports:Viewer:Port"] = "0" });
        await app.StartAsync();
        var url = new Uri($"{app.Urls.First().TrimEnd('/')}/mcp");

        async Task<string[]> ToolNames()
        {
            await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions { Endpoint = url }));
            return (await client.ListToolsAsync()).Select(t => t.Name).ToArray();
        }

        Assert.Contains("place_block", await ToolNames());

        // Atomic-style save: write a temp file, then rename over the target.
        var tmp = Path.Combine(_dir, ".tmp-desired");
        File.WriteAllText(tmp, VoxelFile(on: false));
        File.Move(tmp, FilePath, overwrite: true);

        // Deliberate exception to the fake-clock rule: this test exercises the real FileSystemWatcher,
        // the real debounce and the real clock. The loop exits at the first success; the deadline only
        // bounds the failure case, so it is generous for slow CI runners.
        var timeout = TimeSpan.FromSeconds(60);
        var deadline = DateTime.UtcNow + timeout;
        while ((await ToolNames()).Contains("place_block"))
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"voxel tools still listed {timeout.TotalSeconds}s after the file turned voxel off");
            await Task.Delay(100);
        }

        await app.StopAsync();
    }
}
