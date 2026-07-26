using System.ComponentModel;
using ModelContextProtocol.Server;
using ToolBox.Core;

namespace ToolBox.Voxel;

/// <summary>A single point along a <c>place_tube</c> path — the wire-facing shape for
/// what <see cref="VoxelRasterizer.Tube"/> takes as <see cref="VoxelCoordinate"/>s.</summary>
[Description("A single point along a tube's path, in grid coordinates.")]
public sealed record VoxelPathPoint(
    [property: Description("Grid X coordinate.")] int X,
    [property: Description("Grid Y coordinate. 0 is ground; never negative.")] int Y,
    [property: Description("Grid Z coordinate.")] int Z)
{
    public VoxelCoordinate ToCoordinate() => new(X, Y, Z);
}

/// <summary>
/// Tools that describe *form*, not coordinates — the call-economy lesson from
/// Documentation/Brainstorms/003-TOOLSET_IDEAS.md (A1) and confirmed against a working
/// reference implementation in Documentation/ImplementationPlans/003 §2.7. The agent
/// never computes a cube position itself; it describes a box, a radius, a taper, and
/// <see cref="VoxelRasterizer"/> works out which cells that means server-side.
///
/// Every tool and parameter description below is a prompt — the model never sees this
/// C# code, only these strings (see DescriptionConventionTests).
/// </summary>
[McpServerToolType]
public sealed class VoxelTools
{
    private readonly VoxelWorld _world;

    public VoxelTools(VoxelWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    [McpServerTool(Name = "world_info")]
    [Description("Report the world's conventions: the coordinate system, the ground rule, " +
                 "and reference sizes in blocks. CALL THIS FIRST, before any build — the other " +
                 "tool signatures can't tell you how big a block is meant to represent.")]
    public string WorldInfo() => OutputLimiter.Limit(
        "This is a voxel grid, not a continuous space. x/z are the ground plane; y is up.\n" +
        "y = 0 is ground — never place below it (every tool here rejects a negative y).\n\n" +
        "Reference sizes, in blocks:\n" +
        "  a person          ~2 tall\n" +
        "  a doorway         ~1 wide x 3 tall\n" +
        "  a wall            ~9 tall\n" +
        "  a tower           ~26 tall\n" +
        "  a large structure ~50 long\n\n" +
        "Scale builds to this table. There is no metric unit here, only blocks.");

    [McpServerTool(Name = "list_materials")]
    [Description("List every material this world accepts. Call this once before building " +
                 "so you know the palette.")]
    public string ListMaterials() => OutputLimiter.Limit(string.Join(", ", Materials.All));

    [McpServerTool(Name = "place_block")]
    [Description("Place one block. Detail only — a single accent, a corner stone. Use the " +
                 "bulk primitives (place_box, place_cylinder, ...) for anything larger; looping " +
                 "this tool to build a wall wastes calls the primitives already solve.")]
    public string PlaceBlock(
        [Description("Grid X coordinate.")] int x,
        [Description("Grid Y coordinate. 0 is ground; never negative.")] int y,
        [Description("Grid Z coordinate.")] int z,
        [Description("Material name — call list_materials to see the options.")] string material)
    {
        string? error = FirstFailure(ValidateGround(y, nameof(y)), Materials.Validate(material));
        if (error is not null)
        {
            return OutputLimiter.Limit(error);
        }

        _world.PlaceBlock(new VoxelCoordinate(x, y, z), material);
        return OutputLimiter.Limit($"placed {material}");
    }

    [McpServerTool(Name = "place_box")]
    [Description("Fill a rectangular volume between two corners (inclusive). Walls, floors, " +
                 "rooms. hollow=true gives a shell instead of a solid block — use that for " +
                 "walls and rooms you want to stand inside.")]
    public string PlaceBox(
        [Description("First corner X.")] int x1,
        [Description("First corner Y. 0 is ground; never negative.")] int y1,
        [Description("First corner Z.")] int z1,
        [Description("Second corner X.")] int x2,
        [Description("Second corner Y. 0 is ground; never negative.")] int y2,
        [Description("Second corner Z.")] int z2,
        [Description("Material name — call list_materials to see the options.")] string material,
        [Description("True for a hollow shell (walls/rooms), false for solid fill. Default false.")]
        bool hollow = false)
    {
        string? error = FirstFailure(
            ValidateGround(y1, nameof(y1)),
            ValidateGround(y2, nameof(y2)),
            Materials.Validate(material));
        if (error is not null)
        {
            return OutputLimiter.Limit(error);
        }

        List<VoxelCoordinate> coords = [.. VoxelRasterizer.Box(x1, y1, z1, x2, y2, z2, hollow)];
        _world.PlaceBlocks(coords, material);
        return OutputLimiter.Limit($"placed {coords.Count} {material}");
    }

    [McpServerTool(Name = "place_cylinder")]
    [Description("A vertical cylinder — the primitive for round towers and columns.")]
    public string PlaceCylinder(
        [Description("Centre X.")] int x,
        [Description("Centre Z.")] int z,
        [Description("Radius, 1-30.")] double r,
        [Description("Height in blocks, 1-120.")] int h,
        [Description("Material name — call list_materials to see the options.")] string material,
        [Description("Base Y. 0 is ground; never negative. Default 0.")] int y = 0,
        [Description("True for a hollow tube (default — towers are usually walked inside), " +
                     "false for a solid column.")]
        bool hollow = true)
    {
        string? error = FirstFailure(
            ValidateGround(y, nameof(y)),
            ValidateRange(r, 1, 30, nameof(r)),
            ValidateRange(h, 1, 120, nameof(h)),
            Materials.Validate(material));
        if (error is not null)
        {
            return OutputLimiter.Limit(error);
        }

        List<VoxelCoordinate> coords = [.. VoxelRasterizer.Cylinder(x, z, r, h, y, hollow)];
        _world.PlaceBlocks(coords, material);
        return OutputLimiter.Limit($"placed {coords.Count} {material}");
    }

    [McpServerTool(Name = "place_cone")]
    [Description("A cone — witch-hat tower caps and spires. Set r2 for a truncated cone " +
                 "(a frustum) instead of a point.")]
    public string PlaceCone(
        [Description("Centre X.")] int x,
        [Description("Centre Z.")] int z,
        [Description("Base Y. 0 is ground; never negative.")] int y,
        [Description("Base radius, 1-30.")] double r,
        [Description("Height in blocks, 1-80.")] int h,
        [Description("Material name — call list_materials to see the options.")] string material,
        [Description("Radius at the top, 0-30. 0 = a point (the default).")] double r2 = 0)
    {
        string? error = FirstFailure(
            ValidateGround(y, nameof(y)),
            ValidateRange(r, 1, 30, nameof(r)),
            ValidateRange(h, 1, 80, nameof(h)),
            ValidateRange(r2, 0, 30, nameof(r2)),
            Materials.Validate(material));
        if (error is not null)
        {
            return OutputLimiter.Limit(error);
        }

        List<VoxelCoordinate> coords = [.. VoxelRasterizer.Cone(x, z, y, r, h, r2)];
        _world.PlaceBlocks(coords, material);
        return OutputLimiter.Limit($"placed {coords.Count} {material}");
    }

    [McpServerTool(Name = "place_sphere")]
    [Description("A sphere or ellipsoid — domes, orbs, rounded roofs. Give ry/rz to stretch " +
                 "it away from a perfect sphere.")]
    public string PlaceSphere(
        [Description("Centre X.")] int x,
        [Description("Centre Y. 0 is ground; never negative.")] int y,
        [Description("Centre Z.")] int z,
        [Description("Radius, 1-30.")] double r,
        [Description("Material name — call list_materials to see the options.")] string material,
        [Description("Vertical radius, 0-30. 0 = use r (a uniform sphere).")] double ry = 0,
        [Description("Depth radius, 0-30. 0 = use r (a uniform sphere).")] double rz = 0,
        [Description("True for a hollow shell (a dome), false for a solid fill. Default false.")]
        bool hollow = false)
    {
        string? error = FirstFailure(
            ValidateGround(y, nameof(y)),
            ValidateRange(r, 1, 30, nameof(r)),
            ValidateRange(ry, 0, 30, nameof(ry)),
            ValidateRange(rz, 0, 30, nameof(rz)),
            Materials.Validate(material));
        if (error is not null)
        {
            return OutputLimiter.Limit(error);
        }

        List<VoxelCoordinate> coords = [.. VoxelRasterizer.Sphere(x, y, z, r, ry, rz, hollow)];
        _world.PlaceBlocks(coords, material);
        return OutputLimiter.Limit($"placed {coords.Count} {material}");
    }

    [McpServerTool(Name = "place_tube")]
    [Description("A tapering tube swept through a path of points — the primitive for curved, " +
                 "organic forms. Radius interpolates linearly along the path, so r_start > " +
                 "r_end tapers it. Give 3+ points for a curve, not just a straight segment.")]
    public string PlaceTube(
        [Description("Spine points, 2-24 of them. The tube is swept through them in order.")]
        IReadOnlyList<VoxelPathPoint> path,
        [Description("Radius at the first point, 0.5-24.")] double rStart,
        [Description("Radius at the last point, 0.3-24.")] double rEnd,
        [Description("Material name — call list_materials to see the options.")] string material)
    {
        string? pathLengthError = path.Count is < 2 or > 24
            ? $"path must contain between 2 and 24 points — got {path.Count}."
            : null;
        string? pathGroundError = path
            .Select((p, i) => p.Y < 0 ? $"path point {i} has y={p.Y}; y must be 0 or greater." : null)
            .FirstOrDefault(e => e is not null);

        string? error = FirstFailure(
            pathLengthError,
            pathGroundError,
            ValidateRange(rStart, 0.5, 24, nameof(rStart)),
            ValidateRange(rEnd, 0.3, 24, nameof(rEnd)),
            Materials.Validate(material));
        if (error is not null)
        {
            return OutputLimiter.Limit(error);
        }

        List<VoxelCoordinate> coordinates = [.. path.Select(p => p.ToCoordinate())];
        List<VoxelCoordinate> coords = [.. VoxelRasterizer.Tube(coordinates, rStart, rEnd)];
        _world.PlaceBlocks(coords, material);
        return OutputLimiter.Limit($"placed {coords.Count} {material}");
    }

    [McpServerTool(Name = "mirror")]
    [Description("Mirror the whole build across a plane. Build one wing or one half of a " +
                 "facade, then mirror across x=0 (or z=0) for perfect symmetry instead of " +
                 "hand-building both sides.")]
    public string Mirror(
        [Description("Which ground-plane axis to mirror across.")] VoxelAxis axis,
        [Description("The plane's position along that axis. Default 0.")] int plane = 0)
    {
        IReadOnlyList<PlacedVoxel> placed = _world.MirrorAcross(axis, plane);
        string axisLabel = axis == VoxelAxis.X ? "x" : "z";
        return OutputLimiter.Limit($"mirrored {placed.Count} blocks across {axisLabel}={plane}");
    }

    [McpServerTool(Name = "remove_box")]
    [Description("Remove every block in a volume between two corners (inclusive) — the fast " +
                 "way to carve a gate, window, or doorway.")]
    public string RemoveBox(
        [Description("First corner X.")] int x1,
        [Description("First corner Y. 0 is ground; never negative.")] int y1,
        [Description("First corner Z.")] int z1,
        [Description("Second corner X.")] int x2,
        [Description("Second corner Y. 0 is ground; never negative.")] int y2,
        [Description("Second corner Z.")] int z2)
    {
        string? error = FirstFailure(ValidateGround(y1, nameof(y1)), ValidateGround(y2, nameof(y2)));
        if (error is not null)
        {
            return OutputLimiter.Limit(error);
        }

        List<VoxelCoordinate> candidates = [.. VoxelRasterizer.Box(x1, y1, z1, x2, y2, z2, hollow: false)];
        IReadOnlyList<VoxelCoordinate> removed = _world.RemoveBlocks(candidates);
        return OutputLimiter.Limit($"removed {removed.Count} blocks");
    }

    [McpServerTool(Name = "clear")]
    [Description("Empty the world completely. Call before starting a new, unrelated build.")]
    public string Clear()
    {
        _world.Clear();
        return OutputLimiter.Limit("world cleared");
    }

    [McpServerTool(Name = "describe_world")]
    [Description("Report the current build's block count and bounding box. The closest thing " +
                 "to 'seeing' the world from here — there is no image, only this summary.")]
    public string DescribeWorld()
    {
        VoxelBounds? bounds = _world.BoundingBox();
        if (bounds is null)
        {
            return OutputLimiter.Limit("The world is empty.");
        }

        VoxelBounds b = bounds.Value;
        return OutputLimiter.Limit(
            $"{_world.Count} blocks. x {b.MinX}..{b.MaxX}, y {b.MinY}..{b.MaxY}, z {b.MinZ}..{b.MaxZ}.");
    }

    private static string? ValidateGround(int y, string paramName) =>
        y < 0 ? $"{paramName} must be 0 or greater — 0 is ground, and this world never goes below it. Got {y}." : null;

    private static string? ValidateRange(double value, double min, double max, string paramName) =>
        value < min || value > max ? $"{paramName} must be between {min} and {max} — got {value}." : null;

    private static string? FirstFailure(params string?[] checks) =>
        checks.FirstOrDefault(c => c is not null);
}
