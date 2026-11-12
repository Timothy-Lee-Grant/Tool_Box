using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ToolBox.Core;
using ToolBox.Core.Admin;
using ToolBox.Core.Health;
using ToolBox.Core.Toolsets;

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

        group.MapGet("/status", (ServerInfoProvider info, ComponentStatusRegistry registry,
            DesiredRosterSource roster, ToolsetStatusBoard board) =>
        {
            ServerInfo snapshot = info.Get();
            IReadOnlyDictionary<string, string> components = registry.Snapshot();
            var toolsets = Toolsets(roster, board);
            bool degraded = components.Values.Any(v => v != "ok")
                || toolsets.Values.Any(t => t.Actual == "failed");

            // The registry stores "corrupt: reason" / "unsupported: ..."; split it into status + reason.
            string? raw = components.GetValueOrDefault("desiredState");
            string desiredStatus = raw is null ? "ok" : raw.Split(':')[0];
            string? reason = raw is null ? null : raw[(raw.IndexOf(':') + 1)..].Trim();

            return Results.Ok(new
            {
                status = degraded ? "degraded" : "ok",
                version = snapshot.Version,
                uptime = snapshot.Uptime.ToString(),
                components,
                desiredState = new { status = desiredStatus, path = roster.FilePath, reason },
                toolsets,
            });
        });

        group.MapGet("/toolsets", (DesiredRosterSource roster, ToolsetStatusBoard board) =>
            Results.Ok(new { toolsets = Toolsets(roster, board) }));

        group.MapGet("/toolsets/{id}", (string id, DesiredRosterSource roster, ToolsetStatusBoard board,
            AdminWriteService writes) =>
        {
            string? canonical = writes.Canonical(id);
            return canonical is null
                ? AdminProblems.UnknownToolset(id)
                : Results.Ok(DesiredRosterSource.Describe(roster.Current, board, canonical));
        });

        // Body {"enabled": true|false}. 202: the intent is durable and a reconcile is queued; `actual`
        // in the response may still be the old state, so callers poll GET /status.
        group.MapPut("/toolsets/{id}/enabled", async (string id, HttpRequest request,
            AdminWriteService writes, CancellationToken ct) =>
        {
            // Order matters: unknown id (404), then pin (409), then the body (400), then the write.
            if (writes.CheckEnabledWritable(id, out string canonical) is { } refusal) return refusal;

            bool enabled;
            try
            {
                using JsonDocument doc = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
                if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || !doc.RootElement.TryGetProperty("enabled", out JsonElement flag)
                    || flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return AdminProblems.InvalidBody("Body must be a JSON object like {\"enabled\": true}.");
                enabled = flag.GetBoolean();
            }
            catch (JsonException)
            {
                return AdminProblems.InvalidBody("Body is not valid JSON.");
            }

            return await writes.SetEnabledAsync(canonical, enabled, ct);
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

    private static SortedDictionary<string, AdminToolsetView> Toolsets(DesiredRosterSource roster, ToolsetStatusBoard board)
    {
        EnabledResolution current = roster.Current;
        var result = new SortedDictionary<string, AdminToolsetView>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in roster.KnownIds)
            result[id.ToLowerInvariant()] = DesiredRosterSource.Describe(current, board, id);
        return result;
    }
}
