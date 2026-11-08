using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ToolBox.Core;
using ToolBox.Voxel;

namespace ToolBox.Host.Tests;

/// <summary>
/// Proves a disabled toolset is ABSENT, not merely hidden: its tools are missing from
/// tools/list and its hosted service is not in the container. Most tests only Build()
/// the app (nothing starts, so no ports are used) and inspect the DI container.
/// </summary>
[Collection(VoxelViewerCollection.Name)]
public class ToolsetSelectionTests
{
    private static WebApplication Build(string? toolsets) =>
        ToolBoxHttpApp.Build(
            [],
            overrideUrl: "http://127.0.0.1:0",
            settings: toolsets is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { [ToolBoxCatalog.EnvKey] = toolsets });

    private static string[] ToolsetNames(WebApplication app) =>
        app.Services.GetServices<ToolsetDescriptor>()
            .Select(d => d.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    private static bool HasViewer(WebApplication app) =>
        app.Services.GetServices<IHostedService>().OfType<VoxelViewerBroadcastService>().Any();

    [Fact]
    public async Task Default_IsBasicsAndVoxel()
    {
        await using var app = Build(null);

        Assert.Equal(new[] { "Basics", "Voxel" }, ToolsetNames(app));
        Assert.True(HasViewer(app));
    }

    [Fact]
    public async Task VoxelDisabled_ViewerServiceIsNotRegistered()
    {
        await using var app = Build("basics");

        Assert.Equal(new[] { "Basics" }, ToolsetNames(app));
        Assert.False(HasViewer(app));
    }

    [Fact]
    public async Task VoxelEnabled_ViewerServiceIsRegistered()
    {
        await using var app = Build("basics, voxel");

        Assert.True(HasViewer(app));
    }

    [Fact]
    public async Task EmbeddedEnabled_AppearsAlongsideOthers()
    {
        await using var app = Build("basics,embedded");

        Assert.Equal(new[] { "Basics", "Embedded" }, ToolsetNames(app));
        Assert.False(HasViewer(app));
    }

    [Fact]
    public async Task EmptyValue_EnablesNothing()
    {
        await using var app = Build("");

        Assert.Empty(ToolsetNames(app));
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
        await using var app = Build("basics");
        await app.StartAsync();
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
