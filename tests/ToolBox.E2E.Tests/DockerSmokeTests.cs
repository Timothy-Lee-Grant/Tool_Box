using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ModelContextProtocol.Client;

namespace ToolBox.E2E.Tests;

/// <summary>
/// The checks done by hand for #38, #66 and the v1.2.0 release, against the real image: healthy, the shipped
/// version, the DNS-rebinding guard (forged Host 421, foreign Origin 403, ADR-021) and a real MCP client.
/// With TOOLBOX_E2E=1 and no Docker this fails loudly; without the variable it is skipped.
/// </summary>
[Trait("Category", "E2E")]
public sealed class DockerSmokeTests
{
    [E2EFact]
    public async Task Container_IsHealthy_GuardsHold_AndServesTools()
    {
        await using ComposeStack stack = await ComposeStack.StartAsync();
        using var http = new HttpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        // /health: 200 and the version the build stamped, i.e. <Version> in Directory.Build.props.
        using (var health = await http.GetAsync($"{stack.McpBaseUrl}/health", cts.Token))
        {
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            JsonElement body = await health.Content.ReadFromJsonAsync<JsonElement>(cts.Token);
            string version = body.GetProperty("version").GetString() ?? "";
            // The host reports the assembly version, which the SDK pads to four parts ("1.2.0.0"). Compare as versions.
            Assert.Equal(Pad(ComposeStack.ExpectedVersion()), Pad(version.Split('+')[0]));
        }

        // Forged Host: 421 with an empty body, on /health and on /mcp.
        foreach (string path in new[] { "/health", "/mcp" })
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{stack.McpBaseUrl}{path}");
            req.Headers.Host = "evil.example";
            using var resp = await http.SendAsync(req, cts.Token);
            Assert.Equal((HttpStatusCode)421, resp.StatusCode);
            Assert.Equal("", await resp.Content.ReadAsStringAsync(cts.Token));
        }

        // Foreign Origin: 403, empty body.
        using (var req = new HttpRequestMessage(HttpMethod.Get, $"{stack.McpBaseUrl}/health"))
        {
            req.Headers.Add("Origin", "http://evil.example");
            using var resp = await http.SendAsync(req, cts.Token);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
            Assert.Equal("", await resp.Content.ReadAsStringAsync(cts.Token));
        }

        // A real MCP client lists tools through the published port.
        var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri($"{stack.McpBaseUrl}/mcp") });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: cts.Token);
        var tools = await client.ListToolsAsync(cancellationToken: cts.Token);
        Assert.Contains(tools, t => t.Name == "ping");
    }

    /// <summary>"1.2" and "1.2.0.0" are the same version; Version.Parse alone leaves unset parts at -1.</summary>
    private static Version Pad(string v)
    {
        var p = Version.Parse(v);
        return new Version(p.Major, Math.Max(p.Minor, 0), Math.Max(p.Build, 0), Math.Max(p.Revision, 0));
    }
}
