using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ToolBox.Core.Health;
using ToolBox.Core.Ports;
using ToolBox.Voxel;

namespace ToolBox.Host.Tests;

/// <summary>
/// Issue #37 steps 3-4: the MCP pre-flight check (R1, fail fast) and the viewer's visible
/// degradation (R2). Every test uses real sockets; ports come from the OS so nothing collides.
/// </summary>
[Collection(VoxelViewerCollection.Name)]
public class PortConflictTests
{
    private static TcpListener Occupy(out int port)
    {
        var holder = new TcpListener(IPAddress.Loopback, 0);
        holder.Start();
        port = ((IPEndPoint)holder.LocalEndpoint).Port;
        return holder;
    }

    private static Dictionary<string, string?> Settings(int mcpPort, int viewerPort, string viewerAddress = "127.0.0.1") => new()
    {
        ["Ports:Mcp:Port"] = mcpPort.ToString(),
        ["Ports:Viewer:Port"] = viewerPort.ToString(),
        ["Ports:Viewer:Address"] = viewerAddress,
    };

    private static VoxelViewerBroadcastService Viewer(WebApplication app) =>
        app.Services.GetServices<IHostedService>().OfType<VoxelViewerBroadcastService>().Single();

    // ---- Step 3: MCP (R1) ----

    [Fact]
    public async Task Mcp_PortTaken_FailsFastNamingServicePortAndKey()
    {
        using TcpListener holder = Occupy(out int taken);
        var app = ToolBoxHttpApp.Build([], settings: Settings(taken, viewerPort: 0));
        await using var _ = app;

        var ex = await Assert.ThrowsAsync<PortConflictException>(() => ToolBoxHttpApp.RunAsync(app));

        Assert.Contains("MCP", ex.Message);
        Assert.Contains(taken.ToString(), ex.Message);
        Assert.Contains("TOOLBOX_Ports__Mcp__Port", ex.Message);
    }

    [Fact]
    public async Task Mcp_PortTakenAfterProbe_BindFailureIsTranslated()
    {
        // Free at probe time ...
        int port = PortProbe.FindFreePort(IPAddress.Loopback);
        var app = ToolBoxHttpApp.Build([], settings: Settings(port, viewerPort: 0));
        await using var _ = app;

        // ... then someone grabs it between the probe and Kestrel's real bind (the D1 backstop).
        TcpListener? thief = null;
        try
        {
            var ex = await Assert.ThrowsAsync<PortConflictException>(() =>
                ToolBoxHttpApp.RunAsync(app, afterPreflight: () =>
                {
                    thief = new TcpListener(IPAddress.Loopback, port);
                    thief.Start();
                }));

            Assert.Contains("MCP", ex.Message);
            Assert.Contains(port.ToString(), ex.Message);
            Assert.NotNull(ex.InnerException); // the original bind failure is preserved
        }
        finally
        {
            thief?.Stop();
        }
    }

    // ---- Step 4: viewer (R2) ----

    [Fact]
    public async Task Viewer_FreePort_ReportsOkAndAcceptsBothLoopbackHostNames()
    {
        var app = ToolBoxHttpApp.Build([], overrideUrl: "http://127.0.0.1:0", settings: Settings(0, viewerPort: 0));
        await using var _ = app;
        await app.StartAsync();
        try
        {
            Assert.Equal("ok", app.Services.GetRequiredService<ComponentStatusRegistry>().Snapshot()["viewer"]);
            int port = Viewer(app).Port!.Value;

            // viewer/index.html connects to "localhost"; scripts may use the literal IP.
            // In loopback mode BOTH must work (two HttpListener prefixes).
            foreach (string host in new[] { "127.0.0.1", "localhost" })
            {
                using var client = new ClientWebSocket();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await client.ConnectAsync(new Uri($"ws://{host}:{port}/voxel/"), cts.Token);

                var buffer = new byte[4096];
                WebSocketReceiveResult first = await client.ReceiveAsync(buffer, cts.Token);
                Assert.Contains("snapshot", System.Text.Encoding.UTF8.GetString(buffer, 0, first.Count));
            }
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Viewer_PortTaken_DegradesVisiblyAndHostStillStarts_NoFallback()
    {
        using TcpListener holder = Occupy(out int taken);
        var app = ToolBoxHttpApp.Build([], overrideUrl: "http://127.0.0.1:0", settings: Settings(0, viewerPort: taken));
        await using var _ = app;

        await app.StartAsync(); // must NOT throw: the viewer is optional
        try
        {
            Assert.Equal($"unavailable: port {taken} in use",
                app.Services.GetRequiredService<ComponentStatusRegistry>().Snapshot()["viewer"]);
            Assert.Null(Viewer(app).Port); // and it did not hop to 8091+
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Viewer_AllInterfacesAddress_StillReachableViaLoopback()
    {
        var app = ToolBoxHttpApp.Build([], overrideUrl: "http://127.0.0.1:0",
            settings: Settings(0, viewerPort: 0, viewerAddress: "0.0.0.0"));
        await using var _ = app;
        await app.StartAsync();
        try
        {
            Assert.Equal("ok", app.Services.GetRequiredService<ComponentStatusRegistry>().Snapshot()["viewer"]);

            using var client = new ClientWebSocket();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await client.ConnectAsync(new Uri($"ws://127.0.0.1:{Viewer(app).Port}/voxel/"), cts.Token);
            Assert.Equal(WebSocketState.Open, client.State);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Theory]
    [InlineData("0.0.0.0", new[] { "http://+:8090/voxel/" })]
    [InlineData("127.0.0.1", new[] { "http://127.0.0.1:8090/voxel/", "http://localhost:8090/voxel/" })]
    public void BuildPrefixes_MapsBindAddressToHttpListenerPrefixes(string address, string[] expected) =>
        Assert.Equal(expected, VoxelViewerBroadcastService.BuildPrefixes(IPAddress.Parse(address), 8090));
}
