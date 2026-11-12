using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace ToolBox.Host;

/// <summary>
/// DNS-rebinding defence for the MCP socket (issue #66, ADR-021). Guards the whole socket, so <c>/mcp</c> and
/// <c>/health</c> alike, and a route added later cannot slip outside it (the ADR-019 principle).
/// <list type="number">
/// <item>Host: loopback names plus <c>Security:Mcp:AllowedHosts</c>, compared by hostname only (a published Docker
/// port such as <c>localhost:8081</c> differs from the listener's 8080). Anything else gets 421.</item>
/// <item>Origin: absent is fine (agents, curl); present must be a loopback origin or listed in
/// <c>Security:Mcp:AllowedOrigins</c>, else 403 (MCP spec, transport security).</item>
/// </list>
/// Empty body and no CORS headers, matching the admin guard. Admin-socket requests pass through untouched:
/// <see cref="AdminGuardMiddleware"/> owns that socket.
/// </summary>
internal sealed class McpGuardMiddleware(
    RequestDelegate next, AdminListener admin, McpGuardOptions options, ILogger<McpGuardMiddleware> logger)
{
    private readonly HostOriginPolicy _policy = new(options.AllowedHosts, strictPort: false);

    public Task InvokeAsync(HttpContext context)
    {
        int localPort = context.Connection.LocalPort;
        if (admin.IsAdminPort(localPort))
            return next(context);

        HttpRequest request = context.Request;

        if (!_policy.IsAllowedHost(request.Host.Host, request.Host.Port, localPort))
        {
            logger.LogWarning(
                "Rejected MCP request: Host '{Host}' is not allowed (421). To allow it, set {Key}:0=<name>.",
                Clean(request.Host.Value), McpGuardOptions.HostsKey);
            return Reject(context, StatusCodes.Status421MisdirectedRequest);
        }

        if (request.Headers.TryGetValue(HeaderNames.Origin, out var origin) && origin.Count > 0
            && !HostOriginPolicy.IsAllowedOrigin(origin.ToString(), options.AllowedOrigins))
        {
            logger.LogWarning(
                "Rejected MCP request: Origin '{Origin}' is not allowed (403). To allow it, set {Key}:0=<origin>.",
                Clean(origin.ToString()), McpGuardOptions.OriginsKey);
            return Reject(context, StatusCodes.Status403Forbidden);
        }

        return next(context);
    }

    private static Task Reject(HttpContext context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask; // no body, no CORS headers
    }

    /// <summary>Header values are attacker-controlled: drop control characters and cap the length before logging.</summary>
    private static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "(none)";
        string s = new(value.Select(c => char.IsControl(c) ? '?' : c).ToArray());
        return s.Length > 100 ? s[..100] + "..." : s;
    }
}
