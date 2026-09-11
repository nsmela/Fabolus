using System.Numerics;

namespace GeometryManifold.Internal;

/// <summary>
/// Bounding-volume hierarchy over a triangle soup, supplying the spatial queries Manifold does not
/// expose: ray intersection and closest-point-on-surface. MeshLib gave these away for free
/// (ObjectMesh.worldRayIntersection, findSignedDistance); on Manifold they have to be built here,
/// and the decal and deviation-colour features are unusable without them.
/// </summary>
internal sealed class MeshBvh
{
    /// <summary>Leaves hold at most this many triangles; below it the traversal costs more than the scan.</summary>
    private const int LeafSize = 8;

    private readonly Vector3[] _vertices;
    private readonly int[] _triangles;
    private readonly int[] _triangleIndices;
    private readonly Node[] _nodes;
    private readonly int _nodeCount;

    /// <summary>
    /// Traversal scratch space. Every query below is a stack-driven descent, and allocating a
    /// fresh <see cref="Stack{T}"/> for each one dominated the offset cost - the level-set mesher
    /// issues one query per voxel. It is thread-static because Manifold may call the distance
    /// callback from several threads at once.
    /// </summary>
    [ThreadStatic]
    private static int[]? _traversalStack;

    private static int[] RentStack(int depth)
    {
        if (_traversalStack is null || _traversalStack.Length < depth)
        {
            _traversalStack = new int[Math.Max(64, depth)];
        }
        return _traversalStack;
    }

    private struct Node
    {
        public Vector3 Min;
        public Vector3 Max;
        public int Start;
        public int Count;
        public int Left;
        public int Right;
    }

    public MeshBvh(Vector3[] vertices, int[] triangles)
    {
        _vertices = vertices;
        _triangles = triangles;

        int triangleCount = triangles.Length / 3;
        _triangleIndices = new int[triangleCount];
        for (int i = 0; i < triangleCount; i++) _triangleIndices[i] = i;

        // A binary tree over n leaves of at least one triangle needs fewer than 2n nodes.
        _nodes = new Node[Math.Max(1, triangleCount * 2)];
        _nodeCount = 0;

        if (triangleCount > 0)
        {
            Build(0, triangleCount, ref _nodeCount);
        }
    }

    public bool IsEmpty => _nodeCount == 0;

    private int Build(int start, int count, ref int nodeCount)
    {
        int nodeIndex = nodeCount++;
        var node = new Node { Start = start, Count = count, Left = -1, Right = -1 };

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (int i = start; i < start + count; i++)
        {
            GetTriangle(_triangleIndices[i], out var a, out var b, out var c);
            min = Vector3.Min(min, Vector3.Min(a, Vector3.Min(b, c)));
            max = Vector3.Max(max, Vector3.Max(a, Vector3.Max(b, c)));
        }
        node.Min = min;
        node.Max = max;

        if (count > LeafSize)
        {
            // Split down the middle of the widest axis - cheap, and good enough for meshes that
            // are already spatially coherent.
            var extent = max - min;
            int axis = extent.X > extent.Y ? (extent.X > extent.Z ? 0 : 2) : (extent.Y > extent.Z ? 1 : 2);
            float split = Axis(min + extent * 0.5f, axis);

            int mid = Partition(start, count, axis, split);
            if (mid == start || mid == start + count)
            {
                mid = start + count / 2; // Degenerate split (all centroids coincide): halve by count.
            }

            _nodes[nodeIndex] = node;
            node.Left = Build(start, mid - start, ref nodeCount);
            node.Right = Build(mid, start + count - mid, ref nodeCount);
            node.Count = 0;
        }

        _nodes[nodeIndex] = node;
        return nodeIndex;
    }

    private int Partition(int start, int count, int axis, float split)
    {
        int i = start;
        int j = start + count - 1;
        while (i <= j)
        {
            GetTriangle(_triangleIndices[i], out var a, out var b, out var c);
            float centroid = Axis((a + b + c) / 3f, axis);
            if (centroid < split)
            {
                i++;
            }
            else
            {
                (_triangleIndices[i], _triangleIndices[j]) = (_triangleIndices[j], _triangleIndices[i]);
                j--;
            }
        }
        return i;
    }

    private static float Axis(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    private void GetTriangle(int triangle, out Vector3 a, out Vector3 b, out Vector3 c)
    {
        a = _vertices[_triangles[triangle * 3]];
        b = _vertices[_triangles[triangle * 3 + 1]];
        c = _vertices[_triangles[triangle * 3 + 2]];
    }

    /// <summary>
    /// Closest intersection of a ray with the surface. <paramref name="direction"/> must be a unit
    /// vector. Returns false when nothing is hit in front of the origin.
    /// </summary>
    public bool Raycast(Vector3 origin, Vector3 direction, out float distance, out int triangle)
    {
        distance = float.MaxValue;
        triangle = -1;
        if (IsEmpty) return false;

        var inverseDirection = new Vector3(
            1f / (direction.X == 0f ? 1e-20f : direction.X),
            1f / (direction.Y == 0f ? 1e-20f : direction.Y),
            1f / (direction.Z == 0f ? 1e-20f : direction.Z));

        var stack = RentStack(_nodeCount + 2);
        int top = 0;
        stack[top++] = 0;

        while (top > 0)
        {
            int index = stack[--top];
            ref var node = ref _nodes[index];

            if (!IntersectsBox(origin, inverseDirection, node.Min, node.Max, distance)) continue;

            if (node.Count > 0)
            {
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    int candidate = _triangleIndices[i];
                    GetTriangle(candidate, out var a, out var b, out var c);
                    if (RayTriangle(origin, direction, a, b, c, out float t) && t < distance)
                    {
                        distance = t;
                        triangle = candidate;
                    }
                }
                continue;
            }

            if (node.Left >= 0) stack[top++] = node.Left;
            if (node.Right >= 0) stack[top++] = node.Right;
        }

        return triangle >= 0;
    }

    /// <summary>
    /// Nearest point on the surface to <paramref name="point"/>, and the triangle carrying it.
    /// </summary>
    public bool ClosestPoint(Vector3 point, out Vector3 closest, out int triangle, out float distance)
    {
        closest = point;
        triangle = -1;
        distance = float.MaxValue;
        if (IsEmpty) return false;

        float bestSquared = float.MaxValue;
        var stack = RentStack(_nodeCount + 2);
        int top = 0;
        stack[top++] = 0;

        while (top > 0)
        {
            int index = stack[--top];
            ref var node = ref _nodes[index];

            // Prune on the box: nothing inside can beat a hit closer than the box itself.
            float boxDistance = SquaredDistanceToBox(point, node.Min, node.Max);
            if (boxDistance > bestSquared) continue;

            if (node.Count > 0)
            {
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    int candidate = _triangleIndices[i];
                    GetTriangle(candidate, out var a, out var b, out var c);
                    var projected = ClosestPointOnTriangle(point, a, b, c);
                    float squared = (projected - point).LengthSquared();
                    if (squared < bestSquared)
                    {
                        bestSquared = squared;
                        closest = projected;
                        triangle = candidate;
                    }
                }
                continue;
            }

            if (node.Left >= 0) stack[top++] = node.Left;
            if (node.Right >= 0) stack[top++] = node.Right;
        }

        distance = MathF.Sqrt(bestSquared);
        return triangle >= 0;
    }

    /// <summary>
    /// Distance to the surface, negative inside the solid.
    /// </summary>
    /// <remarks>
    /// The sign comes from which side of the nearest triangle the point falls on, not from a
    /// crossing-parity ray cast. Parity is more robust around sharp creases, but it costs a full
    /// traversal per query on top of the closest-point search, and the level-set offset asks for
    /// hundreds of thousands of these - the pseudo-normal test is what keeps offsetting usable.
    /// </remarks>
    public float SignedDistance(Vector3 point)
    {
        if (!ClosestPoint(point, out var closest, out int triangle, out float distance)) return float.MaxValue;

        var normal = TriangleNormal(triangle);
        if (normal == Vector3.Zero) return distance;

        return Vector3.Dot(point - closest, normal) < 0f ? -distance : distance;
    }

    /// <summary>
    /// Parity test along +X. Assumes a closed mesh; on an open one the answer is meaningless, the
    /// same way MeshLib's signed distance is.
    /// </summary>
    public bool IsInside(Vector3 point)
    {
        if (IsEmpty) return false;

        int crossings = 0;
        var direction = Vector3.UnitX;
        var inverseDirection = new Vector3(1f, 1e20f, 1e20f);

        var stack = RentStack(_nodeCount + 2);
        int top = 0;
        stack[top++] = 0;

        while (top > 0)
        {
            int index = stack[--top];
            ref var node = ref _nodes[index];

            if (!IntersectsBox(point, inverseDirection, node.Min, node.Max, float.MaxValue)) continue;

            if (node.Count > 0)
            {
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    GetTriangle(_triangleIndices[i], out var a, out var b, out var c);
                    if (RayTriangle(point, direction, a, b, c, out _)) crossings++;
                }
                continue;
            }

            if (node.Left >= 0) stack[top++] = node.Left;
            if (node.Right >= 0) stack[top++] = node.Right;
        }

        return (crossings & 1) == 1;
    }

    /// <summary>
    /// Calls <paramref name="onOverlap"/> for every triangle whose bounding box overlaps the given
    /// box. The broad phase for self-intersection testing.
    /// </summary>
    public void QueryBox(Vector3 min, Vector3 max, Action<int> onOverlap)
    {
        if (IsEmpty) return;

        // Its own stack, not the pooled one: this is the only traversal that hands control back
        // to a caller mid-descent, and a callback that queried the tree again would walk over
        // the shared buffer underneath it.
        var stack = new int[_nodeCount + 2];
        int top = 0;
        stack[top++] = 0;

        while (top > 0)
        {
            int index = stack[--top];
            ref var node = ref _nodes[index];

            if (node.Min.X > max.X || node.Max.X < min.X ||
                node.Min.Y > max.Y || node.Max.Y < min.Y ||
                node.Min.Z > max.Z || node.Max.Z < min.Z) continue;

            if (node.Count > 0)
            {
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    onOverlap(_triangleIndices[i]);
                }
                continue;
            }

            if (node.Left >= 0) stack[top++] = node.Left;
            if (node.Right >= 0) stack[top++] = node.Right;
        }
    }

    /// <summary>Geometric normal of a triangle, or <see cref="Vector3.Zero"/> if it is degenerate.</summary>
    public Vector3 TriangleNormal(int triangle)
    {
        GetTriangle(triangle, out var a, out var b, out var c);
        var cross = Vector3.Cross(b - a, c - a);
        return cross.LengthSquared() > 1e-16f ? Vector3.Normalize(cross) : Vector3.Zero;
    }

    public void GetTriangleVertices(int triangle, out Vector3 a, out Vector3 b, out Vector3 c) =>
        GetTriangle(triangle, out a, out b, out c);

    private static bool IntersectsBox(Vector3 origin, Vector3 inverseDirection, Vector3 min, Vector3 max, float maxDistance)
    {
        float t1 = (min.X - origin.X) * inverseDirection.X;
        float t2 = (max.X - origin.X) * inverseDirection.X;
        float tMin = MathF.Min(t1, t2);
        float tMax = MathF.Max(t1, t2);

        t1 = (min.Y - origin.Y) * inverseDirection.Y;
        t2 = (max.Y - origin.Y) * inverseDirection.Y;
        tMin = MathF.Max(tMin, MathF.Min(t1, t2));
        tMax = MathF.Min(tMax, MathF.Max(t1, t2));

        t1 = (min.Z - origin.Z) * inverseDirection.Z;
        t2 = (max.Z - origin.Z) * inverseDirection.Z;
        tMin = MathF.Max(tMin, MathF.Min(t1, t2));
        tMax = MathF.Min(tMax, MathF.Max(t1, t2));

        return tMax >= MathF.Max(tMin, 0f) && tMin <= maxDistance;
    }

    private static float SquaredDistanceToBox(Vector3 point, Vector3 min, Vector3 max)
    {
        var clamped = Vector3.Clamp(point, min, max);
        return (clamped - point).LengthSquared();
    }

    /// <summary>Moller-Trumbore, matching the tolerances the MeshLib engine's raycast used.</summary>
    private static bool RayTriangle(Vector3 origin, Vector3 direction, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
    {
        const float eps = 1e-7f;
        const float tol = 1e-4f;
        t = 0f;

        var edge1 = v1 - v0;
        var edge2 = v2 - v0;
        var h = Vector3.Cross(direction, edge2);
        float a = Vector3.Dot(edge1, h);
        if (a > -eps && a < eps) return false;

        float f = 1f / a;
        var s = origin - v0;
        float u = f * Vector3.Dot(s, h);
        if (u < -tol || u > 1f + tol) return false;

        var q = Vector3.Cross(s, edge1);
        float v = f * Vector3.Dot(direction, q);
        if (v < -tol || u + v > 1f + tol) return false;

        t = f * Vector3.Dot(edge2, q);
        return t > eps;
    }

    private static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        // Ericson, Real-Time Collision Detection: walk the Voronoi regions of the triangle's
        // vertices and edges before falling through to the face interior.
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;

        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;

        var bp = p - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float denominator = d1 - d3;
            return a + (MathF.Abs(denominator) > 1e-20f ? d1 / denominator : 0f) * ab;
        }

        var cp = p - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float denominator = d2 - d6;
            return a + (MathF.Abs(denominator) > 1e-20f ? d2 / denominator : 0f) * ac;
        }

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
        {
            float denominator = d4 - d3 + d5 - d6;
            float w = MathF.Abs(denominator) > 1e-20f ? (d4 - d3) / denominator : 0f;
            return b + w * (c - b);
        }

        float total = va + vb + vc;
        if (MathF.Abs(total) < 1e-20f) return a;

        return a + ab * (vb / total) + ac * (vc / total);
    }
}
