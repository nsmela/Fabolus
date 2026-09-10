using System.Numerics;

namespace GeometryManifold.Internal;

/// <summary>
/// Reduces a mesh to a target triangle count by quadric edge collapse, standing in for MeshLib's
/// decimateMesh. Manifold has no simplification of its own - its whole design is exactness, not
/// approximation - so the smoothing and mould features need this here.
/// </summary>
/// <remarks>
/// Garland-Heckbert quadrics: each vertex carries the summed squared distance to the planes of the
/// faces around it, so collapsing the edge with the smallest added error keeps the silhouette
/// while the flat interior gives way first.
/// </remarks>
internal static class MeshDecimator
{
    /// <summary>Below this a quadric matrix is too near-singular to solve for an optimal position.</summary>
    private const double SingularQuadric = 1e-12;

    public static (Vector3[] Vertices, int[] Triangles) Decimate(Vector3[] inputVertices, int[] inputTriangles, int targetTriangleCount)
    {
        var (vertices, triangles) = MeshExtensions.Weld(inputVertices, inputTriangles);
        int triangleCount = triangles.Length / 3;

        if (triangleCount <= targetTriangleCount || targetTriangleCount < 4)
            return MeshExtensions.Compact(vertices, triangles);

        var positions = vertices.Select(v => new Vector3d(v.X, v.Y, v.Z)).ToArray();
        var quadrics = new Quadric[positions.Length];
        var alive = new bool[triangleCount];
        Array.Fill(alive, true);

        // Vertices are merged by union-find rather than rewritten in place, so an edge collapse
        // stays O(1) instead of touching every triangle that used the vertex.
        var merged = new int[positions.Length];
        for (int i = 0; i < merged.Length; i++) merged[i] = i;

        int Find(int x)
        {
            while (merged[x] != x)
            {
                merged[x] = merged[merged[x]];
                x = merged[x];
            }
            return x;
        }

        for (int t = 0; t < triangleCount; t++)
        {
            var a = positions[triangles[t * 3]];
            var b = positions[triangles[t * 3 + 1]];
            var c = positions[triangles[t * 3 + 2]];

            var plane = Plane(a, b, c);
            if (plane is null) continue;

            var quadric = Quadric.FromPlane(plane.Value);
            quadrics[triangles[t * 3]] += quadric;
            quadrics[triangles[t * 3 + 1]] += quadric;
            quadrics[triangles[t * 3 + 2]] += quadric;
        }

        // Boundary edges get a heavy penalty so an open mesh keeps its rim.
        var edgeUse = new Dictionary<(int, int), int>();
        for (int t = 0; t < triangleCount; t++)
        {
            AddEdgeUse(edgeUse, triangles[t * 3], triangles[t * 3 + 1]);
            AddEdgeUse(edgeUse, triangles[t * 3 + 1], triangles[t * 3 + 2]);
            AddEdgeUse(edgeUse, triangles[t * 3 + 2], triangles[t * 3]);
        }

        var queue = new PriorityQueue<(int A, int B), double>();
        foreach (var (edge, uses) in edgeUse)
        {
            if (uses == 1) continue; // Boundary: never collapsed.
            queue.Enqueue(edge, CollapseCost(quadrics, positions, edge.Item1, edge.Item2));
        }

        var trianglesPerVertex = BuildVertexTriangles(triangles, positions.Length);

        int live = triangleCount;
        while (live > targetTriangleCount && queue.Count > 0)
        {
            var (a, b) = queue.Dequeue();

            int rootA = Find(a);
            int rootB = Find(b);
            if (rootA == rootB) continue; // Already collapsed together.

            var target = OptimalPosition(quadrics[rootA] + quadrics[rootB], positions[rootA], positions[rootB]);

            // Collapse b into a, then retire every triangle that has lost two distinct corners.
            merged[rootB] = rootA;
            positions[rootA] = target;
            quadrics[rootA] += quadrics[rootB];

            foreach (int t in trianglesPerVertex[rootB])
            {
                if (!alive[t]) continue;
                int v0 = Find(triangles[t * 3]);
                int v1 = Find(triangles[t * 3 + 1]);
                int v2 = Find(triangles[t * 3 + 2]);
                if (v0 == v1 || v1 == v2 || v2 == v0)
                {
                    alive[t] = false;
                    live--;
                }
            }

            // The merged vertex inherits b's incident triangles so later collapses see them.
            trianglesPerVertex[rootA].AddRange(trianglesPerVertex[rootB]);
            trianglesPerVertex[rootB].Clear();

            // Re-price the edges around the new vertex; stale entries are filtered on dequeue by
            // the root check above.
            var neighbours = new HashSet<int>();
            foreach (int t in trianglesPerVertex[rootA])
            {
                if (!alive[t]) continue;
                for (int i = 0; i < 3; i++)
                {
                    int neighbour = Find(triangles[t * 3 + i]);
                    if (neighbour != rootA) neighbours.Add(neighbour);
                }
            }
            foreach (int neighbour in neighbours)
            {
                queue.Enqueue((rootA, neighbour), CollapseCost(quadrics, positions, rootA, neighbour));
            }
        }

        var resultTriangles = new List<int>(live * 3);
        for (int t = 0; t < triangleCount; t++)
        {
            if (!alive[t]) continue;
            int v0 = Find(triangles[t * 3]);
            int v1 = Find(triangles[t * 3 + 1]);
            int v2 = Find(triangles[t * 3 + 2]);
            if (v0 == v1 || v1 == v2 || v2 == v0) continue;

            resultTriangles.Add(v0);
            resultTriangles.Add(v1);
            resultTriangles.Add(v2);
        }

        var resultVertices = positions.Select(p => new Vector3((float)p.X, (float)p.Y, (float)p.Z)).ToArray();
        return MeshExtensions.Compact(resultVertices, resultTriangles.ToArray());
    }

    private static List<int>[] BuildVertexTriangles(int[] triangles, int vertexCount)
    {
        var result = new List<int>[vertexCount];
        for (int i = 0; i < vertexCount; i++) result[i] = new List<int>();

        for (int t = 0; t < triangles.Length / 3; t++)
        {
            result[triangles[t * 3]].Add(t);
            result[triangles[t * 3 + 1]].Add(t);
            result[triangles[t * 3 + 2]].Add(t);
        }
        return result;
    }

    private static void AddEdgeUse(Dictionary<(int, int), int> edges, int a, int b)
    {
        var key = a < b ? (a, b) : (b, a);
        edges[key] = edges.TryGetValue(key, out int count) ? count + 1 : 1;
    }

    private static double CollapseCost(Quadric[] quadrics, Vector3d[] positions, int a, int b)
    {
        var quadric = quadrics[a] + quadrics[b];
        var target = OptimalPosition(quadric, positions[a], positions[b]);
        return Math.Max(0, quadric.Evaluate(target));
    }

    /// <summary>
    /// The position minimising the combined quadric, or the edge midpoint when the quadric is too
    /// flat to invert (a planar region, where any point on the edge costs the same anyway).
    /// </summary>
    private static Vector3d OptimalPosition(Quadric quadric, Vector3d a, Vector3d b)
    {
        if (quadric.TrySolve(out var solved)) return solved;

        var midpoint = (a + b) * 0.5;
        double costA = quadric.Evaluate(a);
        double costB = quadric.Evaluate(b);
        double costMid = quadric.Evaluate(midpoint);

        if (costMid <= costA && costMid <= costB) return midpoint;
        return costA <= costB ? a : b;
    }

    private static (double A, double B, double C, double D)? Plane(Vector3d a, Vector3d b, Vector3d c)
    {
        var normal = Vector3d.Cross(b - a, c - a);
        double length = normal.Length();
        if (length < 1e-14) return null;

        normal /= length;
        return (normal.X, normal.Y, normal.Z, -Vector3d.Dot(normal, a));
    }

    /// <summary>Symmetric 4x4 error quadric, stored as its ten distinct entries.</summary>
    private struct Quadric
    {
        public double Q00, Q01, Q02, Q03, Q11, Q12, Q13, Q22, Q23, Q33;

        public static Quadric FromPlane((double A, double B, double C, double D) plane)
        {
            var (a, b, c, d) = plane;
            return new Quadric
            {
                Q00 = a * a, Q01 = a * b, Q02 = a * c, Q03 = a * d,
                Q11 = b * b, Q12 = b * c, Q13 = b * d,
                Q22 = c * c, Q23 = c * d,
                Q33 = d * d,
            };
        }

        public static Quadric operator +(Quadric x, Quadric y) => new()
        {
            Q00 = x.Q00 + y.Q00, Q01 = x.Q01 + y.Q01, Q02 = x.Q02 + y.Q02, Q03 = x.Q03 + y.Q03,
            Q11 = x.Q11 + y.Q11, Q12 = x.Q12 + y.Q12, Q13 = x.Q13 + y.Q13,
            Q22 = x.Q22 + y.Q22, Q23 = x.Q23 + y.Q23,
            Q33 = x.Q33 + y.Q33,
        };

        public readonly double Evaluate(Vector3d v) =>
            Q00 * v.X * v.X + 2 * Q01 * v.X * v.Y + 2 * Q02 * v.X * v.Z + 2 * Q03 * v.X +
            Q11 * v.Y * v.Y + 2 * Q12 * v.Y * v.Z + 2 * Q13 * v.Y +
            Q22 * v.Z * v.Z + 2 * Q23 * v.Z +
            Q33;

        /// <summary>Solves the 3x3 upper block for the minimising point, by Cramer's rule.</summary>
        public readonly bool TrySolve(out Vector3d result)
        {
            result = default;

            double determinant =
                Q00 * (Q11 * Q22 - Q12 * Q12) -
                Q01 * (Q01 * Q22 - Q12 * Q02) +
                Q02 * (Q01 * Q12 - Q11 * Q02);

            if (Math.Abs(determinant) < SingularQuadric) return false;

            double x = -(Q03 * (Q11 * Q22 - Q12 * Q12) - Q01 * (Q13 * Q22 - Q12 * Q23) + Q02 * (Q13 * Q12 - Q11 * Q23));
            double y = -(Q00 * (Q13 * Q22 - Q12 * Q23) - Q03 * (Q01 * Q22 - Q02 * Q12) + Q02 * (Q01 * Q23 - Q13 * Q02));
            double z = -(Q00 * (Q11 * Q23 - Q13 * Q12) - Q01 * (Q01 * Q23 - Q13 * Q02) + Q03 * (Q01 * Q12 - Q11 * Q02));

            result = new Vector3d(x / determinant, y / determinant, z / determinant);
            return !double.IsNaN(result.X) && !double.IsNaN(result.Y) && !double.IsNaN(result.Z);
        }
    }

    /// <summary>
    /// Double-precision vector. Quadric error accumulates over thousands of collapses, and in
    /// single precision that drift shows up as visible creases on a decimated bolus.
    /// </summary>
    private readonly struct Vector3d
    {
        public readonly double X, Y, Z;

        public Vector3d(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vector3d operator -(Vector3d a, Vector3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector3d operator +(Vector3d a, Vector3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3d operator *(Vector3d a, double s) => new(a.X * s, a.Y * s, a.Z * s);
        public static Vector3d operator /(Vector3d a, double s) => new(a.X / s, a.Y / s, a.Z / s);

        public static Vector3d Cross(Vector3d a, Vector3d b) =>
            new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        public static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);
    }
}
