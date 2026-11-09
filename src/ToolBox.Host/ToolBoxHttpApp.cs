using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ToolBox.Core;
using Microsoft.Extensions.Logging;
using ToolBox.Core.Health;
using ToolBox.Core.Logging;
using ToolBox.Core.Ports;
using ToolBox.Core.Toolsets;
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
        app.MapGet("/health", (ServerInfoProvider info, ComponentStatusRegistry registry,
            DesiredRosterSource roster, ToolsetStatusBoard board) =>
        {
            ServerInfo snapshot = info.Get();
            // Optional components (e.g. the Voxel viewer) report here. One being down makes the
            // host "degraded" but never unhealthy: still HTTP 200, so the compose healthcheck passes.
            IReadOnlyDictionary<string, string> components = registry.Snapshot();
            // Toolset state is kept apart from components: "off" is normal, only "failed" degrades.
            var details = roster.Details(board);
            bool degraded = components.Values.Any(v => v != "ok")
                || details.Values.Any(d => d.Actual == "failed");
            return Results.Ok(new
            {
                status = degraded ? "degraded" : "ok",
                components,
                version = snapshot.Version,
                toolsets = snapshot.Toolsets,
                toolsetDetails = details,
                uptime = snapshot.Uptime.ToString(),
            });
        });

        return app;
    }

    /// <summary>
    /// Runs the app with a fail-fast port check (issue #37, R1). MCP is a required service,
    /// so a taken port stops the host with a <see cref="PortConflictException"/> that names
    /// the service, port and config key, instead of Kestrel's raw bind exception.
    /// <paramref name="afterPreflight"/> is a test seam: it runs between the probe and the
    /// real bind, which is exactly where the TOCTOU race lives.
    /// </summary>
    public static async Task RunAsync(WebApplication app, Action? afterPreflight = null)
    {
        // Resolved ONCE, up front: after a failed run the app's services are disposed,
        // so the catch block below must not touch the app again.
        var endpoints = ConfiguredEndpoints(app).ToList();

        Preflight(endpoints);
        afterPreflight?.Invoke();
        try
        {
            await app.RunAsync();
        }
        catch (Exception ex) when (PortConflictException.IsAddressInUse(ex))
        {
            // D1 backstop: the probe passed but the real bind lost the race. Re-probe to
            // name the exact endpoint that is taken; if every probe now passes (the other
            // process already let go), report the first configured endpoint.
            Preflight(endpoints, inner: ex);
            (IPAddress address, int port) = endpoints.FirstOrDefault();
            throw new PortConflictException(McpService, port, address ?? IPAddress.Loopback, McpPortKey, ex);
        }
    }

    private const string McpService = "MCP";
    private static readonly string McpPortKey = PortTable.PortKey(PortTable.Mcp);

    /// <summary>Probes every URL the MCP server is about to bind (ASPNETCORE_URLS can hold several).</summary>
    private static void Preflight(List<(IPAddress Address, int Port)> endpoints, Exception? inner = null)
    {
        foreach ((IPAddress address, int port) in endpoints)
        {
            try
            {
                PortProbe.Check(address, port, McpService, McpPortKey);
            }
            catch (PortConflictException) when (inner is not null)
            {
                // Rethrow with the original bind failure attached as the cause.
                throw new PortConflictException(McpService, port, address, McpPortKey, inner);
            }
        }
    }

    /// <summary>
    /// Turns the app's configured URLs into probe-able (address, port) pairs. Wildcard hosts
    /// ("+", "*", "0.0.0.0") probe <see cref="IPAddress.Any"/>; "localhost" probes loopback.
    /// </summary>
    private static IEnumerable<(IPAddress Address, int Port)> ConfiguredEndpoints(WebApplication app)
    {
        // The "urls" setting is where UseUrls / ASPNETCORE_URLS / --urls all end up. Read it
        // from configuration: app.Urls resolves the server, which isn't safe before start.
        string urls = app.Configuration[WebHostDefaults.ServerUrlsKey] ?? string.Empty;
        foreach (string url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Uri can't parse "+" or "*" as a host, so swap them for the any-address first.
            string normalized = url.Replace("://+", "://0.0.0.0").Replace("://*", "://0.0.0.0");
            if (!Uri.TryCreate(normalized, UriKind.Absolute, out Uri? uri))
                continue;

            IPAddress? address = uri.HostNameType switch
            {
                UriHostNameType.IPv4 or UriHostNameType.IPv6 => IPAddress.Parse(uri.Host.Trim('[', ']')),
                _ when uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) => IPAddress.Loopback,
                _ => null, // an arbitrary DNS name: leave it to the real bind (D1)
            };
            if (address is not null)
                yield return (address, uri.Port);
        }
    }
}
