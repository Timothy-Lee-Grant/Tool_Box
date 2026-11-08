using System.Net;
using System.Net.Sockets;

namespace ToolBox.Core.Ports;

/// <summary>
/// Pre-flight port check (D2): briefly bind a throwaway <see cref="TcpListener"/> to the
/// same address the real server will use, then release it. There is a small race between
/// probe and real bind — the real bind's failure is translated by
/// <see cref="PortConflictException.IsAddressInUse"/> as the backstop.
/// The probe MUST use the real bind address: a port free on 127.0.0.1 can still be
/// taken on 0.0.0.0 (and vice versa).
/// </summary>
public static class PortProbe
{
    /// <summary>Throws <see cref="PortConflictException"/> if the port is taken. Port 0 ("OS picks") is never a conflict.</summary>
    public static void Check(IPAddress address, int port, string service, string configKey)
    {
        if (port == 0)
            return;

        var listener = new TcpListener(address, port);
        try
        {
            listener.Start();
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            throw new PortConflictException(service, port, address, configKey, ex);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Asks the OS for a currently-free port on the address (tests / port-0 fallbacks).</summary>
    public static int FindFreePort(IPAddress address)
    {
        var listener = new TcpListener(address, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}
