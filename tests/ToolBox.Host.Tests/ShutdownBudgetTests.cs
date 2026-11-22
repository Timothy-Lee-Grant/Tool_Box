using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ToolBox.Core.Desired;
using ToolBox.Core.Health;
using ToolBox.Core.Toolsets;

namespace ToolBox.Host.Tests;

/// <summary>
/// Issue #102 (gap from #38): shutdown timing. The reconciler gives toolsets 10 s to stop on its own token;
/// the host must allow longer than that, or it would abandon the stop mid-way.
/// </summary>
public sealed class ShutdownBudgetTests : IDisposable
{
    private static readonly TimeSpan Safety = TimeSpan.FromSeconds(30);

    private readonly string _dir = Directory.CreateTempSubdirectory("toolbox-shutdown-").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    /// <summary>Starts instantly; its Stop waits until the test releases it.</summary>
    private sealed class GatedLifecycle(string id) : IToolsetLifecycle
    {
        public readonly TaskCompletionSource StopEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public volatile bool Stopped;
        public string ToolsetId => id;
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public async Task StopAsync(CancellationToken ct)
        {
            StopEntered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            Stopped = true;
        }
    }

    [Fact]
    public async Task A_slow_toolset_stop_completes_when_the_host_token_is_already_cancelled()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { [ToolBoxCatalog.EnvKey] = "voxel" }).Build();
        var store = new DesiredStateStore(Path.Combine(_dir, "desired-state.json"));
        string[] known = ["basics", "voxel", "embedded"];
        var resolution = EnabledResolution.Resolve(config, store.Load().State, known);
        var gate = new ToolsetGate();
        var slow = new GatedLifecycle("voxel");
        var reconciler = new ToolsetReconciler(known, [slow], gate, new ToolsetStatusBoard(), TimeProvider.System);
        var roster = new DesiredRosterSource(store, config, known, new ComponentStatusRegistry(), resolution);
        using var service = new ToolsetReconcilerService(
            reconciler, gate, new NullToolListChangedNotifier(), roster, resolution,
            NullLogger<ToolsetReconcilerService>.Instance, TimeProvider.System, watchFile: false);

        await service.StartAsync(CancellationToken.None);
        Assert.True(gate.IsOn("voxel"));

        // Host token already cancelled, as it can be late in shutdown: the stop must use its own token.
        using var hostToken = new CancellationTokenSource();
        await hostToken.CancelAsync();
        var stopping = service.StopAsync(hostToken.Token);

        await slow.StopEntered.Task.WaitAsync(Safety);
        Assert.False(stopping.IsCompleted); // still waiting on the slow toolset
        slow.Release.SetResult();
        await stopping.WaitAsync(Safety);

        Assert.True(slow.Stopped);
        Assert.False(gate.IsOn("voxel"));
    }

    [Fact]
    public async Task Host_shutdown_budget_exceeds_the_reconciler_stop_timeout_with_headroom()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Ports:Viewer:Port"] = "0",
            ["Ports:Admin:Port"] = "0",
            ["Admin:TokenPath"] = Path.Combine(_dir, "admin-token"),
            ["Desired:Path"] = Path.Combine(_dir, "desired-state.json"),
        };
        await using var app = ToolBoxHttpApp.Build([], overrideUrl: "http://127.0.0.1:0", settings: settings);

        var budget = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<HostOptions>>().Value.ShutdownTimeout;

        Assert.True(budget >= ToolsetReconcilerService.StopTimeout + TimeSpan.FromSeconds(5),
            $"ShutdownTimeout {budget} must exceed the reconciler's {ToolsetReconcilerService.StopTimeout} stop timeout by at least 5 s.");
    }
}
