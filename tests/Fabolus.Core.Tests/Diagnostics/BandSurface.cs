using System.Numerics;
using Fabolus.Core.Geometry;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// The rim wall as a working surface: which faces belong to it, how they join, and which of the two
/// creases each boundary vertex belongs to.
///
/// <para>
/// Shared by every candidate centring method so they are compared on the same band rather than on
/// three slightly different readings of it. The one thing they must agree about is what they are
/// finding the middle of.
/// </para>
/// </summary>
internal sealed class BandSurface
{
    public required IMesh Mesh { get; init; }
    public required bool[] Faces { get; init; }
    public required int[] FaceList { get; init; }
    public required Vector3[] Centroid { get; init; }
    public required List<int>[] Neighbours { get; init; }

    /// <summary>Per vertex: 0 on the first crease, 1 on the second, -1 for everything interior.</summary>
    public required int[] Side { get; init; }

    /// <summary>Vertices belonging to at least one band face.</summary>
    public required List<int> Vertices { get; init; }

    public required List<int>[] VertexNeighbours { get; init; }

    public required PartingBand Band { get; init; }

    public static BandSurface? Build(IMesh mesh, bool[] band, int[] faceRims, int rim, PartingBand pair)
    {
        var triangles = mesh.Triangles;
        var vertices = mesh.Vertices;
        int faceCount = triangles.Length / 3;
        if (band.Length != faceCount) return null;

        // Only this rim's share of the band. A body with two rims has two walls, and a field solved
        // across both at once would run from one rim's crease to the other's.
        var mine = new bool[faceCount];
        var list = new List<int>();
        for (int f = 0; f < faceCount; f++)
        {
            if (!band[f]) continue;

            // A face with no rim is kept. FaceRims is read off the ridge pass, so the faces the
            // hole-closing added afterwards carry -1 - and dropping those punches the very holes back
            // into the band that the closing had just filled, which leaves a level set with nothing to
            // close around.
            if (faceRims.Length == faceCount && faceRims[f] >= 0 && faceRims[f] != rim) continue;
            mine[f] = true;
            list.Add(f);
        }

        if (list.Count < 16) return null;

        var edges = new Dictionary<(int, int), List<int>>(list.Count * 2);
        foreach (int f in list)
            for (int e = 0; e < 3; e++)
            {
                int a = triangles[(f * 3) + e];
                int b = triangles[(f * 3) + ((e + 1) % 3)];
                var key = a < b ? (a, b) : (b, a);
                if (!edges.TryGetValue(key, out var shared)) edges[key] = shared = new List<int>(2);
                shared.Add(f);
            }

        var neighbours = new List<int>[faceCount];
        foreach (int f in list) neighbours[f] = new List<int>(3);
        foreach (var shared in edges.Values)
            for (int i = 0; i < shared.Count; i++)
                for (int j = 0; j < shared.Count; j++)
                    if (i != j) neighbours[shared[i]].Add(shared[j]);

        var centroid = new Vector3[faceCount];
        foreach (int f in list)
            centroid[f] = (vertices[triangles[f * 3]]
                + vertices[triangles[(f * 3) + 1]]
                + vertices[triangles[(f * 3) + 2]]) / 3f;

        // Band vertices, and the neighbour graph restricted to band edges.
        var onBand = new bool[vertices.Length];
        foreach (int f in list)
            for (int e = 0; e < 3; e++) onBand[triangles[(f * 3) + e]] = true;

        var vertexList = new List<int>();
        for (int v = 0; v < vertices.Length; v++) if (onBand[v]) vertexList.Add(v);

        var vertexNeighbours = new List<int>[vertices.Length];
        foreach (int v in vertexList) vertexNeighbours[v] = new List<int>(6);
        foreach (var key in edges.Keys)
        {
            vertexNeighbours[key.Item1].Add(key.Item2);
            vertexNeighbours[key.Item2].Add(key.Item1);
        }

        // A vertex on the band's edge - one whose edge has only a single band face - belongs to
        // whichever crease it is nearer. That is what pins the two ends of every field below.
        var side = new int[vertices.Length];
        Array.Fill(side, -1);

        foreach (var (key, shared) in edges)
        {
            if (shared.Count >= 2) continue;   // interior edge of the band

            foreach (int v in new[] { key.Item1, key.Item2 })
            {
                float toFirst = PartingBandProbe.Distance(vertices[v], pair.First);
                float toSecond = PartingBandProbe.Distance(vertices[v], pair.Second);
                side[v] = toFirst <= toSecond ? 0 : 1;
            }
        }

        if (!side.Any(s => s == 0) || !side.Any(s => s == 1)) return null;

        return new BandSurface
        {
            Mesh = mesh,
            Faces = mine,
            FaceList = list.ToArray(),
            Centroid = centroid,
            Neighbours = neighbours,
            Side = side,
            Vertices = vertexList,
            VertexNeighbours = vertexNeighbours,
            Band = pair,
        };
    }
}

/// <summary>Point-to-contour distance, kept apart so every method measures it the same way.</summary>
internal static class PartingBandProbe
{
    public static float Distance(Vector3 from, RidgeContour contour)
    {
        var points = contour.Points;
        int spans = contour.IsClosed ? points.Count : points.Count - 1;

        float best = float.MaxValue;
        for (int i = 0; i < spans; i++)
        {
            var a = points[i];
            var ab = points[(i + 1) % points.Count] - a;
            float lengthSquared = ab.LengthSquared();
            float t = lengthSquared < 1e-12f
                ? 0f
                : Math.Clamp(Vector3.Dot(from - a, ab) / lengthSquared, 0f, 1f);
            best = MathF.Min(best, Vector3.Distance(from, a + (ab * t)));
        }
        return best;
    }
}

/// <summary>
/// Turns loose 3D segments into closed loops by welding endpoints on a grid. Used by the methods that
/// produce a level set or a set of slices rather than a walk, neither of which comes out ordered.
/// </summary>
internal static class SegmentChains
{
    /// <summary>
    /// As <see cref="Chain"/>, but keeps open runs as well as closed rings. Wanted when the surface
    /// the level set was taken over has had pieces cut out of it, so the set is expected to arrive in
    /// arcs with the gaps still to be bridged.
    /// </summary>
    public static List<(Vector3[] Points, bool Closed)> ChainAll(
        List<(Vector3 A, Vector3 B)> segments, float weld, int minimum = 4)
    {
        var (points, links) = Graph(segments, weld);
        var used = new HashSet<int>();
        var chains = new List<(Vector3[], bool)>();

        // Open runs first. Starting from a free end means an arc is walked from its end rather than
        // from the middle, which would split it into two.
        foreach (int start in links.Keys.Where(k => links[k].Count == 1).Concat(links.Keys))
        {
            if (used.Contains(start)) continue;

            var chain = new List<int> { start };
            used.Add(start);

            int current = start;
            while (true)
            {
                int next = -1;
                foreach (int candidate in links[current])
                    if (!used.Contains(candidate)) { next = candidate; break; }

                if (next < 0) break;

                used.Add(next);
                chain.Add(next);
                current = next;
            }

            if (chain.Count < minimum) continue;
            chains.Add((chain.Select(i => points[i]).ToArray(), links[current].Contains(start)));
        }

        return chains;
    }

    private static (List<Vector3> Points, Dictionary<int, List<int>> Links) Graph(
        List<(Vector3 A, Vector3 B)> segments, float weld)
    {
        var points = new List<Vector3>();
        var lookup = new Dictionary<(int, int, int), int>(segments.Count * 2);

        int Key(Vector3 p)
        {
            var cell = ((int)MathF.Round(p.X / weld), (int)MathF.Round(p.Y / weld),
                        (int)MathF.Round(p.Z / weld));
            if (lookup.TryGetValue(cell, out int found)) return found;

            lookup[cell] = points.Count;
            points.Add(p);
            return points.Count - 1;
        }

        var links = new Dictionary<int, List<int>>();
        foreach (var (a, b) in segments)
        {
            int i = Key(a), j = Key(b);
            if (i == j) continue;

            if (!links.TryGetValue(i, out var fi)) links[i] = fi = new List<int>(2);
            if (!links.TryGetValue(j, out var fj)) links[j] = fj = new List<int>(2);
            if (!fi.Contains(j)) fi.Add(j);
            if (!fj.Contains(i)) fj.Add(i);
        }

        return (points, links);
    }

    public static List<Vector3[]> Chain(List<(Vector3 A, Vector3 B)> segments, float weld)
    {
        if (segments.Count == 0) return new List<Vector3[]>();

        var points = new List<Vector3>();
        var lookup = new Dictionary<(int, int, int), int>(segments.Count * 2);

        int Key(Vector3 p)
        {
            var cell = ((int)MathF.Round(p.X / weld), (int)MathF.Round(p.Y / weld),
                        (int)MathF.Round(p.Z / weld));
            if (lookup.TryGetValue(cell, out int found)) return found;

            lookup[cell] = points.Count;
            points.Add(p);
            return points.Count - 1;
        }

        var links = new Dictionary<int, List<int>>();
        foreach (var (a, b) in segments)
        {
            int i = Key(a), j = Key(b);
            if (i == j) continue;

            if (!links.TryGetValue(i, out var fi)) links[i] = fi = new List<int>(2);
            if (!links.TryGetValue(j, out var fj)) links[j] = fj = new List<int>(2);
            if (!fi.Contains(j)) fi.Add(j);
            if (!fj.Contains(i)) fj.Add(i);
        }

        var used = new HashSet<int>();
        var loops = new List<Vector3[]>();

        foreach (int start in links.Keys)
        {
            if (used.Contains(start)) continue;

            var chain = new List<int> { start };
            used.Add(start);

            int current = start;
            while (true)
            {
                int next = -1;
                foreach (int candidate in links[current])
                    if (!used.Contains(candidate)) { next = candidate; break; }

                if (next < 0) break;

                used.Add(next);
                chain.Add(next);
                current = next;
            }

            // Only closed rings are wanted: an open chain is a level set running off the band's edge,
            // which is not a parting line however tidy it looks.
            if (chain.Count >= 16 && links[current].Contains(start))
                loops.Add(chain.Select(i => points[i]).ToArray());
        }

        return loops;
    }
}
