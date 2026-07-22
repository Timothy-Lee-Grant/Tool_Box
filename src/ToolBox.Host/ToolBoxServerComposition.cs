using Microsoft.Extensions.DependencyInjection;
using ToolBox.Basics;
using ToolBox.Core.DependencyInjection;

namespace ToolBox.Host;

/// <summary>
/// The single definition of "what this server is" (plan 002, Step 1.1).
/// Both transport paths — stdio today, HTTP in Step 2 — call this and then attach
/// their transport. If the toolset list ever appears twice in this codebase,
/// the two transports have started drifting apart; that's the smell to watch for.
/// </summary>
internal static class ToolBoxServerComposition
{
    public static IMcpServerBuilder AddToolBoxServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddToolBoxCore();

        return services
            .AddMcpServer()
            // Toolsets compose below — one line each (plan 001, ADR-005).
            .AddBasicsToolset();
    }
}
