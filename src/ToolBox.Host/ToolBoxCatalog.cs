using Microsoft.Extensions.Configuration;
using ToolBox.Basics;
using ToolBox.Core;
using ToolBox.Embedded;
using ToolBox.Voxel;

namespace ToolBox.Host;

/// <summary>
/// The ONLY place toolsets are named (ADR-003: Core never references a toolset).
/// Adding a toolset = one project reference + one line in <see cref="Create"/>.
/// </summary>
internal static class ToolBoxCatalog
{
    /// <summary>Config key for the comma-separated form (env var TOOLBOX_TOOLSETS).</summary>
    public const string EnvKey = "TOOLSETS";

    /// <summary>Config section for the appsettings list form.</summary>
    public const string SectionKey = "Toolsets:Enabled";

    /// <summary>Behaviour when nothing is configured: identical to before the catalog existed.</summary>
    public static readonly string[] DefaultEnabled = ["basics", "voxel"];

    public static ToolsetCatalog Create() => new(
    [
        BasicsToolsetExtensions.CatalogEntry,
        VoxelToolsetExtensions.CatalogEntry,
        EmbeddedToolsetExtensions.CatalogEntry,
    ]);

    /// <summary>
    /// Precedence, last wins: code default &lt; appsettings "Toolsets:Enabled" &lt;
    /// TOOLBOX_TOOLSETS env var (comma-separated) &lt; in-memory/test overrides.
    /// The env var is its own key rather than "Toolsets__Enabled__0" because array
    /// config merges by index, so an env var could only partially override the JSON list.
    /// An explicitly empty value enables nothing.
    /// </summary>
    public static IReadOnlyList<string> ResolveEnabled(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string? csv = configuration[EnvKey];
        if (csv is not null)
        {
            return Split(csv);
        }

        var section = configuration.GetSection(SectionKey);
        if (section.Exists())
        {
            return section.GetChildren()
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim())
                .ToList();
        }

        return DefaultEnabled;
    }

    private static string[] Split(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
