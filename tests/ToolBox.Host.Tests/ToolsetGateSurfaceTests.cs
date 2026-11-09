using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ToolBox.Core;
using ToolBox.Core.Toolsets;

namespace ToolBox.Host.Tests;

/// <summary>
/// One test per gate surface (issue #32 step 5): tools/list, tools/call, server info.
/// Runs against the real production composition; the gate is flipped directly (Core exposes
/// Open/Close to Host.Tests only) because the reconciler is not wired in until step 6.
/// </summary>
[Collection(VoxelViewerCollection.Name)]
public class ToolsetGateSurfaceTests
{
    private static async Task<WebApplication> StartAsync()
    {
        var app = ToolBoxHttpApp.Build([], overrideUrl: "http://127.0.0.1:0",
            settings: new Dictionary<string, string?> { ["Ports:Viewer:Port"] = "0" });
        await app.StartAsync();
        return app;
    }

    private static Task<McpClient> ConnectAsync(WebApplication app) =>
        McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{app.Urls.First().TrimEnd('/')}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }));

    private static string Text(CallToolResult r) =>
        string.Concat(r.Content.OfType<TextContentBlock>().Select(b => b.Text));

    [Fact]
    public async Task ToolsList_HidesClosedToolset()
    {
        await using var app = await StartAsync();
        await using var client = await ConnectAsync(app);
        var gate = app.Services.GetRequiredService<ToolsetGate>();

        var before = (await client.ListToolsAsync()).Select(t => t.Name).ToArray();
        Assert.Contains("ping", before);
        int withVoxel = before.Length;

        gate.Close("voxel");
        var after = (await client.ListToolsAsync()).Select(t => t.Name).ToArray();
        Assert.Contains("ping", after);
        Assert.True(after.Length < withVoxel, "closing voxel should hide its tools");

        gate.Open("voxel");
        Assert.Equal(withVoxel, (await client.ListToolsAsync()).Count);
    }

    [Fact]
    public async Task ToolsCall_ClosedToolset_ReturnsToolsetIsOff()
    {
        await using var app = await StartAsync();
        await using var client = await ConnectAsync(app);
        var gate = app.Services.GetRequiredService<ToolsetGate>();

        gate.Close("basics");
        var off = await client.CallToolAsync("ping");
        Assert.True(off.IsError);
        Assert.Equal("toolset 'basics' is off", Text(off));

        gate.Open("basics");
        var on = await client.CallToolAsync("ping");
        Assert.NotEqual(true, on.IsError);
    }

    [Fact]
    public async Task ServerInfo_ListsOnlyOpenToolsets()
    {
        await using var app = await StartAsync();
        var gate = app.Services.GetRequiredService<ToolsetGate>();
        var info = app.Services.GetRequiredService<ServerInfoProvider>();

        Assert.Equal(["Basics", "Voxel"], info.Get().Toolsets.Order().ToArray());
        gate.Close("voxel");
        Assert.Equal(["Basics"], info.Get().Toolsets.ToArray());
    }
}
