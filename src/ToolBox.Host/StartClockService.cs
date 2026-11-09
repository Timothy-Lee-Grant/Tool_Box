using Microsoft.Extensions.Hosting;
using ToolBox.Core;

namespace ToolBox.Host;

/// <summary>
/// Forces <see cref="ServerInfoProvider"/> (a lazily created singleton) to be built when the host
/// starts, so its uptime clock measures the host's life rather than the time since the first
/// /health or server_info call.
/// </summary>
internal sealed class StartClockService(ServerInfoProvider info) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = info.Get();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
