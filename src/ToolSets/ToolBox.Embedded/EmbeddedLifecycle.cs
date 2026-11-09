using Microsoft.Extensions.DependencyInjection;
using ToolBox.Core.Toolsets;

namespace ToolBox.Embedded;

/// <summary>
/// Starting Embedded means locating the firmware directory (a repo-root walk). That lookup
/// used to run at registration and crash the host when no checkout was present; now it runs
/// here, so the failure is reported as this toolset being Failed with a reason (#32).
/// Stopping has nothing to release.
/// </summary>
public sealed class EmbeddedLifecycle(IServiceProvider services) : IToolsetLifecycle
{
    public string ToolsetId => "embedded";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Throws InvalidOperationException("could not locate the repo root ...") when absent.
        // A throwing singleton factory is not cached, so a later retry can recover.
        _ = services.GetRequiredService<FirmwareDirectory>();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
