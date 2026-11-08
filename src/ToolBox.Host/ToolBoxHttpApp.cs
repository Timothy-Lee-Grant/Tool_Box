using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ToolBox.Core;
using Microsoft.Extensions.Logging;
using ToolBox.Core.Logging;
using ToolBox.Host.Ports;

namespace ToolBox.Host;

/// <summary>
/// Builds the HTTP-mode application (plan 002, Steps 2–3). Extracted from Program.cs
/// so the integration tests boot the EXACT app production runs — same composition,
/// same endpoints, real Kestrel, real sockets — just on an ephemeral port.
/// If tests built their own copy of this, they'd be testing the copy.
/// </summary>
internal static class ToolBoxHttpApp
{
    public static WebApplication Build(string[] args, string? overrideUrl = null,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Stage 2 decision 3: the stderr rule stays uniform across transports.
        builder.Logging.UseStderrOnly();

        // Configuration layers, lowest → highest (issue #37 precedence):
        //   PortTable defaults < appsettings < TOOLBOX_ env < command line < test settings.
        // CreateBuilder already added appsettings/env/command line; the TOOLBOX_ env layer
        // would otherwise outrank the command line, so re-add the command line after it.
        builder.Configuration.AddEnvironmentVariables(prefix: "TOOLBOX_");
        builder.Configuration.AddCommandLine(args);
        if (settings is not null)
        {
            // Added last so tests can override everything, env vars included.
            builder.Configuration.AddInMemoryCollection(settings);
        }

        // Did anyone set the MCP port/address explicitly? Checked BEFORE the defaults
        // layer exists, so a non-null value means a real source set it.
        bool mcpExplicit = builder.Configuration[PortTable.PortKey(PortTable.Mcp)] is not null
            || builder.Configuration[PortTable.AddressKey(PortTable.Mcp)] is not null;
        PortTable.AddDefaults(builder.Configuration);

        // Validates everything (bad IP, range, duplicate ports, non-loopback admin) and
        // fails startup loudly before any socket is opened.
        PortSettings ports = PortSettings.Resolve(builder.Configuration);

        // MCP URL precedence: overrideUrl (tests) > ASPNETCORE_URLS / --urls > Ports:Mcp:*.
        bool urlsSetElsewhere = !string.IsNullOrEmpty(builder.Configuration[WebHostDefaults.ServerUrlsKey]);
        bool warnUrlsWin = false;
        if (overrideUrl is not null)
        {
            // Tests pass http://127.0.0.1:0 — port 0 asks the OS for any free port,
            // so parallel CI runs can never collide on a fixed number.
            builder.WebHost.UseUrls(overrideUrl);
        }
        else if (urlsSetElsewhere)
        {
            // Kept for compatibility (the Dockerfile sets ASPNETCORE_URLS). Left untouched.
            warnUrlsWin = mcpExplicit;
        }
        else
        {
            builder.WebHost.UseUrls(ports.Mcp.Url);
        }

        builder.Services
            .AddToolBoxServer(builder.Configuration)
            .WithHttpTransport(options =>
            {
                // SDK-recommended for servers that don't need server-to-client requests
                // (sampling/elicitation): no in-memory session tracking, horizontal
                // scaling without affinity, and no Mcp-Session-Id header for simpler
                // clients to fumble. Set explicitly for forward compatibility.
                options.Stateless = true;
            });

        var app = builder.Build();

        if (warnUrlsWin)
        {
            app.Logger.LogWarning(
                "Both ASPNETCORE_URLS/--urls and {PortKey}/{AddressKey} are set; the URLs win for MCP.",
                PortTable.PortKey(PortTable.Mcp), PortTable.AddressKey(PortTable.Mcp));
        }

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
