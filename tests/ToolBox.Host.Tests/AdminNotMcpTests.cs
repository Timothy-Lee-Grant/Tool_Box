using ModelContextProtocol.Client;

namespace ToolBox.Host.Tests;

/// <summary>
/// Epic #23 decision 7: agents cannot change their own settings. The admin API is plain HTTP on a
/// separate loopback listener, so it must never show up as an MCP tool on the data plane.
/// </summary>
[Collection(VoxelViewerCollection.Name)]
public class AdminNotMcpTests(HttpServerFixture fixture) : IClassFixture<HttpServerFixture>
{
    [Fact]
    public async Task Tools_list_contains_no_admin_tool()
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{fixture.BaseUrl}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        });
        await using McpClient client = await McpClient.CreateAsync(transport);

        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToList();

        Assert.NotEmpty(names);
        Assert.DoesNotContain(names, n =>
            n.Contains("admin", StringComparison.OrdinalIgnoreCase)
            || n.Contains("token", StringComparison.OrdinalIgnoreCase)
            || n.Contains("reconcile", StringComparison.OrdinalIgnoreCase)
            || n.Contains("rotate", StringComparison.OrdinalIgnoreCase));
    }
}
