using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ToolBox.Core.DependencyInjection;
using ToolBox.Core.Desired;
using ToolBox.Core.Health;

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

        // Created first so AddToolBoxCore's TryAdd keeps this instance and we can report to it now.
        var registry = new ComponentStatusRegistry();
        services.AddSingleton(registry);
        services.AddToolBoxCore();

        // Desired-state file (issue #31): read once; the host never creates or writes it.
        // Path: Desired:Path (env TOOLBOX_Desired__Path), else LocalApplicationData/ToolBox/desired-state.json.
        string? configuredPath = configuration["Desired:Path"];
        var store = new DesiredStateStore(string.IsNullOrWhiteSpace(configuredPath) ? null : configuredPath);
        var loaded = store.Load();
        if (loaded.Status == DesiredStateLoadStatus.Corrupt)
        {
            registry.Set("desiredState", $"corrupt: {loaded.Reason}");
            Console.Error.WriteLine($"desired-state file {loaded.Path} is corrupt ({loaded.Reason}); using defaults.");
        }
        else if (loaded.Status == DesiredStateLoadStatus.Unsupported)
        {
            registry.Set("desiredState",
                $"unsupported: schemaVersion {loaded.FoundVersion} (this build understands {DesiredState.CurrentSchemaVersion})");
            Console.Error.WriteLine($"desired-state file {loaded.Path} is from a newer build; ignoring it and leaving it untouched.");
        }

        var catalog = ToolBoxCatalog.Create();
        var resolution = EnabledResolution.Resolve(configuration, loaded.State, catalog.Entries.Select(e => e.Id));
        foreach (var id in resolution.UnknownIds)
        {
            Console.Error.WriteLine($"desired-state file mentions unknown toolset '{id}'; ignoring it (kept in the file).");
        }
        services.AddSingleton(store);
        services.AddSingleton(resolution);

        // Only enabled entries are invoked, so a disabled toolset's tools and hosted
        // services are never registered. Unknown ids in env/appsettings still throw (fail loud).
        var enabled = resolution.Effective;
        var composed = catalog.Compose(services, enabled);
        services.AddSingleton(ToolOwners.FromDescriptors(composed));

        // Interim (#32 step 5): the gate starts open for exactly what was composed, so behaviour
        // is unchanged. Step 6 removes this; the reconciler becomes the only writer.
        var gate = new ToolBox.Core.Toolsets.ToolsetGate();
        foreach (var id in composed.Keys) gate.Open(id);
        services.AddSingleton(gate);

        // Interim bridge (#32 step 4): starts the lifecycles of the toolsets composed above.
        services.AddHostedService<StartupLifecycleRunner>();

        // Startup line on stderr (ADR-004) naming what this process actually composed.
        var ids = enabled.Select(e => e.ToLowerInvariant()).Distinct().ToArray();
        services.AddHostedService(sp => new ToolsetStartupLogger(
            ids, sp.GetRequiredService<ILogger<ToolsetStartupLogger>>()));

        // Idempotent (proved by the AddMcpServer_CalledTwice_IsSafe test); this call also
        // guarantees a builder for the transport even when no toolset is enabled.
        return services.AddMcpServer().WithToolsetGateFilters();
    }
}
