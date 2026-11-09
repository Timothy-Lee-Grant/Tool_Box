using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ToolBox.Core.Health;

namespace ToolBox.Voxel.Tests;

/// <summary>
/// #32 step 4: Voxel's lifecycle binds and releases the viewer port, never touches the world,
/// and degrades (does not throw) when the port is taken (ADR-016). Real sockets, OS-chosen ports.
/// </summary>
[Collection("VoxelViewer")]
public class VoxelLifecycleTests
{
    private static (VoxelLifecycle Lifecycle, VoxelViewerBroadcastService Viewer, VoxelWorld World, ComponentStatusRegistry Status)
        Create(int port)
    {
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [VoxelViewerBroadcastService.PortKey] = port.ToString(),
            [VoxelViewerBroadcastService.AddressKey] = "127.0.0.1",
        }).Build();
        var world = new VoxelWorld();
        var status = new ComponentStatusRegistry();
        var viewer = new VoxelViewerBroadcastService(world, config, status, NullLogger<VoxelViewerBroadcastService>.Instance);
        return (new VoxelLifecycle(viewer), viewer, world, status);
    }

    private static bool CanBind(int port)
    {
        try
        {
            var probe = new TcpListener(IPAddress.Loopback, port);
            probe.Start();
            probe.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    [Fact]
    public void ToolsetId_IsVoxel() => Assert.Equal("voxel", Create(0).Lifecycle.ToolsetId);

    [Fact]
    public async Task Start_BindsPort_Stop_ReleasesIt()
    {
        var (lifecycle, viewer, _, status) = Create(0);

        await lifecycle.StartAsync(CancellationToken.None);
        int port = viewer.Port!.Value;
        Assert.Equal("ok", status.Snapshot()["viewer"]);
        Assert.False(CanBind(port));

        await lifecycle.StopAsync(CancellationToken.None);

        Assert.Null(viewer.Port);
        Assert.False(status.Snapshot().ContainsKey("viewer"));
        Assert.True(CanBind(port));
    }

    [Fact]
    public async Task StartStopStart_Rebinds_AndKeepsTheWorld()
    {
        var (lifecycle, viewer, world, _) = Create(0);
        await lifecycle.StartAsync(CancellationToken.None);
        world.PlaceBlock(new VoxelCoordinate(1, 2, 3), "stone");

        await lifecycle.StopAsync(CancellationToken.None);
        Assert.Equal(1, world.Count);

        await lifecycle.StartAsync(CancellationToken.None);
        Assert.NotNull(viewer.Port);
        Assert.Equal(1, world.Count);

        await lifecycle.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Start_WhenAlreadyRunning_IsANoOp()
    {
        var (lifecycle, viewer, _, _) = Create(0);
        await lifecycle.StartAsync(CancellationToken.None);
        int port = viewer.Port!.Value;

        await lifecycle.StartAsync(CancellationToken.None);

        Assert.Equal(port, viewer.Port);
        await lifecycle.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Stop_WithoutStart_DoesNotThrow()
    {
        var (lifecycle, _, _, _) = Create(0);
        await lifecycle.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Start_PortTaken_DegradesWithoutThrowing_AndStopClearsStatus()
    {
        using var holder = new TcpListener(IPAddress.Loopback, 0);
        holder.Start();
        int taken = ((IPEndPoint)holder.LocalEndpoint).Port;
        var (lifecycle, viewer, _, status) = Create(taken);

        await lifecycle.StartAsync(CancellationToken.None);

        Assert.Null(viewer.Port);
        Assert.Contains("in use", status.Snapshot()["viewer"]);

        await lifecycle.StopAsync(CancellationToken.None);
        Assert.False(status.Snapshot().ContainsKey("viewer"));
    }

    [Fact]
    public async Task WorldChange_WhileStopping_DoesNotThrow()
    {
        var (lifecycle, _, world, _) = Create(0);
        await lifecycle.StartAsync(CancellationToken.None);

        Task writer = Task.Run(() =>
        {
            for (int i = 0; i < 200; i++)
            {
                world.PlaceBlock(new VoxelCoordinate(i, 0, 0), "stone");
            }
        });
        await lifecycle.StopAsync(CancellationToken.None);

        await writer;
    }
}
