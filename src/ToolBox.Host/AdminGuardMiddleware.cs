using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using ToolBox.Core.Admin;

namespace ToolBox.Host;

/// <summary>
/// The three guards every control-plane request passes, in this order, before routing:
/// <list type="number">
/// <item>Host allowlist (421) and Origin check (403): defence against DNS rebinding. A rebinding page makes the
/// browser send requests to 127.0.0.1 carrying <c>Host: evil.example</c> / <c>Origin: http://evil.example</c>.
/// It runs first because it needs no secret.</item>
/// <item>Bearer token (401, <c>WWW-Authenticate: Bearer</c>, empty body). The header only: a token in the query
/// string would end up in logs.</item>
/// <item>Writes with a body must be <c>application/json</c> (415), so "simple" cross-site form posts cannot reach them.</item>
/// </list>
/// It guards by socket port, not by route, so a future endpoint cannot be added outside it by accident. No CORS
/// headers are ever emitted: browsers can then neither read responses nor pass a preflight with Authorization.
/// Requests on the MCP port are left alone (see <see cref="PlaneSeparationMiddleware"/>).
/// </summary>
internal sealed class AdminGuardMiddleware(RequestDelegate next, AdminListener admin, AdminTokenStore tokens)
{
    private const string BearerPrefix = "Bearer ";

    public Task InvokeAsync(HttpContext context)
    {
        int localPort = context.Connection.LocalPort;
        if (!admin.IsAdminPort(localPort))
            return next(context);

        HttpRequest request = context.Request;

        // 1a. Host: loopback name AND the real admin port. Missing/other => 421 Misdirected Request.
        if (!IsLoopbackName(request.Host.Host) || request.Host.Port != localPort)
            return Reject(context, StatusCodes.Status421MisdirectedRequest);

        // 1b. Origin: absent is fine (curl, the GUI); present must be this very loopback origin.
        if (request.Headers.TryGetValue(HeaderNames.Origin, out var origin) && origin.Count > 0
            && !IsAllowedOrigin(origin.ToString(), localPort))
            return Reject(context, StatusCodes.Status403Forbidden);

        // 2. Bearer token.
        string? presented = null;
        string header = request.Headers.Authorization.ToString();
        if (header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            presented = header[BearerPrefix.Length..].Trim();

        if (!tokens.Validate(presented))
        {
            context.Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer";
            return Reject(context, StatusCodes.Status401Unauthorized);
        }

        // 3. Writes that carry a body must declare JSON.
        if ((HttpMethods.IsPut(request.Method) || HttpMethods.IsPost(request.Method) || HttpMethods.IsPatch(request.Method))
            && HasBody(request) && !IsJson(request.ContentType))
            return Reject(context, StatusCodes.Status415UnsupportedMediaType);

        return next(context);
    }

    private static Task Reject(HttpContext context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask; // no body: nothing about the guard's internals leaks
    }

    private static bool IsLoopbackName(string host) =>
        host.Equals("127.0.0.1", StringComparison.Ordinal)
        || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host is "[::1]" or "::1";

    private static bool IsAllowedOrigin(string origin, int adminPort) =>
        Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
        && uri.Scheme == Uri.UriSchemeHttp
        && IsLoopbackName(uri.Host.Equals("::1", StringComparison.Ordinal) ? "[::1]" : uri.Host)
        && uri.Port == adminPort;

    private static bool HasBody(HttpRequest request) =>
        request.ContentLength > 0 || request.Headers.ContainsKey(HeaderNames.TransferEncoding);

    private static bool IsJson(string? contentType) =>
        contentType is not null
        && MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? mt)
        && mt.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase);
}
