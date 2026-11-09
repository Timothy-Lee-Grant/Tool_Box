using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ToolBox.Core.Desired;

namespace ToolBox.Host;

/// <summary>
/// The layered answer to "which toolsets are on?" (issue #31, ADR-017).
/// <b>Desired</b> = what the user asked for (code default &lt; appsettings &lt; desired-state file).
/// <b>Effective</b> = what is composed (desired, unless TOOLBOX_TOOLSETS pins it).
/// </summary>
internal sealed record EnabledResolution(
    IReadOnlyList<string> Desired,
    IReadOnlyList<string> Effective,
    IReadOnlyDictionary<string, string> PinnedBy,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>> Settings,
    IReadOnlyList<string> UnknownIds)
{
    public const string EnvPin = "TOOLBOX_TOOLSETS";

    public static EnabledResolution Resolve(
        IConfiguration configuration, DesiredState desired, IEnumerable<string> knownIds)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(knownIds);

        var known = new HashSet<string>(knownIds, StringComparer.OrdinalIgnoreCase);

        // Layers 1-2: code default, replaced wholesale by the appsettings list if present.
        var section = configuration.GetSection(ToolBoxCatalog.SectionKey);
        List<string> desiredIds = section.Exists()
            ? section.GetChildren().Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).ToList()
            : [.. ToolBoxCatalog.DefaultEnabled];

        // Layer 3: the file overrides per id, only where it states an opinion.
        var unknown = new List<string>();
        var settings = new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, intent) in desired.Toolsets)
        {
            if (!known.Contains(id))
            {
                unknown.Add(id);
                continue;
            }

            if (intent.Settings is not null && intent.Settings.Count > 0)
            {
                settings[id] = intent.Settings;
            }

            if (intent.Enabled is bool on)
            {
                bool present = desiredIds.Contains(id, StringComparer.OrdinalIgnoreCase);
                if (on && !present)
                {
                    desiredIds.Add(id);
                }
                else if (!on && present)
                {
                    desiredIds.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        // Layer 4: the env var is authoritative for `enabled` on EVERY known id (named on, unnamed off).
        string? csv = configuration[ToolBoxCatalog.EnvKey];
        if (csv is null)
        {
            return new(desiredIds, desiredIds, new Dictionary<string, string>(), settings, unknown);
        }

        var effective = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var pinned = known.ToDictionary(id => id, _ => EnvPin, StringComparer.OrdinalIgnoreCase);
        return new(desiredIds, effective, pinned, settings, unknown);
    }
}
