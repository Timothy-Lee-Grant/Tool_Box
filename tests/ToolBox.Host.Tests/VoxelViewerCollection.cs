namespace ToolBox.Host.Tests;

/// <summary>
/// The voxel viewer binds a fixed port (8090, falling back to 8091-8093), and xUnit runs
/// test classes in parallel. Every class that STARTS the voxel toolset joins this
/// collection so those classes run one at a time.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class VoxelViewerCollection
{
    public const string Name = "VoxelViewer";
}
