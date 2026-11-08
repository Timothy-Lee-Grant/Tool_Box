using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ToolBox.Core.DependencyInjection;

namespace ToolBox.Host;

/// <summary>
/// The single definition of "what this server is" (plan 002, Step 1.1).
/// Both transport paths — stdio and HTTP — call this and then attach their transport.
/// Which toolsets exist is data (<see cref="ToolBoxCatalog"/>) plus configuration
/// (<c>TOOLBOX_TOOLSETS</c> / <c>Toolsets:Enabled</c>), not a hard-coded chain (ADR-015).
/// </summary>
internal static class ToolBoxServerComposition
{
    public static IMcpServerBuilder AddToolBoxServer(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddToolBoxCore();

        // Only enabled entries are invoked, so a disabled toolset's tools and hosted
        // services are never registered. Unknown ids throw (fail loud, like the transport check).
        var enabled = ToolBoxCatalog.ResolveEnabled(configuration);
        ToolBoxCatalog.Create().Compose(services, enabled);

        // Startup line on stderr (ADR-004) naming what this process actually composed.
        var ids = enabled.Select(e => e.ToLowerInvariant()).Distinct().ToArray();
        services.AddHostedService(sp => new ToolsetStartupLogger(
            ids, sp.GetRequiredService<ILogger<ToolsetStartupLogger>>()));

        // Idempotent (proved by the AddMcpServer_CalledTwice_IsSafe test); this call also
        // guarantees a builder for the transport even when no toolset is enabled.
        return services.AddMcpServer();
    }
}
