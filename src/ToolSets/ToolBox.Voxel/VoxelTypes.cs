namespace ToolBox.Voxel;

/// <summary>
/// A single grid cell. Coordinates are integers — the world is a voxel grid, not a
/// continuous space; a rasterizer's whole job is turning continuous shape parameters
/// (a radius, a taper) into a set of these.
/// </summary>
public readonly record struct VoxelCoordinate(int X, int Y, int Z);

/// <summary>A coordinate with the material occupying it — the unit <see cref="VoxelWorld.Snapshot"/>
/// and the future viewer broadcast (plan 003, Step 4) deal in.</summary>
public sealed record PlacedVoxel(VoxelCoordinate Coordinate, string Material);

/// <summary>Inclusive axis-aligned bounds of every occupied cell.</summary>
public readonly record struct VoxelBounds(int MinX, int MaxX, int MinY, int MaxY, int MinZ, int MaxZ);

/// <summary>
/// Mirroring is restricted to the two ground-plane axes — mirroring across Y
/// (up/down) never makes sense for a build standing on ground level.
/// </summary>
public enum VoxelAxis
{
    X,
    Z,
}

/// <summary>
/// What changed in a <see cref="VoxelWorld"/>, shaped for the viewer broadcast
/// (plan 003, Step 4) to turn into a wire message. One event per bulk tool call,
/// not one per block — a 2,000-cube sphere is one message, not 2,000.
/// </summary>
public abstract record VoxelChange
{
    private VoxelChange() { }

    public sealed record Placed(IReadOnlyList<PlacedVoxel> Blocks) : VoxelChange;

    public sealed record Removed(IReadOnlyList<VoxelCoordinate> Coordinates) : VoxelChange;

    public sealed record Cleared : VoxelChange;
}
