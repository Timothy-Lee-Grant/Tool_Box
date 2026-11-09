using Microsoft.Extensions.DependencyInjection;

namespace ToolBox.Core;

/// <summary>
/// An immutable list of available toolsets. The Host builds one (it is the only place
/// allowed to name toolsets, ADR-003) and composes just the enabled entries.
/// Disabled entries' <see cref="ToolsetCatalogEntry.Register"/> is never invoked, so their
/// tools and hosted services never exist.
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
    /// Runs <see cref="ToolsetCatalogEntry.Register"/> for each enabled id, in the order given.
    /// Fails loudly on an unknown id, listing the valid ones. Ids are case-insensitive;
    /// repeated ids are registered once.
    /// </summary>
    /// <returns>
    /// For each composed entry id, the service descriptors its <c>Register</c> added. The Host uses
    /// this to learn which tools belong to which toolset (the gate's tool-to-toolset map).
    /// </returns>
    public IReadOnlyDictionary<string, IReadOnlyList<ServiceDescriptor>> Compose(
        IServiceCollection services, IEnumerable<string> enabledIds)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(enabledIds);

        var selected = new List<ToolsetCatalogEntry>();
        foreach (var id in enabledIds)
        {
            if (!_entries.TryGetValue(id?.Trim() ?? string.Empty, out var entry))
            {
                throw new InvalidOperationException(
                    $"unknown toolset id '{id}'. Valid ids: {string.Join(", ", _entries.Values.Select(e => e.Id))}.");
            }

            if (!selected.Contains(entry))
            {
                selected.Add(entry);
            }
        }

        var added = new Dictionary<string, IReadOnlyList<ServiceDescriptor>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in selected)
        {
            int before = services.Count;
            entry.Register(services);
            added[entry.Id] = services.Skip(before).ToList();
        }

        return added;
    }
}
