using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;

namespace ToolBox.Host.Ports;

/// <summary>One service's resolved, validated bind.</summary>
internal sealed record ServiceBinding(string Service, string Name, int Port, IPAddress Address)
{
    public string PortKey => PortTable.PortKey(Name);
    public string AddressKey => PortTable.AddressKey(Name);

    /// <summary>"http://127.0.0.1:8080" — IPv6 addresses are bracketed.</summary>
    public string Url
    {
        get
        {
            string host = Address.AddressFamily == AddressFamily.InterNetworkV6
                ? $"[{Address}]" : Address.ToString();
            return $"http://{host}:{Port}";
        }
    }
}

/// <summary>Invalid port configuration — fails startup loudly with a message naming the key.</summary>
internal sealed class PortConfigurationException(string message) : Exception(message);

/// <summary>The three resolved bindings. Port 0 means "OS picks" (tests).</summary>
internal sealed record PortSettings(ServiceBinding Mcp, ServiceBinding Admin, ServiceBinding Viewer)
{
    public static PortSettings Resolve(IConfiguration config)
    {
        var settings = new PortSettings(
            ResolveOne(config, PortTable.Mcp, "MCP"),
            ResolveOne(config, PortTable.Admin, "admin API"),
            ResolveOne(config, PortTable.Viewer, "viewer"));

        // Brainstorm 004: the admin API is loopback-only. Refuse rather than warn.
        if (!IPAddress.IsLoopback(settings.Admin.Address))
            throw new PortConfigurationException(
                $"{settings.Admin.AddressKey} is '{settings.Admin.Address}', but the admin API must bind to a loopback address " +
                "(127.0.0.1 or ::1). It is never exposed beyond this machine.");

        var all = new[] { settings.Mcp, settings.Admin, settings.Viewer };
        foreach (var group in all.Where(b => b.Port != 0).GroupBy(b => b.Port).Where(g => g.Count() > 1))
        {
            string names = string.Join(" and ", group.Select(b => $"{b.Service} ({b.PortKey})"));
            throw new PortConfigurationException($"{names} are both configured for port {group.Key}. Each service needs its own port.");
        }

        return settings;
    }

    private static ServiceBinding ResolveOne(IConfiguration config, string name, string service)
    {
        string portKey = PortTable.PortKey(name), addressKey = PortTable.AddressKey(name);

        string? rawPort = config[portKey];
        if (!int.TryParse(rawPort, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 0 or > 65535)
            throw new PortConfigurationException($"{portKey} is '{rawPort}' ({service}); expected a port number 0-65535.");

        string? rawAddress = config[addressKey];
        if (!IPAddress.TryParse(rawAddress, out IPAddress? address))
            throw new PortConfigurationException($"{addressKey} is '{rawAddress}' ({service}); expected an IP address such as 127.0.0.1 or 0.0.0.0.");

        return new ServiceBinding(service, name, port, address);
    }
}
