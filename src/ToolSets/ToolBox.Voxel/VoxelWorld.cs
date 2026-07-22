namespace ToolBox.Voxel;

/// <summary>
/// The build's state: which cells are occupied, with what material. A plain,
/// non-thread-safe singleton — one world, shared by whatever is currently connected.
/// Deliberately no more machinery than that (see Documentation/ImplementationPlans/003
/// §2.1.1/2.7): a working reference implementation this toolset is modeled on uses the
/// same plain in-process map, and a singleton only becomes inadequate once a real
/// multi-client demo needs otherwise — not before.
/// </summary>
public sealed class VoxelWorld
{
    private readonly Dictionary<VoxelCoordinate, string> _blocks = [];

    /// <summary>Raised once per mutating call — batched, not per-block, so a
    /// 2,000-cube sphere is one event, not 2,000.</summary>
    public event Action<VoxelChange>? Changed;

    public int Count => _blocks.Count;

    /// <summary>Sets (or overwrites) every given coordinate to <paramref name="material"/>.</summary>
    public void PlaceBlocks(IEnumerable<VoxelCoordinate> coordinates, string material)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        ArgumentException.ThrowIfNullOrWhiteSpace(material);

        List<PlacedVoxel> placed = [];
        foreach (VoxelCoordinate coordinate in coordinates)
        {
            _blocks[coordinate] = material;
            placed.Add(new PlacedVoxel(coordinate, material));
        }

        if (placed.Count > 0)
        {
            Changed?.Invoke(new VoxelChange.Placed(placed));
        }
    }

    public void PlaceBlock(VoxelCoordinate coordinate, string material) =>
        PlaceBlocks([coordinate], material);

    /// <summary>
    /// Removes every given coordinate that is actually occupied. Returns only the
    /// coordinates that were really removed, not the ones that were already empty —
    /// callers use the count to report an honest "removed N blocks".
    /// </summary>
    public IReadOnlyList<VoxelCoordinate> RemoveBlocks(IEnumerable<VoxelCoordinate> coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);

        List<VoxelCoordinate> removed = [];
        foreach (VoxelCoordinate coordinate in coordinates)
        {
            if (_blocks.Remove(coordinate))
            {
                removed.Add(coordinate);
            }
        }

        if (removed.Count > 0)
        {
            Changed?.Invoke(new VoxelChange.Removed(removed));
        }

        return removed;
    }

    public bool RemoveBlock(VoxelCoordinate coordinate) =>
        RemoveBlocks([coordinate]).Count > 0;

    public void Clear()
    {
        if (_blocks.Count == 0)
        {
            return;
        }

        _blocks.Clear();
        Changed?.Invoke(new VoxelChange.Cleared());
    }

    /// <summary>
    /// Reflects every currently-occupied cell across the given axis/plane, skipping
    /// any target cell that's already occupied. Iterates a snapshot of the pre-mirror
    /// state but checks/writes the live map as it goes — matches the semantics of the
    /// reference implementation this is ported from (Documentation/ImplementationPlans/003 §2.7).
    /// </summary>
    public IReadOnlyList<PlacedVoxel> MirrorAcross(VoxelAxis axis, int plane)
    {
        KeyValuePair<VoxelCoordinate, string>[] originals = [.. _blocks];
        List<PlacedVoxel> placed = [];

        foreach ((VoxelCoordinate original, string material) in originals)
        {
            VoxelCoordinate reflected = axis == VoxelAxis.X
                ? original with { X = (2 * plane) - original.X }
                : original with { Z = (2 * plane) - original.Z };

            if (_blocks.ContainsKey(reflected))
            {
                continue;
            }

            _blocks[reflected] = material;
            placed.Add(new PlacedVoxel(reflected, material));
        }

        if (placed.Count > 0)
        {
            Changed?.Invoke(new VoxelChange.Placed(placed));
        }

        return placed;
    }

    /// <summary>Every occupied cell, for a viewer's on-connect sync (plan 003, Step 4)
    /// and for <c>describe_world</c>.</summary>
    public IReadOnlyList<PlacedVoxel> Snapshot() =>
        [.. _blocks.Select(kv => new PlacedVoxel(kv.Key, kv.Value))];

    /// <summary>Null when the world is empty — there is no meaningful bounding box of
    /// nothing, and a zeroed struct would silently lie about that.</summary>
    public VoxelBounds? BoundingBox()
    {
        if (_blocks.Count == 0)
        {
            return null;
        }

        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;
        int minZ = int.MaxValue, maxZ = int.MinValue;

        foreach (VoxelCoordinate c in _blocks.Keys)
        {
            if (c.X < minX) minX = c.X;
            if (c.X > maxX) maxX = c.X;
            if (c.Y < minY) minY = c.Y;
            if (c.Y > maxY) maxY = c.Y;
            if (c.Z < minZ) minZ = c.Z;
            if (c.Z > maxZ) maxZ = c.Z;
        }

        return new VoxelBounds(minX, maxX, minY, maxY, minZ, maxZ);
    }
}
