namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// Which faces of a mesh pass through another face, for diagnostics that need to say where crossings
/// are rather than how many - the engine answers only the count. Brute force made tolerable by a grid:
/// triangles are binned by their bounds, and only pairs sharing a cell are tested.
/// </summary>
/// <remarks>
/// Pairs sharing a vertex are skipped, as they meet at that vertex by construction, and a pair counts
/// only when the separating-axis test finds no axis between them with room to spare - so two faces
/// that merely touch do not count as crossing. Slow on large meshes; it is only ever asked of one.
/// </remarks>
internal static class SelfIntersectingFaces
{
    private const double Tolerance = 1e-9;

    /// <summary>The centroid of every face that crosses another.</summary>
    public static Vector3[] Centroids(IMesh mesh)
    {
        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;
        int faceCount = triangles.Length / 3;
        if (faceCount == 0) return [];

        var min = new Vector3(double.MaxValue, double.MaxValue, double.MaxValue);
        var max = new Vector3(double.MinValue, double.MinValue, double.MinValue);
        double edges = 0;
        for (int f = 0; f < faceCount; f++)
        {
            var (a, b, c) = Corners(f);
            min = min.ComponentMin(a).ComponentMin(b).ComponentMin(c);
            max = max.ComponentMax(a).ComponentMax(b).ComponentMax(c);
            edges += a.DistanceTo(b) + b.DistanceTo(c) + c.DistanceTo(a);
        }

        double cell = Math.Max(edges / (faceCount * 3), 1e-6) * 2;
        var grid = new Dictionary<(int, int, int), List<int>>();
        for (int f = 0; f < faceCount; f++)
        {
            var (a, b, c) = Corners(f);
            var (lo, hi) = (Cell(a.ComponentMin(b).ComponentMin(c)), Cell(a.ComponentMax(b).ComponentMax(c)));
            for (int x = lo.Item1; x <= hi.Item1; x++)
                for (int y = lo.Item2; y <= hi.Item2; y++)
                    for (int z = lo.Item3; z <= hi.Item3; z++)
                    {
                        if (!grid.TryGetValue((x, y, z), out var list)) grid[(x, y, z)] = list = [];
                        list.Add(f);
                    }
        }

        var crossing = new HashSet<int>();
        var tested = new HashSet<(int, int)>();
        foreach (var list in grid.Values)
        {
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    var (f, g) = list[i] < list[j] ? (list[i], list[j]) : (list[j], list[i]);
                    if (!tested.Add((f, g)) || ShareVertex(f, g)) continue;

                    var (a0, a1, a2) = Corners(f);
                    var (b0, b1, b2) = Corners(g);
                    if (Intersect(a0, a1, a2, b0, b1, b2))
                    {
                        crossing.Add(f);
                        crossing.Add(g);
                    }
                }
        }

        return crossing.Select(f =>
        {
            var (a, b, c) = Corners(f);
            return (a + b + c) / 3.0;
        }).ToArray();

        (Vector3, Vector3, Vector3) Corners(int f) =>
            (vertices[triangles[f * 3]], vertices[triangles[(f * 3) + 1]], vertices[triangles[(f * 3) + 2]]);

        (int, int, int) Cell(Vector3 p) =>
            ((int)Math.Floor((p.X - min.X) / cell), (int)Math.Floor((p.Y - min.Y) / cell), (int)Math.Floor((p.Z - min.Z) / cell));

        bool ShareVertex(int f, int g)
        {
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    if (vertices[triangles[(f * 3) + i]] == vertices[triangles[(g * 3) + j]]) return true;
            return false;
        }
    }

    /// <summary>
    /// The separating-axis test for two triangles: the two face normals, the nine edge-edge crosses,
    /// and - for a coplanar pair, where those all vanish - each edge crossed with the shared normal.
    /// </summary>
    private static bool Intersect(Vector3 a0, Vector3 a1, Vector3 a2, Vector3 b0, Vector3 b1, Vector3 b2)
    {
        Vector3[] a = [a0, a1, a2];
        Vector3[] b = [b0, b1, b2];
        var na = (a1 - a0).Cross(a2 - a0);
        var nb = (b1 - b0).Cross(b2 - b0);

        var axes = new List<Vector3> { na, nb };
        for (int i = 0; i < 3; i++)
        {
            var ea = a[(i + 1) % 3] - a[i];
            var eb = b[(i + 1) % 3] - b[i];
            for (int j = 0; j < 3; j++)
                axes.Add(ea.Cross(b[(j + 1) % 3] - b[j]));
            axes.Add(ea.Cross(na));
            axes.Add(eb.Cross(nb));
        }

        foreach (var axis in axes)
        {
            double length = axis.Length;
            if (length < 1e-12) continue;

            var unit = axis / length;
            var (aLo, aHi) = Project(a, unit);
            var (bLo, bHi) = Project(b, unit);
            if (aHi <= bLo + Tolerance || bHi <= aLo + Tolerance) return false;
        }

        return true;

        static (double, double) Project(Vector3[] t, Vector3 axis)
        {
            double p0 = t[0].Dot(axis), p1 = t[1].Dot(axis), p2 = t[2].Dot(axis);
            return (Math.Min(p0, Math.Min(p1, p2)), Math.Max(p0, Math.Max(p1, p2)));
        }
    }
}
