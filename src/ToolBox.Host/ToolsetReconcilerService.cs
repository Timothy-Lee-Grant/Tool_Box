using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ToolBox.Core;
using ToolBox.Core.Toolsets;

namespace ToolBox.Host;

/// <summary>
/// The Host's thin driver for <see cref="ToolsetReconciler"/> (ADR-010 hosted service).
/// At startup it reconciles to the effective roster (<see cref="EnabledResolution"/>, #31);
/// at shutdown it reconciles to nothing so every lifecycle is stopped. Logs the outcome on
/// stderr (ADR-004). Triggers for file change and manual request arrive in step 7.
/// </summary>
internal sealed class ToolsetReconcilerService(
    ToolsetReconciler reconciler,
    EnabledResolution resolution,
    ILogger<ToolsetReconcilerService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var result = await reconciler.ReconcileAsync(resolution.Effective, cancellationToken);
        var on = resolution.Effective
            .Where(id => !result.Failed.ContainsKey(id))
            .Select(id => id.ToLowerInvariant()).Distinct().ToArray();
        logger.LogInformation("Enabled toolsets: {Toolsets}", on.Length == 0 ? "(none)" : string.Join(", ", on));
        foreach (var (id, reason) in result.Failed)
        {
            logger.LogError("Toolset '{Toolset}' failed to start: {Reason}", id, reason);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var result = await reconciler.ReconcileAsync([], cancellationToken);
        foreach (var (id, reason) in result.Failed)
        {
            logger.LogError("Toolset '{Toolset}' failed to stop: {Reason}", id, reason);
        }
    }
}
