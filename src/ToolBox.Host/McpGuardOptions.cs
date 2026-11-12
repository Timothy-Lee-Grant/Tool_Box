using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ToolBox.Host;

/// <summary>
/// Settings for the MCP-port Host/Origin guard (issue #66, ADR to be written in PR B).
/// <list type="bullet">
/// <item><c>Security:Mcp:AllowedHosts</c>: extra host names (hostname only, the port is ignored so a published
/// Docker port like localhost:8081 works). Loopback names are always allowed.</item>
/// <item><c>Security:Mcp:AllowedOrigins</c>: extra full origins. Loopback origins on any port are always allowed.</item>
/// <item>Legacy fallback: if the MCP key is unset, ASP.NET's own <c>AllowedHosts</c> is used (unless empty or <c>*</c>),
/// so external composes (LLM_Monitor) keep working.</item>
/// </list>
/// Lists bind with the standard .NET syntax, e.g. <c>Security:Mcp:AllowedHosts:0=toolbox</c> or the env var
/// <c>TOOLBOX_Security__Mcp__AllowedHosts__0=toolbox</c>. This class only reads and validates; nothing enforces it yet.
/// </summary>
internal sealed class McpGuardOptions
{
    public const string HostsKey = "Security:Mcp:AllowedHosts";
    public const string OriginsKey = "Security:Mcp:AllowedOrigins";
    public const string LegacyHostsKey = "AllowedHosts";

    /// <summary>Extra allowed host names (beyond loopback), already split and trimmed.</summary>
    public IReadOnlyList<string> AllowedHosts { get; init; } = [];

    /// <summary>Extra allowed full origins (beyond loopback).</summary>
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];

    /// <summary>True when <see cref="AllowedHosts"/> came from the legacy <c>AllowedHosts</c> key.</summary>
    public bool UsedLegacyHostsKey { get; init; }

    /// <summary>True when any MCP listener binds a non-loopback address (wildcards count).</summary>
    public bool BindsNonLoopback { get; init; }

    /// <summary>Non-loopback bind with no configured hosts: only loopback names will pass, which is easy to trip over in Docker.</summary>
    public bool NeedsAllowlistWarning => BindsNonLoopback && AllowedHosts.Count == 0;

    /// <param name="mcpUrls">The MCP listener URLs, semicolon-separated (the admin URL excluded).</param>
    public static McpGuardOptions Resolve(IConfiguration config, string? mcpUrls)
    {
        List<string> hosts = ReadList(config, HostsKey);
        bool legacy = false;
        if (hosts.Count == 0)
        {
            string? raw = config[LegacyHostsKey];
            if (!string.IsNullOrWhiteSpace(raw) && raw.Trim() != "*")
            {
                hosts = Split(raw);
                legacy = hosts.Count > 0;
            }
        }

        // A "*" or a wildcard entry would silently disable the check; drop it rather than honour it.
        hosts.RemoveAll(h => h.Contains('*'));

        return new McpGuardOptions
        {
            AllowedHosts = hosts,
            AllowedOrigins = ReadList(config, OriginsKey),
            UsedLegacyHostsKey = legacy,
            BindsNonLoopback = BindsNonLoopbackAddress(mcpUrls),
        };
    }

    /// <summary>Startup log lines described in the plan. Called by the guard wiring in PR B.</summary>
    public void LogStartup(ILogger logger)
    {
        if (UsedLegacyHostsKey)
            logger.LogInformation(
                "MCP Host allowlist taken from the legacy '{Legacy}' key. Prefer '{Key}' (e.g. {Key}:0=toolbox).",
                LegacyHostsKey, HostsKey, HostsKey);
        if (NeedsAllowlistWarning)
            logger.LogWarning(
                "MCP binds a non-loopback address but only loopback Host names are allowed. Requests with other Host " +
                "headers (a Docker service name, a LAN name) get 421. Set {Key}:0=<name> to allow one.", HostsKey);
    }

    private static List<string> ReadList(IConfiguration config, string key)
    {
        IConfigurationSection section = config.GetSection(key);
        var items = new List<string>();
        // List form (key:0, key:1): what the TOOLBOX_ env var __0 syntax produces.
        foreach (IConfigurationSection child in section.GetChildren())
            if (!string.IsNullOrWhiteSpace(child.Value)) items.AddRange(Split(child.Value));
        // Scalar form: a semicolon-separated string.
        if (!string.IsNullOrWhiteSpace(section.Value)) items.AddRange(Split(section.Value));
        return items;
    }

    private static List<string> Split(string value) =>
        [.. value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static bool BindsNonLoopbackAddress(string? urls)
    {
        if (string.IsNullOrWhiteSpace(urls)) return false;
        foreach (string url in Split(urls))
        {
            string normalized = url.Replace("://+", "://0.0.0.0").Replace("://*", "://0.0.0.0");
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri)) continue;
            if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) continue;
            if (System.Net.IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip))
            {
                if (System.Net.IPAddress.IsLoopback(ip)) continue;
                return true; // includes 0.0.0.0 and ::
            }
            return true; // an arbitrary DNS name: treat as non-loopback
        }
        return false;
    }
}
