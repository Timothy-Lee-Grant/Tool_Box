using System.Net;
using System.Net.Sockets;
using ToolBox.Core.Health;
using ToolBox.Core.Ports;

namespace ToolBox.Core.Tests;

public class PortProbeTests
{
    [Fact]
    public void Check_FreePort_Passes()
    {
        int port = PortProbe.FindFreePort(IPAddress.Loopback);

        PortProbe.Check(IPAddress.Loopback, port, "MCP", "Ports:Mcp:Port");
    }

    [Fact]
    public void Check_PortZero_IsSkipped()
    {
        PortProbe.Check(IPAddress.Loopback, 0, "MCP", "Ports:Mcp:Port");
    }

    [Fact]
    public void Check_OccupiedPort_ThrowsNamingServicePortAndConfigKey()
    {
        using var holder = new TcpListener(IPAddress.Loopback, 0);
        holder.Start();
        int port = ((IPEndPoint)holder.LocalEndpoint).Port;

        var ex = Assert.Throws<PortConflictException>(
            () => PortProbe.Check(IPAddress.Loopback, port, "MCP", "Ports:Mcp:Port"));

        Assert.Contains("MCP", ex.Message);
        Assert.Contains(port.ToString(), ex.Message);
        Assert.Contains("127.0.0.1", ex.Message);
        Assert.Contains("TOOLBOX_Ports__Mcp__Port", ex.Message);
        Assert.Equal("MCP", ex.Service);
        Assert.Equal(port, ex.Port);
    }

    [Fact]
    public void Check_PortHeldOnLoopback_ConflictsWithWildcardProbe()
    {
        // A port taken on 127.0.0.1 blocks a 0.0.0.0 bind of the same port — why the probe
        // must use the real bind address rather than assuming one.
        using var holder = new TcpListener(IPAddress.Loopback, 0);
        holder.Start();
        int port = ((IPEndPoint)holder.LocalEndpoint).Port;

        Assert.Throws<PortConflictException>(
            () => PortProbe.Check(IPAddress.Any, port, "viewer", "Ports:Viewer:Port"));
    }

    [Fact]
    public void Check_ReleasesThePortAfterProbing()
    {
        int port = PortProbe.FindFreePort(IPAddress.Loopback);

        PortProbe.Check(IPAddress.Loopback, port, "MCP", "Ports:Mcp:Port");
        PortProbe.Check(IPAddress.Loopback, port, "MCP", "Ports:Mcp:Port"); // would throw if the first probe leaked
    }

    [Fact]
    public void IsAddressInUse_FindsSocketErrorInsideWrappers()
    {
        var inner = new SocketException((int)SocketError.AddressAlreadyInUse);
        var wrapped = new IOException("Failed to bind", inner);

        Assert.True(PortConflictException.IsAddressInUse(wrapped));
        Assert.False(PortConflictException.IsAddressInUse(new IOException("other")));
        Assert.False(PortConflictException.IsAddressInUse(null));
    }

    [Fact]
    public void IsAddressInUse_RecognisesHttpListenerErrorCode()
    {
        Assert.True(PortConflictException.IsAddressInUse(new HttpListenerException(98)));
        Assert.False(PortConflictException.IsAddressInUse(new HttpListenerException(5)));
    }
}

public class ComponentStatusRegistryTests
{
    [Fact]
    public void Snapshot_IsEmptyInitially()
    {
        Assert.Empty(new ComponentStatusRegistry().Snapshot());
    }

    [Fact]
    public void Set_OverwritesAndSnapshotIsSorted()
    {
        var registry = new ComponentStatusRegistry();
        registry.Set("viewer", "ok");
        registry.Set("admin", "ok");
        registry.Set("viewer", "unavailable: port 8090 in use");

        var snapshot = registry.Snapshot();

        Assert.Equal(["admin", "viewer"], snapshot.Keys);
        Assert.Equal("unavailable: port 8090 in use", snapshot["viewer"]);
    }

    [Fact]
    public void Snapshot_IsACopy()
    {
        var registry = new ComponentStatusRegistry();
        registry.Set("viewer", "ok");
        var before = registry.Snapshot();

        registry.Set("viewer", "unavailable");

        Assert.Equal("ok", before["viewer"]);
    }
}
