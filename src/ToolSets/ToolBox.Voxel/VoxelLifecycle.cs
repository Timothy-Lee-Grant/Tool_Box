using ToolBox.Core.Toolsets;

namespace ToolBox.Voxel;

/// <summary>
/// What "on" and "off" mean for Voxel (#32): On binds the viewer port, Off releases it.
/// The <see cref="VoxelWorld"/> is never touched, so turning Voxel off and on keeps the build
/// (in memory for now; disk persistence is S3). An occupied port does not fail the toolset:
/// the viewer degrades visibly under <c>components.viewer</c> (ADR-016) and the tools keep working.
/// </summary>
public sealed class VoxelLifecycle(VoxelViewerBroadcastService viewer) : IToolsetLifecycle
{
    public string ToolsetId => "voxel";

    public Task StartAsync(CancellationToken cancellationToken) => viewer.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => viewer.StopAsync(cancellationToken);
}
