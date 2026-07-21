using ToolBox.Voxel;

namespace ToolBox.Voxel.Tests;

/// <summary>
/// Proves the state/eventing contract Step 4's viewer broadcast will depend on:
/// one <see cref="VoxelChange"/> per mutating call (never per block), honest
/// "what actually changed" reporting, and a null bounding box for an empty world
/// rather than a zeroed one that would silently lie about it.
/// </summary>
public class VoxelWorldTests
{
    [Fact]
    public void NewWorld_IsEmpty()
    {
        var world = new VoxelWorld();

        Assert.Equal(0, world.Count);
        Assert.Empty(world.Snapshot());
        Assert.Null(world.BoundingBox());
    }

    [Fact]
    public void PlaceBlocks_AddsEveryCoordinateWithTheGivenMaterial()
    {
        var world = new VoxelWorld();
        VoxelCoordinate[] coords = [new(0, 0, 0), new(1, 0, 0), new(2, 0, 0)];

        world.PlaceBlocks(coords, "stone");

        Assert.Equal(3, world.Count);
        Assert.All(world.Snapshot(), block => Assert.Equal("stone", block.Material));
    }

    [Fact]
    public void PlaceBlocks_Overwrites_WhenTheSameCoordinateIsPlacedAgain()
    {
        var world = new VoxelWorld();
        var coordinate = new VoxelCoordinate(0, 0, 0);

        world.PlaceBlock(coordinate, "stone");
        world.PlaceBlock(coordinate, "gold");

        Assert.Equal(1, world.Count);
        Assert.Equal("gold", world.Snapshot().Single().Material);
    }

    [Fact]
    public void PlaceBlocks_RaisesOneChangedEvent_NotOnePerBlock()
    {
        var world = new VoxelWorld();
        var raised = new List<VoxelChange>();
        world.Changed += raised.Add;

        world.PlaceBlocks([new(0, 0, 0), new(1, 0, 0), new(2, 0, 0)], "stone");

        VoxelChange change = Assert.Single(raised);
        var placed = Assert.IsType<VoxelChange.Placed>(change);
        Assert.Equal(3, placed.Blocks.Count);
    }

    [Fact]
    public void PlaceBlocks_WithNoCoordinates_RaisesNoEvent()
    {
        var world = new VoxelWorld();
        var raised = new List<VoxelChange>();
        world.Changed += raised.Add;

        world.PlaceBlocks([], "stone");

        Assert.Empty(raised);
    }

    [Fact]
    public void RemoveBlocks_ReportsOnlyTheCoordinatesThatWereActuallyRemoved()
    {
        var world = new VoxelWorld();
        world.PlaceBlock(new VoxelCoordinate(0, 0, 0), "stone");

        var removed = world.RemoveBlocks([new(0, 0, 0), new(5, 5, 5)]);

        Assert.Single(removed);
        Assert.Equal(new VoxelCoordinate(0, 0, 0), removed[0]);
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void RemoveBlocks_WhenNothingWasRemoved_RaisesNoEvent()
    {
        var world = new VoxelWorld();
        var raised = new List<VoxelChange>();
        world.Changed += raised.Add;

        var removed = world.RemoveBlocks([new(9, 9, 9)]);

        Assert.Empty(removed);
        Assert.Empty(raised);
    }

    [Fact]
    public void Clear_EmptiesTheWorldAndRaisesOneClearedEvent()
    {
        var world = new VoxelWorld();
        world.PlaceBlocks([new(0, 0, 0), new(1, 0, 0)], "stone");
        var raised = new List<VoxelChange>();
        world.Changed += raised.Add;

        world.Clear();

        Assert.Equal(0, world.Count);
        var change = Assert.Single(raised);
        Assert.IsType<VoxelChange.Cleared>(change);
    }

    [Fact]
    public void Clear_OnAnAlreadyEmptyWorld_RaisesNoEvent()
    {
        var world = new VoxelWorld();
        var raised = new List<VoxelChange>();
        world.Changed += raised.Add;

        world.Clear();

        Assert.Empty(raised);
    }

    [Fact]
    public void BoundingBox_ReflectsEveryOccupiedCoordinate()
    {
        var world = new VoxelWorld();
        world.PlaceBlocks([new(-2, 0, 5), new(3, 4, -1), new(0, 1, 0)], "stone");

        VoxelBounds? bounds = world.BoundingBox();

        Assert.NotNull(bounds);
        Assert.Equal(new VoxelBounds(MinX: -2, MaxX: 3, MinY: 0, MaxY: 4, MinZ: -1, MaxZ: 5), bounds!.Value);
    }

    [Fact]
    public void MirrorAcross_ReflectsBlocksAndSkipsAlreadyOccupiedTargets()
    {
        var world = new VoxelWorld();
        world.PlaceBlock(new VoxelCoordinate(1, 0, 0), "stone");
        world.PlaceBlock(new VoxelCoordinate(0, 0, 0), "gold"); // sits ON the mirror plane

        var placed = world.MirrorAcross(VoxelAxis.X, plane: 0);

        // (1,0,0) mirrors to (-1,0,0): new. (0,0,0) mirrors to itself: already
        // occupied, so it's skipped rather than overwritten.
        Assert.Single(placed);
        Assert.Equal(new VoxelCoordinate(-1, 0, 0), placed[0].Coordinate);
        Assert.Equal("stone", placed[0].Material);
        Assert.Equal(3, world.Count);
        Assert.Equal("gold", world.Snapshot().Single(b => b.Coordinate == new VoxelCoordinate(0, 0, 0)).Material);
    }

    [Fact]
    public void MirrorAcross_WithNothingToMirror_RaisesNoEvent()
    {
        var world = new VoxelWorld();
        var raised = new List<VoxelChange>();
        world.Changed += raised.Add;

        var placed = world.MirrorAcross(VoxelAxis.Z, plane: 0);

        Assert.Empty(placed);
        Assert.Empty(raised);
    }
}
