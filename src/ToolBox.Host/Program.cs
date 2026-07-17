using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ToolBox.Core;
using ToolBox.Core.Logging;
using ToolBox.Host;

// ToolBox.Host — thin composition root.
// The Host selects its transport at startup, then hands off to a per-transport path.
// Everything the server IS lives in AddToolBoxServer(); everything below is only
// "which wire, and how to fail loudly".

// ---- Transport selection (bootstrap config, before any host is built) ----
// Precedence (last source added wins): appsettings.json < TOOLBOX_* env var < --transport flag.
// BaseDirectory, not cwd: clients like Claude Desktop launch this DLL from an arbitrary
// working directory, so relative paths would silently miss appsettings.json.
var bootstrap = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables(prefix: "TOOLBOX_")
    .AddCommandLine(args, new Dictionary<string, string> { ["--transport"] = "Transport" })
    .Build();

string transport = bootstrap["Transport"]?.Trim().ToLowerInvariant() ?? "stdio";

switch (transport)
{
    case "stdio":
        await RunStdioAsync(args);
        return 0;

    case "http":
        await RunHttpAsync(args);
        return 0;

    default:
        // Fail fast and loud — never guess a transport. (Error text goes to stderr,
        // like everything else that isn't protocol.)
        Console.Error.WriteLine($"Unknown transport '{transport}'. Valid values: stdio, http.");
        return 2;
}

static async Task RunStdioAsync(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    // ADR-004: stdout belongs to the JSON-RPC protocol; all logs go to stderr.
    // Stays FIRST so no later registration can sneak a stdout logger in.
    builder.Logging.UseStderrOnly();

    builder.Services
        .AddToolBoxServer()
        .WithStdioServerTransport();

    await builder.Build().RunAsync();
}

static async Task RunHttpAsync(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);

    // Stage 2 decision 3: the stderr rule stays uniform across transports.
    // HTTP mode doesn't need it (stdout is harmless here), but a per-transport
    // logging exception is a trap for the next stdio change.
    builder.Logging.UseStderrOnly();

    // Default port 8080 unless the environment says otherwise
    // (containers set ASPNETCORE_URLS=http://0.0.0.0:8080; plan 002 Step 4).
    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    {
        builder.WebHost.UseUrls("http://localhost:8080");
    }

    builder.Services
        .AddToolBoxServer()
        .WithHttpTransport();

    var app = builder.Build();

    // The MCP endpoint (streamable HTTP).
    app.MapMcp("/mcp");

    // Plain HTTP healthcheck — curl-shaped on purpose, for compose/orchestrator probes
    // (Stage 2 decision 7). Not an MCP call: health must be checkable by tools that
    // have never heard of MCP.
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

    await app.RunAsync();
}
