using ToolBox.Voxel;

namespace ToolBox.Voxel.Tests;

/// <summary>
/// Proves the ported geometry (Documentation/ImplementationPlans/003 §2.7) before any
/// tool wraps it — same "prove the primitive first" discipline as plan 001's trivial
/// tools. Expected counts below were cross-checked with an independent script run
/// against the same thresholds, not hand-derived from the C# — a translation bug
/// (wrong operator, off-by-one range, transposed axis) should fail these.
/// </summary>
public class VoxelRasterizerTests
{
    [Fact]
    public void Box_Filled_CountsTheWholeVolume()
    {
        var coords = VoxelRasterizer.Box(0, 0, 0, 2, 2, 2, hollow: false).ToArray();

        Assert.Equal(27, coords.Length); // 3x3x3
    }

    [Fact]
    public void Box_Hollow_CountsOnlyTheShell()
    {
        var coords = VoxelRasterizer.Box(0, 0, 0, 2, 2, 2, hollow: true).ToArray();

        // 27 total minus the single (1,1,1) interior cell.
        Assert.Equal(26, coords.Length);
        Assert.DoesNotContain(new VoxelCoordinate(1, 1, 1), coords);
    }

    [Fact]
    public void Box_Hollow_ButFlatInOneAxis_IsEntirelyShell()
    {
        // ax == bx == 0, so every cell satisfies the shell condition trivially —
        // a single-layer-thick wall has no "interior" to hollow out.
        var flat = VoxelRasterizer.Box(0, 0, 0, 0, 2, 2, hollow: true).ToArray();
        var filled = VoxelRasterizer.Box(0, 0, 0, 0, 2, 2, hollow: false).ToArray();

        Assert.Equal(9, flat.Length); // 1x3x3
        Assert.Equal(filled.Length, flat.Length);
    }

    [Fact]
    public void Box_AcceptsCornersInEitherOrder()
    {
        var forward = VoxelRasterizer.Box(0, 0, 0, 2, 2, 2, hollow: false).ToHashSet();
        var reversed = VoxelRasterizer.Box(2, 2, 2, 0, 0, 0, hollow: false).ToHashSet();

        Assert.Equal(forward, reversed);
    }

    [Fact]
    public void Cylinder_Solid_MatchesKnownCount()
    {
        var coords = VoxelRasterizer.Cylinder(cx: 0, cz: 0, r: 3, h: 5, y0: 0, hollow: false).ToArray();

        Assert.Equal(185, coords.Length);
        Assert.Contains(new VoxelCoordinate(0, 2, 0), coords); // solid: axis is filled
    }

    [Fact]
    public void Cylinder_Hollow_ExcludesTheAxisButKeepsFewerBlocksThanSolid()
    {
        var solid = VoxelRasterizer.Cylinder(cx: 0, cz: 0, r: 3, h: 5, y0: 0, hollow: false).ToArray();
        var hollow = VoxelRasterizer.Cylinder(cx: 0, cz: 0, r: 3, h: 5, y0: 0, hollow: true).ToArray();

        Assert.Equal(120, hollow.Length);
        Assert.True(hollow.Length < solid.Length);
        Assert.DoesNotContain(new VoxelCoordinate(0, 2, 0), hollow); // hollow: axis is carved out
    }

    [Fact]
    public void Cylinder_SpansExactlyItsHeightInLayers()
    {
        var coords = VoxelRasterizer.Cylinder(cx: 0, cz: 0, r: 3, h: 5, y0: 10, hollow: false).ToArray();

        Assert.Equal(10, coords.Min(c => c.Y));
        Assert.Equal(14, coords.Max(c => c.Y)); // y0=10, h=5 -> layers 10..14
    }

    [Fact]
    public void Cone_NarrowsFromBaseToTip()
    {
        var coords = VoxelRasterizer.Cone(cx: 0, cz: 0, y0: 0, r: 5, h: 10, r2: 0).ToArray();

        int bottomLayerCount = coords.Count(c => c.Y == 0);
        int topLayerCount = coords.Count(c => c.Y == 9);

        Assert.Equal(354, coords.Length);
        Assert.Equal(97, bottomLayerCount);
        Assert.Equal(1, topLayerCount); // r2=0 -> the tip is (nearly) a single point
        Assert.True(bottomLayerCount > topLayerCount);
    }

    [Fact]
    public void Sphere_Solid_IsInTheRightBallparkOfTheVolumeFormula()
    {
        var coords = VoxelRasterizer.Sphere(cx: 0, cy: 5, cz: 0, r: 3, ry: 0, rz: 0, hollow: false).ToArray();

        // 4/3 * pi * r^3 ~= 113 for r=3; the +1.05 boundary fudge (see class remarks)
        // means the rasterized count runs a bit ahead of the pure math formula.
        Assert.Equal(123, coords.Length);
    }

    [Fact]
    public void Sphere_Hollow_HasFewerBlocksThanSolid()
    {
        var solid = VoxelRasterizer.Sphere(cx: 0, cy: 5, cz: 0, r: 3, ry: 0, rz: 0, hollow: false).ToArray();
        var hollow = VoxelRasterizer.Sphere(cx: 0, cy: 5, cz: 0, r: 3, ry: 0, rz: 0, hollow: true).ToArray();

        Assert.Equal(66, hollow.Length);
        Assert.True(hollow.Length < solid.Length);
    }

    [Fact]
    public void Sphere_RyStretchesTheVerticalExtent()
    {
        var normal = VoxelRasterizer.Sphere(cx: 0, cy: 5, cz: 0, r: 3, ry: 0, rz: 0, hollow: false).ToArray();
        var stretched = VoxelRasterizer.Sphere(cx: 0, cy: 5, cz: 0, r: 3, ry: 6, rz: 0, hollow: false).ToArray();

        Assert.Equal((2, 8), (normal.Min(c => c.Y), normal.Max(c => c.Y)));
        Assert.Equal((0, 11), (stretched.Min(c => c.Y), stretched.Max(c => c.Y)));
    }

    [Fact]
    public void Tube_RequiresAtLeastTwoPathPoints()
    {
        VoxelCoordinate[] path = [new VoxelCoordinate(0, 0, 0)];

        Assert.Throws<ArgumentException>(() => VoxelRasterizer.Tube(path, 2, 2).ToArray());
    }

    [Fact]
    public void Tube_StraightAndConstantRadius_ReachesBothEndsAndTheMiddle()
    {
        VoxelCoordinate[] path = [new VoxelCoordinate(0, 0, 0), new VoxelCoordinate(0, 0, 10)];
        var coords = VoxelRasterizer.Tube(path, rStart: 2, rEnd: 2).ToHashSet();

        Assert.Equal(169, coords.Count);
        Assert.Contains(new VoxelCoordinate(0, 0, 0), coords);
        Assert.Contains(new VoxelCoordinate(0, 0, 10), coords);
        Assert.Contains(new VoxelCoordinate(0, 0, 5), coords);
    }

    [Fact]
    public void Tube_Tapers_WhenStartAndEndRadiiDiffer()
    {
        VoxelCoordinate[] path = [new VoxelCoordinate(0, 0, 0), new VoxelCoordinate(0, 0, 10)];
        var coords = VoxelRasterizer.Tube(path, rStart: 3, rEnd: 1).ToArray();

        int nearStart = coords.Count(c => c.Z <= 1);
        int nearEnd = coords.Count(c => c.Z >= 9);

        // The cross-section near the wide end packs in far more blocks per unit
        // length than the cross-section near the narrow end.
        Assert.True(nearStart > nearEnd);
    }

    [Fact]
    public void Tube_NeverYieldsTheSameCoordinateTwice()
    {
        VoxelCoordinate[] path =
        [
            new VoxelCoordinate(0, 0, 0),
            new VoxelCoordinate(2, 0, 2),
            new VoxelCoordinate(0, 0, 4),
        ];
        var coords = VoxelRasterizer.Tube(path, rStart: 3, rEnd: 3).ToArray();

        Assert.Equal(coords.Length, coords.Distinct().Count());
    }
}
