using System.Net;
using System.Net.Sockets;

namespace ToolBox.Core.Ports;

/// <summary>
/// A configured port is already taken. Names the service, the port, the address and the
/// config key so the operator knows what to stop or change (issue #37, R1: fail clearly).
/// Core knows no specific service — callers pass the display name (ADR-003).
/// </summary>
public sealed class PortConflictException : Exception
{
    public PortConflictException(string service, int port, IPAddress address, string configKey, Exception? inner = null)
        : base(BuildMessage(service, port, address, configKey), inner)
    {
        Service = service;
        Port = port;
        Address = address;
        ConfigKey = configKey;
    }

    public string Service { get; }
    public int Port { get; }
    public IPAddress Address { get; }
    public string ConfigKey { get; }

    /// <summary>"Ports:Mcp:Port" → "TOOLBOX_Ports__Mcp__Port" (the env-var form of a config key).</summary>
    public static string EnvVarName(string configKey) => "TOOLBOX_" + configKey.Replace(":", "__");

    private static string BuildMessage(string service, int port, IPAddress address, string configKey) =>
        $"Port {port} ({service}) is already in use on {address}. " +
        $"Set {EnvVarName(configKey)} (or {configKey}) to another port, or stop the other process.";

    /// <summary>
    /// The thin "D1" half: true when a real bind failure (Kestrel's IOException, a raw
    /// SocketException, or HttpListenerException) is really "address in use", so callers
    /// can rethrow it as a <see cref="PortConflictException"/> when the pre-flight probe
    /// lost the race.
    /// </summary>
    public static bool IsAddressInUse(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse })
                return true;
            // HttpListenerException carries the native error code: EADDRINUSE=98 (Linux),
            // WSAEADDRINUSE=10048 (Windows).
            if (ex is HttpListenerException { ErrorCode: 98 or 10048 })
                return true;
        }
        return false;
    }
}
