using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ToolBox.Core;
using ToolBox.Core.Admin;

namespace ToolBox.Host;

/// <summary>
/// The control-plane routes (issue #35 / #36). Deliberately plain minimal-API routes and NOT MCP tools:
/// agents reach the host only through MCP, so they have no door to change their own settings (epic #23, decision 7).
/// <para>
/// <see cref="MapAdminApi"/> hands back the one <c>/api/v1</c> group and nothing else. The
/// <see cref="AdminGuardMiddleware"/> authenticates every request on the admin socket before routing, so any
/// endpoint mapped here is guarded; a test enumerates the endpoint table to keep that true.
/// </para>
/// </summary>
internal static class AdminApi
{
    public static RouteGroupBuilder MapAdminApi(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup(PlaneSeparationMiddleware.AdminPrefix);

        group.MapGet("/health", (ServerInfoProvider info) =>
        {
            ServerInfo snapshot = info.Get();
            return Results.Ok(new { version = snapshot.Version, uptime = snapshot.Uptime.ToString() });
        });

        // Replaces the token file with a fresh token. 204 and NO body: no endpoint ever returns the token;
        // the caller (same OS user) re-reads the file. The old token stops working at once.
        group.MapPost("/token/rotate", (AdminTokenStore tokens) =>
        {
            tokens.Rotate();
            return Results.NoContent();
        });

        return group;
    }
}
