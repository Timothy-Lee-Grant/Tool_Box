using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToolBox.Core.Desired;

/// <summary>
/// What the user WANTS (as opposed to what is running). Persisted as JSON by the desired-state store.
/// Core never names a specific toolset (ADR-003): ids are plain strings and settings are opaque JSON.
/// </summary>
public sealed class DesiredState
{
    /// <summary>The schema version this build reads and writes.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Toolset id → intent. Ids compare case-insensitively, like the catalog.</summary>
    public Dictionary<string, DesiredToolset> Toolsets
    {
        get => _toolsets;
        // System.Text.Json builds its own dictionary (case-sensitive); re-wrap so lookups stay case-insensitive.
        set => _toolsets = new(value ?? [], StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, DesiredToolset> _toolsets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Unknown top-level fields, kept so a save does not destroy data written by a newer build.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>One toolset's intent.</summary>
public sealed class DesiredToolset
{
    /// <summary><c>null</c> = "no opinion": lower configuration layers decide.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Opaque per-toolset settings. Core stores and round-trips them; it does not interpret them.</summary>
    public Dictionary<string, JsonElement>? Settings { get; set; }

    /// <summary>Unknown fields, preserved on save.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
