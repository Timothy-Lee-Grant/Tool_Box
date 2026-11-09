using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ToolBox.Core.DependencyInjection;
using ToolBox.Core.Desired;
using ToolBox.Core.Health;
using ToolBox.Core.Toolsets;

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

        // Fail loud on an unknown id in env/appsettings (ADR-015 rule, kept), then register EVERY
        // toolset: registration is cheap and side-effect-free, and the reconciler decides what is
        // started and visible (#32). Nothing runs until the reconciler starts it.
        catalog.ValidateIds(resolution.Effective);
        var composed = catalog.ComposeAll(services);
        services.AddSingleton(ToolOwners.FromDescriptors(composed));

        // The gate (registered by AddToolBoxCore) starts closed; only the reconciler opens it.
        var knownIds = catalog.Entries.Select(e => e.Id).ToArray();
        services.AddSingleton<ToolsetStatusBoard>();
        services.AddSingleton(sp => new ToolsetReconciler(
            knownIds,
            sp.GetServices<IToolsetLifecycle>(),
            sp.GetRequiredService<ToolsetGate>(),
            sp.GetRequiredService<ToolsetStatusBoard>(),
            TimeProvider.System));

        // Reconciles to the effective roster once at startup (and to nothing at shutdown).
        services.AddHostedService<ToolsetReconcilerService>();

        // Idempotent (proved by the AddMcpServer_CalledTwice_IsSafe test); this call also
        // guarantees a builder for the transport even when no toolset is enabled.
        return services.AddMcpServer().WithToolsetGateFilters();
    }
}
