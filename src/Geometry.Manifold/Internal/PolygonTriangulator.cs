using System.Numerics;

namespace GeometryManifold.Internal;

/// <summary>
/// Triangulates closed 2D contours, holes included, replacing MeshLib's
/// MR.PlanarTriangulation.triangulateContours. The extrusion paths need the triangles themselves
/// (they lift each vertex to its own Z), so Manifold's own Extrude - which triangulates internally
/// and hands back only a finished solid - cannot stand in here.
/// </summary>
internal static class PolygonTriangulator
{
    /// <summary>Below this a signed area is noise rather than a contour.</summary>
    private const float MinContourArea = 1e-9f;

    /// <summary>
    /// Triangulates the given contours. Outer contours and holes are told apart by containment,
    /// not by the order they arrive in, matching what MeshLib inferred from winding. The returned
    /// triangles are counter-clockwise.
    /// </summary>
    public static (List<Vector2> Points, List<(int A, int B, int C)> Triangles) Triangulate(
        IReadOnlyList<IReadOnlyList<Vector2>> contours)
    {
        var points = new List<Vector2>();
        var triangles = new List<(int, int, int)>();

        var rings = new List<List<Vector2>>();
        foreach (var contour in contours)
        {
            var ring = Clean(contour);
            if (ring.Count >= 3 && MathF.Abs(SignedArea(ring)) > MinContourArea) rings.Add(ring);
        }

        if (rings.Count == 0) return (points, triangles);

        // Classify: a ring nested inside an odd number of other rings is a hole.
        var isHole = new bool[rings.Count];
        for (int i = 0; i < rings.Count; i++)
        {
            int depth = 0;
            for (int j = 0; j < rings.Count; j++)
            {
                if (i == j) continue;
                if (Contains(rings[j], rings[i][0])) depth++;
            }
            isHole[i] = (depth & 1) == 1;
        }

        for (int i = 0; i < rings.Count; i++)
        {
            if (isHole[i]) continue;

            var holes = new List<List<Vector2>>();
            for (int j = 0; j < rings.Count; j++)
            {
                // Only holes directly inside this outer ring; a nested island's own holes belong
                // to that island, not to this one.
                if (!isHole[j] || !Contains(rings[i], rings[j][0])) continue;

                bool nestedDeeper = false;
                for (int k = 0; k < rings.Count; k++)
                {
                    if (k == i || isHole[k]) continue;
                    if (Contains(rings[i], rings[k][0]) && Contains(rings[k], rings[j][0]))
                    {
                        nestedDeeper = true;
                        break;
                    }
                }
                if (!nestedDeeper) holes.Add(rings[j]);
            }

            TriangulateWithHoles(rings[i], holes, points, triangles);
        }

        return (points, triangles);
    }

    private static void TriangulateWithHoles(
        List<Vector2> outer,
        List<List<Vector2>> holes,
        List<Vector2> points,
        List<(int, int, int)> triangles)
    {
        // Orient once so the ear test below only ever has to consider one winding.
        var ring = new List<Vector2>(outer);
        if (SignedArea(ring) < 0) ring.Reverse();

        foreach (var hole in holes.OrderByDescending(h => h.Max(p => p.X)))
        {
            var oriented = new List<Vector2>(hole);
            if (SignedArea(oriented) > 0) oriented.Reverse(); // Holes wind against the outer ring.
            ring = BridgeHole(ring, oriented);
        }

        int baseIndex = points.Count;
        points.AddRange(ring);

        var clipped = EarClip(ring);
        DelaunayFlip(ring, clipped);

        foreach (var (a, b, c) in clipped)
        {
            triangles.Add((baseIndex + a, baseIndex + b, baseIndex + c));
        }
    }

    /// <summary>
    /// Flips shared edges until no triangle's circumcircle contains the opposite vertex, turning
    /// the ear-clipped fan into a Delaunay triangulation.
    /// </summary>
    /// <remarks>
    /// Skipping this is what MeshLib's triangulator never made us think about. Ear clipping is
    /// free to emit slivers spanning the whole outline, and the decal builder lifts each vertex
    /// onto a curved surface independently - so a sliver reaching right across a wrapped label
    /// cuts straight through the geometry beside it, and the prism comes back self-intersecting.
    /// </remarks>
    private static void DelaunayFlip(List<Vector2> ring, List<(int A, int B, int C)> triangles)
    {
        // Passes are capped because a flip can in principle undo an earlier one on degenerate
        // input; in practice this settles in a handful.
        const int maxPasses = 12;

        for (int pass = 0; pass < maxPasses; pass++)
        {
            // Map each interior edge to the two triangles sharing it.
            var edgeOwners = new Dictionary<(int, int), (int First, int Second)>();
            for (int t = 0; t < triangles.Count; t++)
            {
                var (a, b, c) = triangles[t];
                Register(edgeOwners, a, b, t);
                Register(edgeOwners, b, c, t);
                Register(edgeOwners, c, a, t);
            }

            bool flipped = false;
            foreach (var (edge, owners) in edgeOwners)
            {
                if (owners.Second < 0) continue;

                var (t0, t1) = (owners.First, owners.Second);
                int opposite0 = Opposite(triangles[t0], edge);
                int opposite1 = Opposite(triangles[t1], edge);
                if (opposite0 < 0 || opposite1 < 0) continue;

                var p = ring[edge.Item1];
                var q = ring[edge.Item2];
                var r = ring[opposite0];
                var s = ring[opposite1];

                if (!InCircumcircle(p, q, r, s)) continue;

                // The flipped quad must stay convex, or the swap would fold the pair over.
                if (Cross(r, p, s) <= 0 || Cross(r, s, q) <= 0) continue;

                triangles[t0] = (opposite0, edge.Item1, opposite1);
                triangles[t1] = (opposite1, edge.Item2, opposite0);
                flipped = true;
                break; // The owner map is stale now; rebuild it.
            }

            if (!flipped) return;
        }
    }

    private static void Register(Dictionary<(int, int), (int First, int Second)> owners, int a, int b, int triangle)
    {
        var key = a < b ? (a, b) : (b, a);
        if (owners.TryGetValue(key, out var existing))
        {
            if (existing.Second < 0) owners[key] = (existing.First, triangle);
        }
        else
        {
            owners[key] = (triangle, -1);
        }
    }

    /// <summary>The corner of the triangle that is not on the given edge, or -1 if it has none.</summary>
    private static int Opposite((int A, int B, int C) triangle, (int, int) edge)
    {
        if (triangle.A != edge.Item1 && triangle.A != edge.Item2) return triangle.A;
        if (triangle.B != edge.Item1 && triangle.B != edge.Item2) return triangle.B;
        if (triangle.C != edge.Item1 && triangle.C != edge.Item2) return triangle.C;
        return -1;
    }

    /// <summary>True when <paramref name="d"/> falls inside the circle through the other three.</summary>
    private static bool InCircumcircle(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        // The standard determinant test, which only holds for a counter-clockwise a-b-c.
        if (Cross(a, b, c) < 0) (b, c) = (c, b);

        double ax = a.X - d.X, ay = a.Y - d.Y;
        double bx = b.X - d.X, by = b.Y - d.Y;
        double cx = c.X - d.X, cy = c.Y - d.Y;

        double determinant =
            (ax * ax + ay * ay) * (bx * cy - cx * by) -
            (bx * bx + by * by) * (ax * cy - cx * ay) +
            (cx * cx + cy * cy) * (ax * by - bx * ay);

        return determinant > 1e-12;
    }

    /// <summary>
    /// Cuts a hole into its containing ring along a mutually visible pair of vertices, turning the
    /// two closed loops into one. The doubled-back bridge edge is what makes the combined loop
    /// simple enough to ear-clip.
    /// </summary>
    private static List<Vector2> BridgeHole(List<Vector2> outer, List<Vector2> hole)
    {
        // Start from the hole's rightmost vertex: the ray cast from it leaves the hole
        // immediately, so the first outer edge it meets is genuinely between the two rings.
        int holeIndex = 0;
        for (int i = 1; i < hole.Count; i++)
        {
            if (hole[i].X > hole[holeIndex].X) holeIndex = i;
        }

        var start = hole[holeIndex];
        int bridgeIndex = -1;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < outer.Count; i++)
        {
            var a = outer[i];
            var b = outer[(i + 1) % outer.Count];

            // Only edges the rightward ray can cross, and only ahead of the start point.
            if ((a.Y > start.Y) == (b.Y > start.Y)) continue;

            float t = (start.Y - a.Y) / (b.Y - a.Y);
            float x = a.X + t * (b.X - a.X);
            if (x <= start.X) continue;

            float distance = x - start.X;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bridgeIndex = a.X > b.X ? i : (i + 1) % outer.Count;
            }
        }

        if (bridgeIndex < 0)
        {
            // No visible edge (the hole is not actually inside): leave the outer ring untouched
            // rather than stitching a bridge that would cross it.
            return outer;
        }

        var merged = new List<Vector2>(outer.Count + hole.Count + 2);
        for (int i = 0; i <= bridgeIndex; i++) merged.Add(outer[i]);
        for (int i = 0; i < hole.Count; i++) merged.Add(hole[(holeIndex + i) % hole.Count]);
        merged.Add(hole[holeIndex]);
        for (int i = bridgeIndex; i < outer.Count; i++) merged.Add(outer[i]);

        return merged;
    }

    /// <summary>Ear clipping on a counter-clockwise simple polygon.</summary>
    private static List<(int A, int B, int C)> EarClip(List<Vector2> ring)
    {
        var triangles = new List<(int, int, int)>();
        int count = ring.Count;
        if (count < 3) return triangles;

        var indices = new List<int>(count);
        for (int i = 0; i < count; i++) indices.Add(i);

        // Each failed sweep of the remaining vertices means no ear was found; bail rather than
        // spin forever on input that is not simple.
        int guard = count * count;

        while (indices.Count > 3 && guard-- > 0)
        {
            int bestSlot = -1;
            float bestQuality = float.MinValue;

            for (int i = 0; i < indices.Count; i++)
            {
                int previous = indices[(i - 1 + indices.Count) % indices.Count];
                int current = indices[i];
                int next = indices[(i + 1) % indices.Count];

                var a = ring[previous];
                var b = ring[current];
                var c = ring[next];

                if (Cross(a, b, c) <= 0) continue; // Reflex or collinear: not an ear.

                bool contains = false;
                foreach (int other in indices)
                {
                    if (other == previous || other == current || other == next) continue;
                    if (PointInTriangle(ring[other], a, b, c))
                    {
                        contains = true;
                        break;
                    }
                }
                if (contains) continue;

                // Take the roundest ear available rather than the first one round the loop.
                // Clipping in index order walks the outline and leaves a fan of slivers behind
                // it, and slivers are what turn into self-intersections once the decal builder
                // lifts each vertex onto a curved surface.
                float quality = Roundness(a, b, c);
                if (quality > bestQuality)
                {
                    bestQuality = quality;
                    bestSlot = i;
                }
            }

            bool clipped = bestSlot >= 0;
            if (clipped)
            {
                int previous = indices[(bestSlot - 1 + indices.Count) % indices.Count];
                int current = indices[bestSlot];
                int next = indices[(bestSlot + 1) % indices.Count];

                triangles.Add((previous, current, next));
                indices.RemoveAt(bestSlot);
            }

            if (!clipped)
            {
                // Only collinear or self-touching vertices are left. Fan the remainder: it can
                // add slivers but never leaves the extrusion with a hole in its cap.
                break;
            }
        }

        if (indices.Count == 3)
        {
            triangles.Add((indices[0], indices[1], indices[2]));
        }
        else if (indices.Count > 3)
        {
            for (int i = 1; i < indices.Count - 1; i++)
            {
                triangles.Add((indices[0], indices[i], indices[i + 1]));
            }
        }

        return triangles;
    }

    private static List<Vector2> Clean(IReadOnlyList<Vector2> contour)
    {
        const float epsilonSquared = 1e-14f;
        var cleaned = new List<Vector2>(contour.Count);

        foreach (var point in contour)
        {
            if (cleaned.Count > 0 && (cleaned[^1] - point).LengthSquared() < epsilonSquared) continue;
            cleaned.Add(point);
        }

        while (cleaned.Count > 1 && (cleaned[0] - cleaned[^1]).LengthSquared() < epsilonSquared)
        {
            cleaned.RemoveAt(cleaned.Count - 1);
        }

        return cleaned;
    }

    public static float SignedArea(IReadOnlyList<Vector2> ring)
    {
        double sum = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            sum += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        return (float)(sum * 0.5);
    }

    private static bool Contains(IReadOnlyList<Vector2> ring, Vector2 point)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var a = ring[i];
            var b = ring[j];
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>
    /// How close a triangle is to equilateral, as area over the sum of its squared sides. Peaks
    /// for an equilateral triangle and tends to zero for a sliver, so maximising it picks the
    /// best-shaped ear.
    /// </summary>
    private static float Roundness(Vector2 a, Vector2 b, Vector2 c)
    {
        float sumOfSquares =
            (b - a).LengthSquared() +
            (c - b).LengthSquared() +
            (a - c).LengthSquared();

        if (sumOfSquares < 1e-20f) return 0f;
        return Cross(a, b, c) / sumOfSquares;
    }

    private static float Cross(Vector2 a, Vector2 b, Vector2 c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(a, b, p);
        float d2 = Cross(b, c, p);
        float d3 = Cross(c, a, p);
        return d1 >= 0 && d2 >= 0 && d3 >= 0;
    }
}
