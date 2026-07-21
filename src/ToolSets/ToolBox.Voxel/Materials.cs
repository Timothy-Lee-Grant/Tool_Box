namespace ToolBox.Voxel;

/// <summary>
/// v1's fixed material palette — a deliberate reduction from the reference
/// implementation's 100 (Documentation/ImplementationPlans/003 §3.1 /2.7). A dozen
/// names already covers walls, roofs, glazing, and a few accents; expand later if a
/// build actually needs more — abstract from evidence, not imagination.
/// </summary>
public static class Materials
{
    public static readonly IReadOnlyList<string> All =
    [
        "stone", "brick", "wood", "glass", "gold", "iron",
        "grass", "sand", "water", "snow", "lava", "obsidian",
    ];

    private static readonly HashSet<string> Known = new(All, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Null if <paramref name="material"/> is known; otherwise a short, actionable
    /// error string — never an exception. An unrecognized material name from the
    /// agent is an expected, recoverable input, not a bug to throw over.
    /// </summary>
    public static string? Validate(string material)
    {
        if (Known.Contains(material))
        {
            return null;
        }

        string[] near =
        [
            .. All.Where(m =>
                m.Contains(material, StringComparison.OrdinalIgnoreCase) ||
                material.Contains(m, StringComparison.OrdinalIgnoreCase)),
        ];

        string hint = near.Length > 0 ? $" Did you mean: {string.Join(", ", near)}?" : string.Empty;
        return $"Unknown material \"{material}\".{hint} Call list_materials for the full list.";
    }
}
