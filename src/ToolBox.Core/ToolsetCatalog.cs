using Microsoft.Extensions.DependencyInjection;

namespace ToolBox.Core;

/// <summary>
/// An immutable list of available toolsets. The Host builds one (it is the only place
/// allowed to name toolsets, ADR-003). The Host calls <see cref="ComposeAll"/>: every
/// toolset is registered (cheap, side-effect-free) and the reconciler decides which are started
/// and visible (ADR-018).
/// </summary>
public sealed class ToolsetCatalog
{
    private readonly Dictionary<string, ToolsetCatalogEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public ToolsetCatalog(IEnumerable<ToolsetCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        foreach (var entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.Id);
            ArgumentNullException.ThrowIfNull(entry.Register);

            if (!_entries.TryAdd(entry.Id, entry))
            {
                throw new ArgumentException($"duplicate toolset id '{entry.Id}' in the catalog.", nameof(entries));
            }
        }
    }

    /// <summary>All entries, in the order they were supplied.</summary>
    public IReadOnlyList<ToolsetCatalogEntry> Entries => _entries.Values.ToList();

    /// <summary>
    /// Throws if any id is not in the catalog, listing the valid ids (ADR-015's fail-loud rule,
    /// kept now that every toolset is registered). Ids are case-insensitive.
    /// </summary>
    public void ValidateIds(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        foreach (var id in ids) Resolve(id);
    }

    /// <summary>
    /// Registers EVERY entry. A toolset must not register a raw IHostedService (the container
    /// would start it at boot and nothing could turn it off); its background work belongs in an
    /// <c>IToolsetLifecycle</c> the reconciler starts and stops. Throws if one does.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ServiceDescriptor>> ComposeAll(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var added = new Dictionary<string, IReadOnlyList<ServiceDescriptor>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _entries.Values)
        {
            int before = services.Count;
            entry.Register(services);
            var descriptors = services.Skip(before).ToList();
            if (descriptors.Any(d => d.ServiceType.FullName == "Microsoft.Extensions.Hosting.IHostedService"))
            {
                throw new InvalidOperationException(
                    $"toolset '{entry.Id}' registers a raw IHostedService; use an IToolsetLifecycle so it can be stopped.");
            }

            added[entry.Id] = descriptors;
        }

        return added;
    }

    private ToolsetCatalogEntry Resolve(string? id) =>
        _entries.TryGetValue(id?.Trim() ?? string.Empty, out var entry)
            ? entry
            : throw new InvalidOperationException(
                $"unknown toolset id '{id}'. Valid ids: {string.Join(", ", _entries.Values.Select(e => e.Id))}.");
}
