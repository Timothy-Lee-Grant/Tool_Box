using System.Collections.Concurrent;

namespace ToolBox.Core.Toolsets;

/// <summary>
/// The single choke point for "is this toolset on?". Every surface (tools/list, tools/call,
/// server info, companion services) asks <see cref="IsOn"/>; nothing checks per tool.
/// Default is closed. Only the reconciler flips it, so <see cref="Open"/> and <see cref="Close"/>
/// are internal: one writer makes "what is on" easy to reason about.
/// </summary>
public sealed class ToolsetGate
{
    private readonly ConcurrentDictionary<string, byte> _open = new(StringComparer.OrdinalIgnoreCase);

    public bool IsOn(string toolsetId) => _open.ContainsKey(toolsetId);

    /// <summary>A sorted copy of the open ids; later changes never affect it.</summary>
    public IReadOnlyList<string> Snapshot() =>
        _open.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>The message a disabled tool call returns. Deliberately not "tool not found".</summary>
    public static string OffMessage(string toolsetId) => $"toolset '{toolsetId}' is off";

    internal void Open(string toolsetId) => _open[toolsetId] = 0;

    internal void Close(string toolsetId) => _open.TryRemove(toolsetId, out _);
}
