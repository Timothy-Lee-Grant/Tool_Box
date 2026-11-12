using System.ComponentModel;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ToolBox.Host.Tests;

/// <summary>
/// Issue #34 step 1 SPIKE (characterization, test only, no production change).
/// Proves what the pinned ModelContextProtocol 1.4.1 SDK does with
/// <c>notifications/tools/list_changed</c>:
///   1.1 stdio-style (long-lived session) CAN send it, and how we get hold of the live server;
///   1.2 stateless HTTP can NOT deliver it (and what the SDK does when asked);
///   1.3 what the initialize response advertises (tools.listChanged) on each transport;
///   1.4 if stateless HTTP advertises it, which knob turns it off.
/// Uses throwaway tools, not the production Host.
///
/// CHARACTERIZATION TESTS: these record what SDK 1.4.1 actually does (observed, not guessed;
/// run on next @ 35ac714, all passing). If an SDK 2.0 upgrade (#77) turns one red, that is the
/// behaviour that changed, e.g. stateless/subscription-based HTTP push. Observed on 1.4.1:
///   1.1 McpServer resolves from DI on stdio; SendNotificationAsync reaches the client.
///   1.2 Stateless HTTP delivers nothing; GET /mcp (SSE) returns 405.
///   1.3 stdio advertises tools.listChanged=true; stateless HTTP does NOT (the SDK fix is in 1.4.1).
///   1.4 An explicit Capabilities.Tools.ListChanged=false works, but is not needed.
/// </summary>
public class SdkListChangedSpikeTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    [McpServerToolType]
    private sealed class PingTools
    {
        [McpServerTool(Name = "spike_ping"), Description("ping")]
        public static string Ping() => "pong";
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // ---- stdio-style: in-process pipe pair, real SDK server hosted via DI -------------------

    private sealed class StdioRig : IAsyncDisposable
    {
        public required IHost Host { get; init; }
        public required McpClient Client { get; init; }
        public required TaskCompletionSource Notified { get; init; }
        public int Count;

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await Host.StopAsync();
            Host.Dispose();
        }
    }

    private static async Task<StdioRig> StartStdioRigAsync()
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services
            .AddMcpServer()
            .WithTools<PingTools>()
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());
        var host = builder.Build();
        await host.StartAsync();

        var notified = NewSignal();
        var client = await McpClient.CreateAsync(new StreamClientTransport(
            clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()));

        var rig = new StdioRig { Host = host, Client = client, Notified = notified };
        client.RegisterNotificationHandler(
            NotificationMethods.ToolListChangedNotification,
            (_, _) =>
            {
                Interlocked.Increment(ref rig.Count);
                notified.TrySetResult();
                return ValueTask.CompletedTask;
            });
        return rig;
    }

    [Fact]
    public async Task Spike_1_1_Stdio_ServerCanSend_AndTheLiveServerComesFromDi()
    {
        await using var rig = await StartStdioRigAsync();

        // Question: do we get the live McpServer from DI on the stdio path?
        var server = rig.Host.Services.GetService<McpServer>();
        output.WriteLine($"McpServer resolvable from DI on stdio path: {server is not null}");
        Assert.NotNull(server);

        await server!.SendNotificationAsync(NotificationMethods.ToolListChangedNotification);

        var winner = await Task.WhenAny(rig.Notified.Task, Task.Delay(Bound));
        Assert.True(winner == rig.Notified.Task, "stdio client did not receive list_changed within bound");
        Assert.Equal(1, rig.Count);

        // And the follow-up the acceptance test relies on: tools/list still works after the push.
        var tools = await rig.Client.ListToolsAsync();
        Assert.Contains(tools, t => t.Name == "spike_ping");
    }

    // ---- stateless HTTP: minimal app, same shape as SdkFilterSeamTests -----------------------

    private sealed class ServerHolder
    {
        public McpServer? Server;
    }

    private static async Task<(WebApplication App, string Url, ServerHolder Holder)> StartStatelessHttpAsync(
        Action<McpServerOptions>? configureServer = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var holder = new ServerHolder();
        var mcp = builder.Services
            .AddMcpServer(o => configureServer?.Invoke(o))
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<PingTools>();

        // Capture the per-request server object so we can try to push on it AFTER the request ends.
        mcp.WithRequestFilters(f => f.AddListToolsFilter(next => async (ctx, ct) =>
        {
            holder.Server = ctx.Server;
            return await next(ctx, ct);
        }));

        var app = builder.Build();
        app.MapMcp("/mcp");
        await app.StartAsync();
        return (app, app.Urls.First().TrimEnd('/'), holder);
    }

    private static Task<McpClient> ConnectHttpAsync(string url) =>
        McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{url}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        }));

    [Fact]
    public async Task Spike_1_2_StatelessHttp_CannotDeliver_AndRecordWhatTheSdkDoes()
    {
        var (app, url, holder) = await StartStatelessHttpAsync();
        await using var _ = app;
        await using var client = await ConnectHttpAsync(url);

        var notified = NewSignal();
        client.RegisterNotificationHandler(
            NotificationMethods.ToolListChangedNotification,
            (_, _) => { notified.TrySetResult(); return ValueTask.CompletedTask; });

        await client.ListToolsAsync();
        Assert.NotNull(holder.Server); // filter ran, we captured the per-request server object

        string sendOutcome;
        try
        {
            await holder.Server!.SendNotificationAsync(NotificationMethods.ToolListChangedNotification);
            sendOutcome = "returned normally (dropped or queued)";
        }
        catch (Exception ex)
        {
            sendOutcome = $"threw {ex.GetType().Name}: {ex.Message}";
        }
        output.WriteLine($"Stateless HTTP SendNotificationAsync outcome: {sendOutcome}");

        // Nothing may arrive. A short bounded wait is acceptable in a spike (absence cannot be awaited).
        var winner = await Task.WhenAny(notified.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.True(winner != notified.Task, $"stateless HTTP unexpectedly delivered list_changed ({sendOutcome})");

        // Observed on 1.4.1: the standalone GET stream is "405 Method Not Allowed" in stateless mode (#77).
        using var http = new HttpClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{url}/mcp");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        output.WriteLine($"GET /mcp (SSE) status in stateless mode: {(int)resp.StatusCode} {resp.StatusCode}");
        Assert.True(resp.StatusCode is HttpStatusCode.MethodNotAllowed,
            $"GET /mcp status was {(int)resp.StatusCode} {resp.StatusCode}, send outcome: {sendOutcome}");
    }

    // ---- 1.3: what initialize advertises ----------------------------------------------------

    [Fact]
    public async Task Spike_1_3_Initialize_Stdio_AdvertisesListChanged()
    {
        await using var rig = await StartStdioRigAsync();
        bool? listChanged = rig.Client.ServerCapabilities.Tools?.ListChanged;
        output.WriteLine($"stdio tools.listChanged = {listChanged?.ToString() ?? "null"}");
        Assert.True(listChanged == true, $"stdio tools.listChanged observed: {listChanged?.ToString() ?? "null"}");
    }

    [Fact]
    public async Task Spike_1_3_Initialize_StatelessHttp_ObserveListChanged()
    {
        var (app, url, _) = await StartStatelessHttpAsync();
        await using var _a = app;
        await using var client = await ConnectHttpAsync(url);

        bool? listChanged = client.ServerCapabilities.Tools?.ListChanged;
        output.WriteLine($"stateless HTTP tools.listChanged = {listChanged?.ToString() ?? "null"}");
        // Regression pin (#34 step 6): 1.4.1 does NOT advertise listChanged on stateless HTTP, which is
        // right because we cannot honour it. If this fails, the SDK now advertises a capability we
        // cannot honour (or, after #77, HTTP push exists and this pin should be revisited).
        Assert.True(listChanged != true,
            $"stateless HTTP advertises tools.listChanged; observed: {listChanged?.ToString() ?? "null"}");
    }

    // ---- 1.4: the knob that turns it off ----------------------------------------------------

    [Fact]
    public async Task Spike_1_4_Knob_ExplicitCapabilities_TurnsListChangedOffOnStatelessHttp()
    {
        var (app, url, _) = await StartStatelessHttpAsync(o =>
            o.Capabilities = new ServerCapabilities { Tools = new ToolsCapability { ListChanged = false } });
        await using var _a = app;
        await using var client = await ConnectHttpAsync(url);

        bool? listChanged = client.ServerCapabilities.Tools?.ListChanged;
        output.WriteLine($"stateless HTTP with explicit Capabilities.Tools.ListChanged=false -> {listChanged?.ToString() ?? "null"}");
        Assert.True(listChanged != true, $"knob did not work; observed: {listChanged?.ToString() ?? "null"}");

        // Tools must still be listed and callable with the explicit capabilities block.
        var tools = await client.ListToolsAsync();
        Assert.Contains(tools, t => t.Name == "spike_ping");
    }
}
