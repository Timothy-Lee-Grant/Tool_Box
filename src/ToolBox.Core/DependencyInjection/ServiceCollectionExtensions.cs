using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ToolBox.Core.DependencyInjection;

/// <summary>
/// The toolset registration convention (plan 001, Step 2.4). The Host composes
/// capabilities only through these calls — it never news up domain services itself.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Core's shared services. Call once from the Host, before any toolset.
    /// <c>TryAdd</c> keeps this idempotent and lets tests pre-register fakes
    /// (e.g. a fixed <see cref="TimeProvider"/>).
    /// </summary>
    public static IServiceCollection AddToolBoxCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ServerInfoProvider>();
        return services;
    }

    /// <summary>
    /// Declares a toolset's identity. Each toolset's <c>Add*Toolset()</c> extension
    /// calls this exactly once. Descriptors are plain singletons, so consumers simply
    /// inject <c>IEnumerable&lt;ToolsetDescriptor&gt;</c> — no mutable registry needed.
    /// </summary>
    public static IServiceCollection AddToolsetDescriptor(
        this IServiceCollection services, string name, string description)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        services.AddSingleton(new ToolsetDescriptor(name, description));
        return services;
    }
}
