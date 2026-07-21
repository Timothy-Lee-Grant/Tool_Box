namespace ToolBox.Voxel.Tests;

/// <summary>
/// Tools are plain methods over a plain <see cref="VoxelWorld"/> — no MCP server, no
/// transport, no process needed to test their behavior (the same dividend
/// BasicsToolsTests banks on). These exercise the tool layer's own job: validation,
/// wiring rasterizer output into the world, and turning world state into text.
/// </summary>
public class VoxelToolsTests
{
    private static VoxelTools CreateTools(out VoxelWorld world)
    {
        world = new VoxelWorld();
        return new VoxelTools(world);
    }

    [Fact]
    public void WorldInfo_MentionsTheGroundRule()
        => Assert.Contains("ground", CreateTools(out _).WorldInfo(), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void ListMaterials_ContainsTheFixedPalette()
    {
        string result = CreateTools(out _).ListMaterials();

        Assert.Contains("stone", result);
        Assert.Contains("obsidian", result);
    }

    [Fact]
    public void PlaceBlock_AddsExactlyOneBlock()
    {
        var tools = CreateTools(out VoxelWorld world);

        string result = tools.PlaceBlock(1, 2, 3, "stone");

        Assert.Equal("placed stone", result);
        Assert.Equal(1, world.Count);
    }

    [Fact]
    public void PlaceBlock_RejectsNegativeY_WithoutMutatingTheWorld()
    {
        var tools = CreateTools(out VoxelWorld world);

        string result = tools.PlaceBlock(0, -1, 0, "stone");

        Assert.Contains("y", result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void PlaceBlock_RejectsUnknownMaterial_AndSuggestsANearMatch()
    {
        var tools = CreateTools(out VoxelWorld world);

        string result = tools.PlaceBlock(0, 0, 0, "ston");

        Assert.Contains("Unknown material", result);
        Assert.Contains("stone", result); // near-match hint
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void PlaceBox_PlacesTheFullVolume_WhenNotHollow()
    {
        var tools = CreateTools(out VoxelWorld world);

        string result = tools.PlaceBox(0, 0, 0, 2, 2, 2, "brick", hollow: false);

        Assert.Equal("placed 27 brick", result);
        Assert.Equal(27, world.Count);
    }

    [Fact]
    public void PlaceBox_PlacesOnlyTheShell_WhenHollow()
    {
        var tools = CreateTools(out VoxelWorld world);

        string result = tools.PlaceBox(0, 0, 0, 2, 2, 2, "brick", hollow: true);

        Assert.Equal("placed 26 brick", result);
    }

    [Fact]
    public void PlaceCylinder_RejectsAnOutOfRangeRadius()
    {
        var tools = CreateTools(out VoxelWorld world);

        string result = tools.PlaceCylinder(0, 0, r: 0.1, h: 5, material: "iron");

        Assert.Contains("r must be between", result);
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void PlaceCylinder_DefaultsToHollow()
    {
        var tools = CreateTools(out VoxelWorld world);

        tools.PlaceCylinder(0, 0, r: 3, h: 5, material: "iron");

        // The centre axis is carved out by default (hollow=true) — matches a tower,
        // not a solid pillar.
        Assert.DoesNotContain(world.Snapshot(), b => b.Coordinate == new VoxelCoordinate(0, 2, 0));
    }

    [Fact]
    public void PlaceCone_NarrowsToTheTip()
    {
        var tools = CreateTools(out VoxelWorld world);

        tools.PlaceCone(0, 0, y: 0, r: 5, h: 10, material: "stone");

        VoxelBounds bounds = world.BoundingBox()!.Value;
        Assert.Equal(0, bounds.MinY);
        Assert.Equal(9, bounds.MaxY);
    }

    [Fact]
    public void PlaceSphere_StretchesVerticallyWithRy()
    {
        var tools = CreateTools(out VoxelWorld world);

        tools.PlaceSphere(0, 5, 0, r: 3, material: "glass", ry: 6);

        VoxelBounds bounds = world.BoundingBox()!.Value;
        Assert.Equal(0, bounds.MinY);
        Assert.Equal(11, bounds.MaxY);
    }

    [Fact]
    public void PlaceTube_RejectsTooFewPathPoints()
    {
        var tools = CreateTools(out VoxelWorld world);
        VoxelPathPoint[] path = [new(0, 0, 0)];

        string result = tools.PlaceTube(path, rStart: 2, rEnd: 2, material: "stone");

        Assert.Contains("between 2 and 24 points", result);
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void PlaceTube_RejectsAPathPointBelowGround()
    {
        var tools = CreateTools(out VoxelWorld world);
        VoxelPathPoint[] path = [new(0, 0, 0), new(0, -1, 0)];

        string result = tools.PlaceTube(path, rStart: 2, rEnd: 2, material: "stone");

        Assert.Contains("path point 1", result);
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void PlaceTube_SweepsThroughTheGivenPath()
    {
        var tools = CreateTools(out VoxelWorld world);
        VoxelPathPoint[] path = [new(0, 0, 0), new(0, 0, 10)];

        tools.PlaceTube(path, rStart: 2, rEnd: 2, material: "wood");

        Assert.Contains(world.Snapshot(), b => b.Coordinate == new VoxelCoordinate(0, 0, 0));
        Assert.Contains(world.Snapshot(), b => b.Coordinate == new VoxelCoordinate(0, 0, 10));
    }

    [Fact]
    public void Mirror_ReflectsPlacedBlocks()
    {
        var tools = CreateTools(out VoxelWorld world);
        tools.PlaceBlock(1, 0, 0, "gold");

        string result = tools.Mirror(VoxelAxis.X, plane: 0);

        Assert.Equal("mirrored 1 blocks across x=0", result);
        Assert.Equal(2, world.Count);
        Assert.Contains(world.Snapshot(), b => b.Coordinate == new VoxelCoordinate(-1, 0, 0));
    }

    [Fact]
    public void RemoveBox_RemovesOnlyBlocksThatWereActuallyThere()
    {
        var tools = CreateTools(out VoxelWorld world);
        tools.PlaceBlock(0, 0, 0, "stone");

        string result = tools.RemoveBox(0, 0, 0, 2, 2, 2);

        Assert.Equal("removed 1 blocks", result);
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void Clear_EmptiesTheWorld()
    {
        var tools = CreateTools(out VoxelWorld world);
        tools.PlaceBlock(0, 0, 0, "stone");

        string result = tools.Clear();

        Assert.Equal("world cleared", result);
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void DescribeWorld_ReportsEmpty_WhenNothingIsBuilt()
        => Assert.Equal("The world is empty.", CreateTools(out _).DescribeWorld());

    [Fact]
    public void DescribeWorld_ReportsCountAndBounds_AfterBuilding()
    {
        var tools = CreateTools(out _);
        tools.PlaceBlock(0, 0, 0, "stone");
        tools.PlaceBlock(2, 1, 3, "stone");

        string result = tools.DescribeWorld();

        Assert.Equal("2 blocks. x 0..2, y 0..1, z 0..3.", result);
    }
}
