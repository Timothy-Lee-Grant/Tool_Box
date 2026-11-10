using Microsoft.AspNetCore.Http;

namespace ToolBox.Host;

/// <summary>
/// Keeps the control plane and data plane apart by the socket the request arrived on:
/// <c>/api/v1</c> exists only on the admin listener, and the admin listener serves nothing else.
/// Everything else gets 404, so neither plane reveals the other's routes. Runs first in the pipeline.
/// </summary>
internal sealed class PlaneSeparationMiddleware(RequestDelegate next, AdminListener admin)
{
    public const string AdminPrefix = "/api/v1";

    public Task InvokeAsync(HttpContext context)
    {
        bool onAdmin = admin.IsAdminPort(context.Connection.LocalPort);
        bool adminPath = context.Request.Path.StartsWithSegments(AdminPrefix, StringComparison.OrdinalIgnoreCase);

        if (onAdmin != adminPath)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }
        return next(context);
    }
}
