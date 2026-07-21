using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ToolBox.Host.Tests;

/// <summary>
/// The test pyramid's middle layer (plan 002, Step 3): unit tests proved the tool
/// METHODS; these prove the WIRE — the SDK's own client, over real streamable HTTP,
/// against the production app composition. If tool schemas, serialization, or the
/// endpoint mapping break, it breaks here, not in a user's Claude session.
///
/// API-name risk flag (plan 002 risk table): the client transport type and options
/// below follow the SDK's documented pattern for 1.4.x. If names moved, the compile
/// error lands exactly here — fix against the package's XML docs, record in Stage 4.
/// </summary>
public class HttpTransportTests(HttpServerFixture fixture) : IClassFixture<HttpServerFixture>
{
    // API verified against the SDK's v1.4 transport docs (after two wrong guesses —
    // see plan 002 Stage 4, 2026-07-16): HttpClientTransport + McpClient.CreateAsync.
    private async Task<McpClient> ConnectAsync()
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{fixture.BaseUrl}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        });

        return await McpClient.CreateAsync(transport);
    }

    private static string AllText(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    [Fact]
    public async Task Handshake_Succeeds()
    {
        // McpClient.CreateAsync performs the full initialize exchange;
        // reaching this line means protocol version + capabilities negotiated.
        await using McpClient client = await ConnectAsync();
        Assert.NotNull(client);
    }

    [Fact]
    public async Task ToolsList_ExposesExactlyTheCatalog()
    {
        await using McpClient client = await ConnectAsync();

        var tools = await client.ListToolsAsync();

        // Set-equality, not Contains: a tool DISAPPEARING or an unexpected tool
        // APPEARING are both contract breaks worth failing on.
        Assert.Equal(
            new[]
            {
                "clear", "current_time", "describe_world", "list_materials", "mirror",
                "ping", "place_block", "place_box", "place_cone", "place_cylinder",
                "place_sphere", "place_tube", "remove_box", "server_info", "world_info",
            },
            tools.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Ping_RoundTripsThroughTheRealWire()
    {
        await using McpClient client = await ConnectAsync();

        CallToolResult result = await client.CallToolAsync(
            "ping",
            new Dictionary<string, object?> { ["message"] = "integration" });

        Assert.Contains("pong: integration", AllText(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerInfo_ReportsTheBasicsToolset()
    {
        await using McpClient client = await ConnectAsync();

        CallToolResult result = await client.CallToolAsync("server_info");

        Assert.Contains("Basics", AllText(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_IsCurlShaped()
    {
        // Deliberately a plain HttpClient, no MCP anywhere: this endpoint exists for
        // probes that have never heard of the protocol (compose healthchecks).
        using var http = new HttpClient();

        HttpResponseMessage response = await http.GetAsync($"{fixture.BaseUrl}/health");

        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", body, StringComparison.Ordinal);
        Assert.Contains("Basics", body, StringComparison.Ordinal);
    }
}
