using System.Numerics;
using Fabolus.Core.Geometry;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// Face adjacency and edge geometry for a body, built from a run's edge trace so nothing here has to
/// weld the mesh a second time.
/// </summary>
internal sealed class RidgeTopology
{
    public RidgeEdgeAdmission[] Edges { get; }
    public List<int>[] FaceEdges { get; }
    public List<int>[] VertexEdges { get; }
    public Dictionary<(int, int), int> Index { get; }
    public int FaceCount { get; }

    public RidgeTopology(RidgeEdgeAdmission[] edges, int faceCount)
    {
        Edges = edges;
        FaceCount = faceCount;
        FaceEdges = new List<int>[faceCount];
        VertexEdges = new List<int>[edges.Max(e => Math.Max(e.A, e.B)) + 1];
        Index = new Dictionary<(int, int), int>(edges.Length);

        for (int f = 0; f < faceCount; f++) FaceEdges[f] = new List<int>(3);

        for (int i = 0; i < edges.Length; i++)
        {
            Index[edges[i].Key] = i;
            if (edges[i].FaceA >= 0) FaceEdges[edges[i].FaceA].Add(i);
            if (edges[i].FaceB >= 0) FaceEdges[edges[i].FaceB].Add(i);

            (VertexEdges[edges[i].A] ??= new List<int>(6)).Add(i);
            (VertexEdges[edges[i].B] ??= new List<int>(6)).Add(i);
        }
    }

    public int Across(int edge, int face) =>
        Edges[edge].FaceA == face ? Edges[edge].FaceB : Edges[edge].FaceA;

    /// <summary>The mean of a triangle's three edge midpoints is its centroid exactly.</summary>
    public Vector3[] Centroids()
    {
        var centroid = new Vector3[FaceCount];
        for (int f = 0; f < FaceCount; f++)
        {
            if (FaceEdges[f].Count == 0) continue;

            var sum = Vector3.Zero;
            foreach (int e in FaceEdges[f]) sum += Edges[e].Mid;
            centroid[f] = sum / FaceEdges[f].Count;
        }
        return centroid;
    }
}

internal sealed record RidgeReplayed(
    int[] Region, bool[] Filled, bool[] IsBand, bool[] Shaded, int First, int Second, int RegionCount);

/// <summary>
/// Replays the fill and the band classification over a wall set chosen by the caller.
///
/// <para>
/// The detector only ever answers for the walls its own thresholds produced, and the questions worth
/// asking are about walls it would not have chosen: what a rule admits here, what admitting only part
/// of it would do, which of the downstream tests is the one that actually refuses a face. Replaying
/// keeps every threshold where it is and varies the one thing under study, which is what makes the
/// step a face falls out at readable.
/// </para>
///
/// <para>
/// It mirrors <c>FillEnclosedRegions</c> and <c>Classify</c>, including the detail that a ridge with
/// the same region either side counts once towards that region's perimeter rather than twice. It does
/// not mirror <c>CloseBandHoles</c>, which reassigns regions afterwards - so a replay over the
/// detector's own walls comes back a handful of faces under the real run, and that gap is the check
/// that the rest of it is faithful.
/// </para>
/// </summary>
internal static class RidgeReplay
{
    public static RidgeReplayed Run(
        RidgeTopology mesh, HashSet<int> walls, float[] area, float totalArea, float diagonal,
        RidgeDetectionOptions options)
    {
        var edges = mesh.Edges;
        int faceCount = mesh.FaceCount;

        var region = new int[faceCount];
        Array.Fill(region, -1);

        var stack = new Stack<int>();
        int regionCount = 0;
        for (int seed = 0; seed < faceCount; seed++)
        {
            if (region[seed] >= 0) continue;

            region[seed] = regionCount;
            stack.Push(seed);
            while (stack.Count > 0)
            {
                int face = stack.Pop();
                foreach (int e in mesh.FaceEdges[face])
                {
                    if (walls.Contains(e)) continue;

                    int across = mesh.Across(e, face);
                    if (across < 0 || region[across] >= 0) continue;

                    region[across] = regionCount;
                    stack.Push(across);
                }
            }
            regionCount++;
        }

        var regionArea = new float[regionCount];
        var perimeter = new float[regionCount];
        for (int f = 0; f < faceCount; f++) regionArea[region[f]] += area[f];

        var shaded = new bool[faceCount];
        foreach (int index in walls)
        {
            var edge = edges[index];
            shaded[edge.FaceA] = true;
            if (edge.FaceB >= 0) shaded[edge.FaceB] = true;

            perimeter[region[edge.FaceA]] += edge.Length;
            if (edge.FaceB >= 0 && region[edge.FaceB] != region[edge.FaceA])
                perimeter[region[edge.FaceB]] += edge.Length;
        }

        float maxArea = options.MaxRegionAreaFraction * totalArea;
        float maxWidth = options.MaxRegionWidthFraction * diagonal;

        var filled = new bool[regionCount];
        for (int r = 0; r < regionCount; r++)
            filled[r] = regionArea[r] < maxArea && perimeter[r] > 1e-6f
                        && 2f * regionArea[r] / perimeter[r] < maxWidth;

        for (int f = 0; f < faceCount; f++)
            if (filled[region[f]]) shaded[f] = true;

        // ---- Classify ----
        int first = 0, second = -1;
        for (int r = 1; r < regionCount; r++)
            if (regionArea[r] > regionArea[first]) first = r;
        for (int r = 0; r < regionCount; r++)
            if (r != first && (second < 0 || regionArea[r] > regionArea[second])) second = r;

        var neighbours = new Dictionary<int, HashSet<int>>();
        var touchesFirst = new bool[regionCount];
        var touchesSecond = new bool[regionCount];

        foreach (int index in walls)
        {
            var edge = edges[index];
            if (edge.FaceB < 0) continue;

            int left = region[edge.FaceA];
            int right = region[edge.FaceB];
            if (left == right) continue;

            if (left == first) touchesFirst[right] = true;
            if (left == second) touchesSecond[right] = true;
            if (right == first) touchesFirst[left] = true;
            if (right == second) touchesSecond[left] = true;

            if (left == first || left == second || right == first || right == second) continue;

            Link(neighbours, left, right);
            Link(neighbours, right, left);
        }

        var isBand = new bool[regionCount];
        var group = new int[regionCount];
        Array.Fill(group, -1);
        var members = new List<int>();

        for (int seed = 0; seed < regionCount; seed++)
        {
            if (seed == first || seed == second || group[seed] >= 0) continue;

            members.Clear();
            group[seed] = seed;
            stack.Push(seed);

            bool reachesFirst = false, reachesSecond = false;
            while (stack.Count > 0)
            {
                int current = stack.Pop();
                members.Add(current);
                reachesFirst |= touchesFirst[current];
                reachesSecond |= touchesSecond[current];

                if (!neighbours.TryGetValue(current, out var adjacent)) continue;
                foreach (int next in adjacent)
                {
                    if (group[next] >= 0) continue;
                    group[next] = seed;
                    stack.Push(next);
                }
            }

            if (!reachesFirst || !reachesSecond) continue;
            foreach (int member in members) isBand[member] = true;
        }

        return new RidgeReplayed(region, filled, isBand, shaded, first, second, regionCount);

        static void Link(Dictionary<int, HashSet<int>> map, int key, int value)
        {
            if (!map.TryGetValue(key, out var set)) map[key] = set = new HashSet<int>(2);
            set.Add(value);
        }
    }

    /// <summary>Every edge either run measured, so a wall set can be drawn from both.</summary>
    public static RidgeTopology Merge(int faceCount, params RidgeDiagnosis[] runs)
    {
        var edges = new Dictionary<(int, int), RidgeEdgeAdmission>();
        foreach (var run in runs)
            foreach (var edge in run.Edges)
                edges.TryAdd(edge.Key, edge);

        return new RidgeTopology(edges.Values.ToArray(), faceCount);
    }

    public static float[] FaceAreas(IMesh mesh)
    {
        var area = new float[mesh.Triangles.Length / 3];
        for (int t = 0; t < area.Length; t++)
        {
            var a = mesh.Vertices[mesh.Triangles[t * 3]];
            var b = mesh.Vertices[mesh.Triangles[(t * 3) + 1]];
            var c = mesh.Vertices[mesh.Triangles[(t * 3) + 2]];
            area[t] = Vector3.Cross(b - a, c - a).Length() * 0.5f;
        }
        return area;
    }

    public static float Diagonal(IMesh mesh)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in mesh.Vertices)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }
        return (max - min).Length();
    }

    public static float Nearest(Vector3 point, Vector3[] targets)
    {
        float best = float.MaxValue;
        foreach (var other in targets) best = MathF.Min(best, Vector3.DistanceSquared(point, other));
        return MathF.Sqrt(best);
    }
}
