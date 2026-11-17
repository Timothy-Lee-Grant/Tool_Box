using System.Net;
using System.Net.Sockets;

namespace ToolBox.Core.Ports;

/// <summary>
/// Pre-flight port check (D2): briefly bind a throwaway <see cref="TcpListener"/> to the
/// same address the real server will use, then release it. There is a small race between
/// probe and real bind — the real bind's failure is translated by
/// <see cref="PortConflictException.IsAddressInUse"/> as the backstop.
/// The probe MUST use the real bind address: a port free on 127.0.0.1 can still be
/// taken on 0.0.0.0 (and vice versa). For wildcard addresses the probe additionally binds
/// 127.0.0.1 and ::1, because macOS allows a wildcard bind next to a specific-address bind.
/// </summary>
public static class PortProbe
{
    /// <summary>Throws <see cref="PortConflictException"/> if the port is taken. Port 0 ("OS picks") is never a conflict.</summary>
    public static void Check(IPAddress address, int port, string service, string configKey)
    {
        if (port == 0)
            return;

        if (TryBind(address, port, out var ex))
        {
            throw new PortConflictException(service, port, address, configKey, ex);
        }

        // macOS/BSD let a wildcard bind succeed beside a specific-address bind, so a wildcard
        // probe also checks the loopback addresses it could shadow. The first conflict is enough.
        // Errors keep reporting the REQUESTED address so the message matches the user's config.
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            foreach (var loopback in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
            {
                if (TryBind(loopback, port, out ex))
                    throw new PortConflictException(service, port, address, configKey, ex);
            }
        }
    }

    /// <summary>
    /// Binds and releases one throwaway listener. Returns true (with the exception) only when the
    /// address is in use; an address/family the machine lacks (e.g. no ::1) counts as "free" so it is skipped.
    /// Any other socket error propagates.
    /// </summary>
    private static bool TryBind(IPAddress address, int port, out SocketException? inUse)
    {
        inUse = null;
        var listener = new TcpListener(address, port);
        try
        {
            listener.Start();
            return false;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            inUse = ex;
            return true;
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressNotAvailable
                                                             or SocketError.AddressFamilyNotSupported)
        {
            return false;
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
