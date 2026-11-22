using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ToolBox.Host.Tests;

/// <summary>
/// Issue #101: the viewer's fail-soft behaviour proven end to end. A real host (real Kestrel,
/// real sockets) starts while the viewer port is held by someone else. MCP must still answer,
/// /health must say "degraded", and toggling Voxel off and on must retry the bind once the
/// port is free. The lifecycle-level cases live in VoxelLifecycleTests and PortConflictTests.
/// </summary>
[Collection(VoxelViewerCollection.Name)]
public sealed class ViewerFailSoftEndToEndTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("toolbox-failsoft-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static async Task<JsonElement> GetHealthAsync(HttpClient http, string baseUrl)
    {
        HttpResponseMessage response = await http.GetAsync($"{baseUrl}/health");
        response.EnsureSuccessStatusCode(); // degraded is still HTTP 200
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<string?> ViewerComponentAsync(HttpClient http, string baseUrl)
    {
        JsonElement health = await GetHealthAsync(http, baseUrl);
        return health.GetProperty("components").TryGetProperty("viewer", out JsonElement v) ? v.GetString() : null;
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "condition not met in time");
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task ViewerPortTaken_McpStillAnswers_HealthDegraded_ToggleRetriesTheBind()
    {
        var holder = new TcpListener(IPAddress.Loopback, 0);
        holder.Start();
        int taken = ((IPEndPoint)holder.LocalEndpoint).Port;
        bool holderStopped = false;

        string tokenPath = Path.Combine(_dir, "admin-token");
        var app = ToolBoxHttpApp.Build([], overrideUrl: "http://127.0.0.1:0", settings: new Dictionary<string, string?>
        {
            ["Ports:Viewer:Port"] = taken.ToString(),
            ["Ports:Admin:Port"] = "0",
            ["Admin:TokenPath"] = tokenPath,
            ["Desired:Path"] = Path.Combine(_dir, "state", "desired-state.json"),
        });
        await using var _ = app;
        using var http = new HttpClient();

        try
        {
            await app.StartAsync(); // must not throw: the viewer is optional
            string mcpUrl = app.Urls.First().TrimEnd('/');
            string adminUrl = app.Urls.Last().TrimEnd('/');
            string token = File.ReadAllText(tokenPath).Trim();

            // 1. Viewer reported unavailable, host degraded (the status is set shortly after StartAsync).
            await EventuallyAsync(async () => await ViewerComponentAsync(http, mcpUrl) is not null);
            JsonElement health = await GetHealthAsync(http, mcpUrl);
            Assert.Equal("degraded", health.GetProperty("status").GetString());
            Assert.Equal($"unavailable: port {taken} in use",
                health.GetProperty("components").GetProperty("viewer").GetString());

            // 2. MCP tools still work over the real wire.
            await using (McpClient client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri($"{mcpUrl}/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            })))
            {
                CallToolResult result = await client.CallToolAsync(
                    "ping", new Dictionary<string, object?> { ["message"] = "failsoft" });
                string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
                Assert.Contains("pong: failsoft", text, StringComparison.Ordinal);
            }

            // 3. Free the port, then toggle Voxel off and on: the bind is attempted again.
            holder.Stop();
            holderStopped = true;

            async Task SetVoxelAsync(bool enabled)
            {
                var request = new HttpRequestMessage(HttpMethod.Put, $"{adminUrl}/api/v1/toolsets/voxel/enabled")
                {
                    Content = new StringContent($$"""{"enabled":{{(enabled ? "true" : "false")}}}""", Encoding.UTF8, "application/json"),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                HttpResponseMessage response = await http.SendAsync(request);
                Assert.True(response.IsSuccessStatusCode, $"toggle failed: {(int)response.StatusCode}");
            }

            await SetVoxelAsync(false);
            await EventuallyAsync(async () => await ViewerComponentAsync(http, mcpUrl) is null);

            await SetVoxelAsync(true);
            await EventuallyAsync(async () => await ViewerComponentAsync(http, mcpUrl) == "ok");
            Assert.Equal("ok", (await GetHealthAsync(http, mcpUrl)).GetProperty("status").GetString());
        }
        finally
        {
            if (!holderStopped) holder.Stop();
            await app.StopAsync();
        }
    }
}
