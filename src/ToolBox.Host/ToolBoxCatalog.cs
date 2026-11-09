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

        // No desired-state file: identical to the pre-#31 behaviour. The full layering
        // (including the file) is EnabledResolution.Resolve.
        return EnabledResolution.Resolve(configuration, new ToolBox.Core.Desired.DesiredState(),
            Create().Entries.Select(e => e.Id)).Effective;
    }
}
