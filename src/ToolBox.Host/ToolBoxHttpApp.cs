using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ToolBox.Core;
using ToolBox.Core.Logging;

namespace ToolBox.Host;

/// <summary>
/// Builds the HTTP-mode application (plan 002, Steps 2–3). Extracted from Program.cs
/// so the integration tests boot the EXACT app production runs — same composition,
/// same endpoints, real Kestrel, real sockets — just on an ephemeral port.
/// If tests built their own copy of this, they'd be testing the copy.
/// </summary>
internal static class ToolBoxHttpApp
{
    public static WebApplication Build(string[] args, string? overrideUrl = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Stage 2 decision 3: the stderr rule stays uniform across transports.
        builder.Logging.UseStderrOnly();

        if (overrideUrl is not null)
        {
            // Tests pass http://127.0.0.1:0 — port 0 asks the OS for any free port,
            // so parallel CI runs can never collide on a fixed number.
            builder.WebHost.UseUrls(overrideUrl);
        }
        else if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
        {
            // Default port 8080 unless the environment says otherwise
            // (containers set ASPNETCORE_URLS=http://0.0.0.0:8080; plan 002 Step 4).
            builder.WebHost.UseUrls("http://localhost:8080");
        }

        builder.Services
            .AddToolBoxServer()
            .WithHttpTransport(options =>
            {
                // SDK-recommended for servers that don't need server-to-client requests
                // (sampling/elicitation): no in-memory session tracking, horizontal
                // scaling without affinity, and no Mcp-Session-Id header for simpler
                // clients to fumble. Set explicitly for forward compatibility.
                options.Stateless = true;
            });

        var app = builder.Build();

        // The MCP endpoint (streamable HTTP).
        app.MapMcp("/mcp");

        // Plain HTTP healthcheck — curl-shaped on purpose, for compose/orchestrator
        // probes (Stage 2 decision 7). Not an MCP call: health must be checkable by
        // tools that have never heard of MCP.
        app.MapGet("/health", (ServerInfoProvider info) =>
        {
            ServerInfo snapshot = info.Get();
            return Results.Ok(new
            {
                status = "ok",
                version = snapshot.Version,
                toolsets = snapshot.Toolsets,
                uptime = snapshot.Uptime.ToString(),
            });
        });

        return app;
    }
}
