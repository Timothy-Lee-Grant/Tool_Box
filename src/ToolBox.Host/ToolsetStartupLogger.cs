using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ToolBox.Host;

/// <summary>
/// Logs which toolsets this process composed, once at startup. Goes through ILogger, so it
/// lands on stderr (ADR-004: stdout is reserved for the stdio JSON-RPC protocol).
/// </summary>
internal sealed class ToolsetStartupLogger(IReadOnlyList<string> enabledIds, ILogger<ToolsetStartupLogger> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Enabled toolsets: {Toolsets}",
            enabledIds.Count == 0 ? "(none)" : string.Join(", ", enabledIds));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
