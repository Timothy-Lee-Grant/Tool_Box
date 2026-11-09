using System.Text.Json.Nodes;

namespace ToolBox.Core.Desired;

public enum MigrationOutcome
{
    /// <summary>The document is at the current version (possibly after migrating).</summary>
    Ok,
    /// <summary>The version is missing, not an integer, or below 1.</summary>
    Corrupt,
    /// <summary>The document was written by a newer build. Read-only: never overwrite it.</summary>
    Unsupported,
}

/// <param name="Outcome">What happened.</param>
/// <param name="Document">The migrated document when Outcome is Ok; otherwise null.</param>
/// <param name="FoundVersion">The version in the file, when one could be read.</param>
/// <param name="Reason">Human-readable explanation when not Ok.</param>
public sealed record MigrationResult(MigrationOutcome Outcome, JsonObject? Document, int? FoundVersion, string? Reason);

/// <summary>
/// Upgrades an older desired-state document to the current schema, one version at a time
/// (v1→v2, v2→v3, …). Pure: no file I/O, so it can be tested alone. The table is empty at v1.
/// </summary>
public sealed class DesiredStateMigrator
{
    private readonly int _current;
    private readonly IReadOnlyDictionary<int, Func<JsonObject, JsonObject>> _steps;

    /// <param name="currentVersion">The version this build understands.</param>
    /// <param name="steps">Key n = the step that converts a version-n document into version n+1.</param>
    public DesiredStateMigrator(
        int currentVersion = DesiredState.CurrentSchemaVersion,
        IReadOnlyDictionary<int, Func<JsonObject, JsonObject>>? steps = null)
    {
        _current = currentVersion;
        _steps = steps ?? new Dictionary<int, Func<JsonObject, JsonObject>>();
    }

    public MigrationResult Migrate(JsonNode? node)
    {
        if (node is not JsonObject doc)
        {
            return Corrupt(null, "the file's root is not a JSON object.");
        }

        int version;
        try
        {
            if (!doc.TryGetPropertyValue("schemaVersion", out var v) || v is null)
            {
                return Corrupt(null, "schemaVersion is missing.");
            }
            version = v.GetValue<int>(); // throws for strings, fractions, out-of-range numbers
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return Corrupt(null, "schemaVersion is not an integer.");
        }

        if (version < 1)
        {
            return Corrupt(version, $"schemaVersion {version} is invalid (must be 1 or higher).");
        }

        if (version > _current)
        {
            return new MigrationResult(MigrationOutcome.Unsupported, null, version,
                $"schemaVersion {version} is newer than this build understands ({_current}).");
        }

        while (version < _current)
        {
            if (!_steps.TryGetValue(version, out var step))
            {
                return Corrupt(version, $"no migration from schemaVersion {version}.");
            }
            doc = step(doc);
            version++;
            doc["schemaVersion"] = version;
        }

        return new MigrationResult(MigrationOutcome.Ok, doc, version, null);
    }

    private static MigrationResult Corrupt(int? found, string reason) =>
        new(MigrationOutcome.Corrupt, null, found, reason);
}
