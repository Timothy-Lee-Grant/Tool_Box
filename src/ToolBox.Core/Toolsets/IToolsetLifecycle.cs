namespace ToolBox.Core.Toolsets;

/// <summary>
/// The side effects of turning one toolset on or off (open a port, look up firmware, ...).
/// Registration stays cheap and side-effect-free; everything with a side effect happens
/// here, so the reconciler can start and stop a toolset without restarting the process.
/// Core names no toolset (ADR-003): each toolset library supplies its own implementation.
/// A toolset with nothing to start (Basics) needs no lifecycle; the gate alone is enough.
/// </summary>
public interface IToolsetLifecycle
{
    /// <summary>The catalog id this lifecycle belongs to, e.g. "voxel".</summary>
    string ToolsetId { get; }

    /// <summary>Acquire resources. Throwing marks the toolset Failed with the exception message.</summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>Release resources. Must keep in-memory state such as the Voxel world.</summary>
    Task StopAsync(CancellationToken cancellationToken);
}
