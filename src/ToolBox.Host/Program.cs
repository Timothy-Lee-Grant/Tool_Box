using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ToolBox.Core.DependencyInjection;
using ToolBox.Core.Logging;

// ToolBox.Host — thin composition root (plan 001, Step 3).
// Knows no domains, contains no logic: configure logging, register Core,
// compose toolsets (Step 4), start the transport. That's the whole job.

var builder = Host.CreateApplicationBuilder(args);

// ADR-004: stdout belongs to the JSON-RPC protocol; all logs go to stderr.
// This line must stay FIRST so no later registration can sneak a stdout logger in.
builder.Logging.UseStderrOnly();

builder.Services.AddToolBoxCore();

// Toolsets compose here (Step 4 adds: builder.Services.AddBasicsToolset();)

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport();

await builder.Build().RunAsync();
