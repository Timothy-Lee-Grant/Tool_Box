using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ModelContextProtocol.Client;

namespace ToolBox.E2E.Tests;

/// <summary>
/// The contract the GUI relies on (#38, ADR-024), checked against the real Host as a child process:
/// host.json for discovery, the token file for auth, the admin API to toggle a toolset, a real MCP client to
/// observe it, and POST /shutdown for a clean exit. One host, one scenario, so the steps read in order.
/// </summary>
[Trait("Category", "E2E")]
public sealed class BackgroundHostContractTests
{
    private static readonly TimeSpan Ready = TimeSpan.FromSeconds(60);

    [E2EFact]
    public async Task BackgroundHost_FullLifecycle_ThroughTheGuiContract()
    {
        await using HostProcess host = HostProcess.Start();
        string token = await host.WaitUntilReadyAsync(Ready);
        using var http = new HttpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        try
        {
            // Discovery: host.json reports real (non-zero) ports.
            Assert.NotEqual(0, new Uri(host.McpUrl).Port);
            Assert.NotEqual(0, new Uri(host.AdminUrl).Port);

            // The admin API refuses a caller without the token.
            using (var anon = await http.GetAsync($"{host.AdminUrl}/api/v1/toolsets", cts.Token))
                Assert.Equal(HttpStatusCode.Unauthorized, anon.StatusCode);

            // Observe the roster through a real MCP client (stateless HTTP: each list is a fresh pull, ADR-022).
            string[] before = await ListToolNamesAsync(host.McpUrl, cts.Token);
            Assert.Contains("ping", before); // positive control: basics is on by default

            await SetEnabledAsync(http, host, token, "basics", false, cts.Token);
            string[] off = await WaitForToolsAsync(host.McpUrl, names => !names.Contains("ping"), cts.Token);
            Assert.DoesNotContain("ping", off);

            await SetEnabledAsync(http, host, token, "basics", true, cts.Token);
            string[] on = await WaitForToolsAsync(host.McpUrl, names => names.Contains("ping"), cts.Token);
            Assert.Contains("ping", on);

            // A second launch on the same instance dir is refused with the dedicated exit code.
            await using (HostProcess second = HostProcess.Start(host.Dir))
            {
                Assert.Equal(4, await second.WaitForExitAsync(TimeSpan.FromSeconds(60)));
            }
            Assert.True(File.Exists(host.HostJsonPath), "a refused second launch must not delete the first host's host.json");

            // Graceful shutdown: 202, exit code 0, host.json removed.
            using var shutdown = new HttpRequestMessage(HttpMethod.Post, $"{host.AdminUrl}/api/v1/shutdown");
            shutdown.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using (var resp = await http.SendAsync(shutdown, cts.Token))
                Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);

            Assert.Equal(0, await host.WaitForExitAsync(TimeSpan.FromSeconds(30)));
            Assert.False(File.Exists(host.HostJsonPath), "host.json must be removed on a clean shutdown");
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
            throw new InvalidOperationException($"background host contract failed; host output:\n{host.Output}", ex);
        }
    }

    private static async Task SetEnabledAsync(HttpClient http, HostProcess host, string token, string id, bool enabled, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, $"{host.AdminUrl}/api/v1/toolsets/{id}/enabled")
        {
            Content = JsonContent.Create(new { enabled }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await http.SendAsync(req, ct);
        // 202: the intent is durable and a reconcile is queued (the tool list converges shortly after).
        Assert.True(resp.IsSuccessStatusCode, $"PUT enabled={enabled} returned {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync(ct)}");
    }

    private static async Task<string[]> ListToolNamesAsync(string mcpUrl, CancellationToken ct)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(mcpUrl) });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: ct);
        return (await client.ListToolsAsync(cancellationToken: ct)).Select(t => t.Name).ToArray();
    }

    /// <summary>The write returns before the reconciler has applied it, so poll with a bound; never a fixed sleep.</summary>
    private static async Task<string[]> WaitForToolsAsync(string mcpUrl, Func<string[], bool> done, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(30));
        string[] names = [];
        try
        {
            while (true)
            {
                names = await ListToolNamesAsync(mcpUrl, limit.Token);
                if (done(names)) return names;
                await Task.Delay(200, limit.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return names; // the caller's assertion reports the last observed list
        }
    }
}
