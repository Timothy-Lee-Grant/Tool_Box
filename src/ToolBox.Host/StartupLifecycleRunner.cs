using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ToolBox.Core.Toolsets;

namespace ToolBox.Host;

/// <summary>
/// INTERIM BRIDGE (#32 step 4; deleted in step 6). Toolsets are still composed at startup
/// (only enabled ones are registered), so this starts every registered lifecycle at boot and
/// stops them at shutdown, keeping behaviour identical to before. Step 6 replaces it with the
/// reconciler. A failing lifecycle is logged and does not stop the others or the host.
/// </summary>
internal sealed class StartupLifecycleRunner(
    IEnumerable<IToolsetLifecycle> lifecycles,
    ILogger<StartupLifecycleRunner> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (IToolsetLifecycle lifecycle in lifecycles)
        {
            try
            {
                await lifecycle.StartAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Toolset '{Toolset}' failed to start: {Reason}", lifecycle.ToolsetId, ex.Message);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (IToolsetLifecycle lifecycle in lifecycles.Reverse())
        {
            try
            {
                await lifecycle.StopAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Toolset '{Toolset}' failed to stop: {Reason}", lifecycle.ToolsetId, ex.Message);
            }
        }
    }
}
