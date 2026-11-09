using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ToolBox.Core;
using ToolBox.Voxel;

namespace ToolBox.Host.Tests;

/// <summary>
/// Proves a disabled toolset is INVISIBLE and NOT RUNNING (issue #32): every toolset is
/// registered, but the reconciler only starts and opens the enabled ones. Tests start the app
/// on ephemeral ports, then inspect the gate, the viewer and tools/list.
/// </summary>
[Collection(VoxelViewerCollection.Name)]
public class ToolsetSelectionTests
{
    private static WebApplication Build(string? toolsets)
    {
        var settings = new Dictionary<string, string?> { ["Ports:Viewer:Port"] = "0" };
        if (toolsets is not null) settings[ToolBoxCatalog.EnvKey] = toolsets;
        return ToolBoxHttpApp.Build([], overrideUrl: "http://127.0.0.1:0", settings: settings);
    }

    private static async Task<WebApplication> StartAsync(string? toolsets)
    {
        var app = Build(toolsets);
        await app.StartAsync();
        return app;
    }

    private static string[] OpenToolsets(WebApplication app) =>
        app.Services.GetRequiredService<ToolBox.Core.Toolsets.ToolsetGate>().Snapshot()
            .Select(s => s.ToLowerInvariant()).Order().ToArray();

    private static bool ViewerRunning(WebApplication app) =>
        app.Services.GetRequiredService<VoxelViewerBroadcastService>().Port is not null;

    [Fact]
    public async Task Default_IsBasicsAndVoxel()
    {
        await using var app = await StartAsync(null);

        Assert.Equal(new[] { "basics", "voxel" }, OpenToolsets(app));
        Assert.True(ViewerRunning(app));
        await app.StopAsync();
    }

    [Fact]
    public async Task HostedServices_ForOffToolset_NeverRun()
    {
        await using var app = await StartAsync("basics");

        Assert.Equal(new[] { "basics" }, OpenToolsets(app));
        Assert.False(ViewerRunning(app));
        Assert.DoesNotContain("viewer",
            app.Services.GetRequiredService<ToolBox.Core.Health.ComponentStatusRegistry>().Snapshot().Keys);
        await app.StopAsync();
    }

    [Fact]
    public async Task VoxelEnabled_ViewerRuns()
    {
        await using var app = await StartAsync("basics, voxel");

        Assert.True(ViewerRunning(app));
        await app.StopAsync();
    }

    [Fact]
    public async Task EmbeddedEnabled_AppearsAlongsideOthers()
    {
        await using var app = await StartAsync("basics,embedded");

        Assert.Equal(new[] { "basics", "embedded" }, OpenToolsets(app));
        Assert.False(ViewerRunning(app));
        await app.StopAsync();
    }

    [Fact]
    public void ComposeAll_RejectsToolsetThatRegistersRawHostedService()
    {
        var catalog = new ToolsetCatalog([
            new ToolsetCatalogEntry("a", "A", "a",
                s => s.AddSingleton<IHostedService, NoopHosted>())]);

        var ex = Assert.Throws<InvalidOperationException>(() => catalog.ComposeAll(new ServiceCollection()));

        Assert.Contains("IHostedService", ex.Message);
        Assert.Contains("'a'", ex.Message);
    }

    [Fact]
    public void ProductionCatalog_RegistersNoRawHostedService()
    {
        var added = ToolBoxCatalog.Create().ComposeAll(new ServiceCollection());

        Assert.Equal(3, added.Count);
    }

    private sealed class NoopHosted : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task EmptyValue_EnablesNothing()
    {
        await using var app = await StartAsync("");

        Assert.Empty(OpenToolsets(app));
        await app.StopAsync();
    }

    [Fact]
    public void UnknownId_ThrowsListingValidIds()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build("basics,nope"));

        Assert.Contains("nope", ex.Message);
        Assert.Contains("basics", ex.Message);
        Assert.Contains("voxel", ex.Message);
        Assert.Contains("embedded", ex.Message);
    }

    [Fact]
    public void ResolveEnabled_Precedence_DefaultThenAppsettingsThenCsv()
    {
        var none = new ConfigurationBuilder().Build();
        Assert.Equal(ToolBoxCatalog.DefaultEnabled, ToolBoxCatalog.ResolveEnabled(none));

        var json = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Toolsets:Enabled:0"] = "basics" })
            .Build();
        Assert.Equal(new[] { "basics" }, ToolBoxCatalog.ResolveEnabled(json));

        var overridden = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Toolsets:Enabled:0"] = "basics",
                [ToolBoxCatalog.EnvKey] = "voxel",
            })
            .Build();
        Assert.Equal(new[] { "voxel" }, ToolBoxCatalog.ResolveEnabled(overridden));
    }

    [Fact]
    public async Task BasicsOnly_ToolsListHasNoVoxelTools_AndHealthListsOnlyBasics()
    {
        // Voxel is off, so starting this app does not bind a viewer port.
        await using var app = await StartAsync("basics");
        string baseUrl = app.Urls.First().TrimEnd('/');

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{baseUrl}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        });
        await using McpClient client = await McpClient.CreateAsync(transport);
        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToArray();

        Assert.Contains("ping", names);
        Assert.DoesNotContain("place_block", names);
        Assert.DoesNotContain("clear", names);

        using var http = new HttpClient();
        string health = await http.GetStringAsync($"{baseUrl}/health");
        Assert.Contains("Basics", health);
        Assert.DoesNotContain("Voxel", health);

        await app.StopAsync();
    }
}
