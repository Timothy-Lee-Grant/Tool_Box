using System.Net;
using Microsoft.Extensions.Configuration;
using ToolBox.Core.Instance;

namespace ToolBox.Host;

/// <summary>
/// Background run mode (issue #38, ADR-024): a run PROFILE on top of the HTTP transport, not a third transport.
/// It adds the single-instance lock and <c>host.json</c>; everything else is plain HTTP mode.
/// </summary>
internal static class BackgroundMode
{
    public const string ModeKey = "Mode";
    public const string DirKey = "Instance:Dir";
    public const int AlreadyRunningExitCode = 4;

    public static bool IsBackground(IConfiguration config) =>
        string.Equals(config[ModeKey]?.Trim(), "background", StringComparison.OrdinalIgnoreCase);

    public static string InstanceDir(IConfiguration config) =>
        string.IsNullOrWhiteSpace(config[DirKey]) ? InstanceLock.DefaultDirectory() : config[DirKey]!;

    /// <summary>A bound wildcard address ([::], 0.0.0.0) is not a place a client can connect to; use loopback.</summary>
    public static string Reachable(string url)
    {
        if (!Uri.TryCreate(url.Replace("://+", "://0.0.0.0").Replace("://*", "://0.0.0.0"), UriKind.Absolute, out Uri? uri))
            return url;
        bool wildcard = IPAddress.TryParse(uri.Host.Trim('[', ']'), out IPAddress? ip)
            && (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any));
        return wildcard ? $"{uri.Scheme}://127.0.0.1:{uri.Port}" : url;
    }

    /// <summary>The one line a second launch prints. Verifies host.json instead of trusting it.</summary>
    public static async Task<string> DescribeRunningAsync(string dir)
    {
        HostInfo? info = HostInfoFile.TryRead(dir);
        if (info is null)
            return $"Tool_Box is already running (the lock in {dir} is held), but {HostInfoFile.FileName} is missing or unreadable, so its address is unknown.";
        if (!await HostProbe.IsRunningAsync(info))
            return $"Tool_Box is already running (pid {info.Pid}; the lock in {dir} is held), but {info.McpUrl} did not answer /health. It may still be starting or shutting down.";
        return $"Tool_Box is already running (pid {info.Pid}) - MCP {info.McpUrl}, admin {info.AdminUrl}";
    }
}
