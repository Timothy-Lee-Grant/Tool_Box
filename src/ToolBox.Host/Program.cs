using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ToolBox.Basics;
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

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    // Toolsets compose below — one line each, and the Host learns nothing
    // about what's inside them.
    .AddBasicsToolset();

await builder.Build().RunAsync();
