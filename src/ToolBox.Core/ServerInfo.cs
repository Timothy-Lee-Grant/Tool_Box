namespace ToolBox.Core;

/// <summary>
/// Snapshot of the running server, suitable for returning directly from a tool
/// (System.Text.Json serializes it cleanly, including the TimeSpan).
/// </summary>
/// <param name="Version">Assembly version of the platform.</param>
/// <param name="Toolsets">Names of the toolsets loaded in this process.</param>
/// <param name="Uptime">Time since the server info provider was constructed (process start, in practice).</param>
public sealed record ServerInfo(
    string Version,
    IReadOnlyList<string> Toolsets,
    TimeSpan Uptime);
