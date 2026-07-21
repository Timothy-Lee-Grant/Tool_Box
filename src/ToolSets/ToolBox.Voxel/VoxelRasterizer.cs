namespace ToolBox.Voxel;

/// <summary>
/// Pure geometry: turns a shape description (a box's corners, a sphere's radius, a
/// tube's path) into the set of grid coordinates it occupies. No state, no MCP, no
/// materials — just "which cells". Ported from a working reference implementation
/// (Documentation/ImplementationPlans/003 §2.7); the fudge factors below (the
/// "+ 0.5", "+ 1.05", etc.) come from there, not from first principles — they're
/// what makes a rasterized sphere/cylinder/cone read as round at grid resolution
/// instead of faceted or gappy, and changing them is a visual tuning knob, not a bug fix.
/// </summary>
public static class VoxelRasterizer
{
    public static IEnumerable<VoxelCoordinate> Box(int x1, int y1, int z1, int x2, int y2, int z2, bool hollow)
    {
        (int ax, int bx) = (Math.Min(x1, x2), Math.Max(x1, x2));
        (int ay, int by) = (Math.Min(y1, y2), Math.Max(y1, y2));
        (int az, int bz) = (Math.Min(z1, z2), Math.Max(z1, z2));

        for (int x = ax; x <= bx; x++)
        {
            for (int y = ay; y <= by; y++)
            {
                for (int z = az; z <= bz; z++)
                {
                    bool shell = x == ax || x == bx || y == ay || y == by || z == az || z == bz;
                    if (hollow && !shell)
                    {
                        continue;
                    }

                    yield return new VoxelCoordinate(x, y, z);
                }
            }
        }
    }

    public static IEnumerable<VoxelCoordinate> Cylinder(int cx, int cz, double r, int h, int y0, bool hollow)
    {
        for (int y = y0; y < y0 + h; y++)
        {
            for (int x = Floor(cx - r); x <= Ceil(cx + r); x++)
            {
                for (int z = Floor(cz - r); z <= Ceil(cz + r); z++)
                {
                    double d = Hypot(x - cx, z - cz);
                    if (d > r + 0.5)
                    {
                        continue;
                    }

                    if (hollow && d < r - 0.9)
                    {
                        continue;
                    }

                    yield return new VoxelCoordinate(x, y, z);
                }
            }
        }
    }

    public static IEnumerable<VoxelCoordinate> Cone(int cx, int cz, int y0, double r, int h, double r2)
    {
        for (int i = 0; i < h; i++)
        {
            double rr = r + ((r2 - r) * (i / Math.Max(h - 1, 1.0)));
            int y = y0 + i;

            for (int x = Floor(cx - rr); x <= Ceil(cx + rr); x++)
            {
                for (int z = Floor(cz - rr); z <= Ceil(cz + rr); z++)
                {
                    if (Hypot(x - cx, z - cz) <= rr + 0.4)
                    {
                        yield return new VoxelCoordinate(x, y, z);
                    }
                }
            }
        }
    }

    /// <summary><paramref name="ry"/>/<paramref name="rz"/> of 0 mean "same as r" —
    /// pass either to stretch the sphere into an ellipsoid.</summary>
    public static IEnumerable<VoxelCoordinate> Sphere(int cx, int cy, int cz, double r, double ry, double rz, bool hollow)
    {
        double effectiveRy = ry > 0 ? ry : r;
        double effectiveRz = rz > 0 ? rz : r;
        int minY = Math.Max(0, Floor(cy - effectiveRy));

        for (int x = Floor(cx - r); x <= Ceil(cx + r); x++)
        {
            for (int y = minY; y <= Ceil(cy + effectiveRy); y++)
            {
                for (int z = Floor(cz - effectiveRz); z <= Ceil(cz + effectiveRz); z++)
                {
                    double d = Hypot3((x - cx) / r, (y - cy) / effectiveRy, (z - cz) / effectiveRz);
                    if (d > 1.05)
                    {
                        continue;
                    }

                    if (hollow && d < 0.78)
                    {
                        continue;
                    }

                    yield return new VoxelCoordinate(x, y, z);
                }
            }
        }
    }

    /// <summary>
    /// Sweeps a tapering tube through a path of 2+ points — the primitive for
    /// organic, curved forms. Radius interpolates linearly along the whole path, so
    /// <paramref name="rStart"/> &gt; <paramref name="rEnd"/> tapers it.
    /// </summary>
    public static IEnumerable<VoxelCoordinate> Tube(IReadOnlyList<VoxelCoordinate> path, double rStart, double rEnd)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count < 2)
        {
            throw new ArgumentException("A tube needs at least 2 path points.", nameof(path));
        }

        HashSet<VoxelCoordinate> seen = [];
        int segments = path.Count - 1;

        for (int s = 0; s < segments; s++)
        {
            VoxelCoordinate a = path[s];
            VoxelCoordinate b = path[s + 1];
            double length = Hypot3(b.X - a.X, b.Y - a.Y, b.Z - a.Z);
            int steps = Math.Max(2, (int)Math.Ceiling(length * 2));

            for (int i = 0; i <= steps; i++)
            {
                double t = (s + (i / (double)steps)) / segments;
                double u = i / (double)steps;
                double cx = a.X + ((b.X - a.X) * u);
                double cy = a.Y + ((b.Y - a.Y) * u);
                double cz = a.Z + ((b.Z - a.Z) * u);
                double rr = rStart + ((rEnd - rStart) * t);
                int minY = Math.Max(0, Floor(cy - rr));

                for (int x = Floor(cx - rr); x <= Ceil(cx + rr); x++)
                {
                    for (int y = minY; y <= Ceil(cy + rr); y++)
                    {
                        for (int z = Floor(cz - rr); z <= Ceil(cz + rr); z++)
                        {
                            if (Hypot3(x - cx, y - cy, z - cz) > rr + 0.35)
                            {
                                continue;
                            }

                            var coordinate = new VoxelCoordinate(x, y, z);
                            if (seen.Add(coordinate))
                            {
                                yield return coordinate;
                            }
                        }
                    }
                }
            }
        }
    }

    private static int Floor(double value) => (int)Math.Floor(value);

    private static int Ceil(double value) => (int)Math.Ceiling(value);

    private static double Hypot(double a, double b) => Math.Sqrt((a * a) + (b * b));

    private static double Hypot3(double a, double b, double c) => Math.Sqrt((a * a) + (b * b) + (c * c));
}
