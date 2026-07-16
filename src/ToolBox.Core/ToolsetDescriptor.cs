namespace ToolBox.Core;

/// <summary>
/// Identity card for a loaded toolset. Each toolset registers exactly one of these
/// (via <c>AddToolsetDescriptor</c>) so the platform can report what capabilities
/// are active — consumed by <see cref="ServerInfoProvider"/> and, later, by
/// config-driven loading (plan 003).
/// </summary>
/// <param name="Name">Short unique name, e.g. "Basics".</param>
/// <param name="Description">One sentence: what capability this toolset provides.</param>
public sealed record ToolsetDescriptor(string Name, string Description);
