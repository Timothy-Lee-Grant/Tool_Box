using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using ToolBox.Core.Ports;

namespace ToolBox.Host.Tests;

/// <summary>
/// The admin listener is required (it is the control plane), so a taken admin port stops the host
/// exactly like a taken MCP port does (exit 3) — but the message must name the ADMIN port key.
/// </summary>
[Collection(VoxelViewerCollection.Name)]
public class AdminPortConflictTests
{
    private static Dictionary<string, string?> Settings(int adminPort) => new()
    {
        ["Ports:Mcp:Port"] = "0",
        ["Ports:Viewer:Port"] = "0",
        ["Ports:Admin:Port"] = adminPort.ToString(),
    };

    [Fact]
    public async Task Admin_port_taken_fails_fast_naming_the_admin_key()
    {
        using var holder = new TcpListener(IPAddress.Loopback, 0);
        holder.Start();
        int taken = ((IPEndPoint)holder.LocalEndpoint).Port;

        var app = ToolBoxHttpApp.Build([], settings: Settings(taken));
        await using var _ = app;

        var ex = await Assert.ThrowsAsync<PortConflictException>(() => ToolBoxHttpApp.RunAsync(app));

        Assert.Equal("Admin", ex.Service);
        Assert.Contains(taken.ToString(), ex.Message);
        Assert.Contains("TOOLBOX_Ports__Admin__Port", ex.Message);
        Assert.DoesNotContain("MCP", ex.Message);
    }

    [Fact]
    public async Task Admin_port_taken_after_probe_is_translated_and_named_Admin()
    {
        int port = PortProbe.FindFreePort(IPAddress.Loopback);
        var app = ToolBoxHttpApp.Build([], settings: Settings(port));
        await using var _ = app;

        TcpListener? thief = null;
        try
        {
            var ex = await Assert.ThrowsAsync<PortConflictException>(() =>
                ToolBoxHttpApp.RunAsync(app, afterPreflight: () =>
                {
                    thief = new TcpListener(IPAddress.Loopback, port);
                    thief.Start();
                }));

            Assert.Equal("Admin", ex.Service);
            Assert.Contains(port.ToString(), ex.Message);
            Assert.NotNull(ex.InnerException);
        }
        finally
        {
            thief?.Stop();
        }
    }
}
