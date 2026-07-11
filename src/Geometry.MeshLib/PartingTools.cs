using System.Numerics;
using Fabolus.Core.Common;
using Fabolus.Core.Geometry;

namespace GeometryMeshLib;

/// <summary>
/// Parting-line detection (isoline marching against the pull direction) and split-tool solid
/// generation. The isoline pass walks MeshLib's native mesh directly - vertex positions,
/// per-vertex normals and triangle indices - rather than round-tripping through IMesh's flat
/// arrays, since that data is already sitting in native form once the mesh is loaded.
/// </summary>
internal sealed class PartingTools : IPartingTools
{
    private readonly GeometryEngine _engine;

    public PartingTools(GeometryEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public Result<PartingLine> GeneratePartingLine(IMesh mesh, Vector3 pullDirection, float noiseThreshold = 0.1f)
    {
        if (mesh is null) return GeometryErrors.NullMesh;
        if (mesh.IsEmpty) return GeometryErrors.InvalidMesh;
        if (pullDirection == Vector3.Zero) return MeshErrors.InvalidPullDirection;

        var direction = Vector3.Normalize(pullDirection);

        using var mrMesh = mesh.ToMRMesh();
        using var validVerts = mrMesh.topology.getValidVerts();
        var pts = mrMesh.points.vec;
        ulong ptsCount = pts.size();

        using var normals = MR.computePerVertNormals(mrMesh);

        // Scalar field: how aligned each vertex's normal is with the pull direction.
        // Zero crossings of this field, walked triangle by triangle, are the silhouette.
        var scalars = new double[(int)ptsCount];
        for (ulong i = 0; i < ptsCount; i++)
        {
            var vid = new MR.VertId((int)i);
            if (!validVerts.test(vid)) continue;

            var n = normals[vid];
            scalars[(int)i] = n.x * direction.X + n.y * direction.Y + n.z * direction.Z;
        }

        var graph = new IsolineGraph();

        using var validFaces = mrMesh.topology.getValidFaces();
        ulong faceCap = mrMesh.topology.faceCapacity();

        for (ulong i = 0; i < faceCap; i++)
        {
            var fid = new MR.FaceId((int)i);
            if (!validFaces.test(fid)) continue;

            var tri = mrMesh.topology.getTriVerts(fid);
            int a = tri.elems._0.get();
            int b = tri.elems._1.get();
            int c = tri.elems._2.get();

            double s0 = scalars[a];
            double s1 = scalars[b];
            double s2 = scalars[c];

            bool hasPos = s0 > 0 || s1 > 0 || s2 > 0;
            bool hasNeg = s0 < 0 || s1 < 0 || s2 < 0;
            if (!(hasPos && hasNeg)) continue;

            var pa = pts[(ulong)a];
            var pb = pts[(ulong)b];
            var pc = pts[(ulong)c];

            var crossings = new List<Vector3>(2);
            if (Math.Sign(s0) != Math.Sign(s1)) crossings.Add(Interpolate(pa, pb, s0, s1));
            if (Math.Sign(s1) != Math.Sign(s2)) crossings.Add(Interpolate(pb, pc, s1, s2));
            if (Math.Sign(s2) != Math.Sign(s0)) crossings.Add(Interpolate(pc, pa, s2, s0));

            if (crossings.Count == 2)
                graph.AddSegment(crossings[0], crossings[1]);
        }

        var loops = graph.ExtractLoops();

        double maxDim = 1.0;
        var statsResult = _engine.Evaluators.GetStatistics(mesh);
        if (statsResult.IsSuccess)
        {
            var s = statsResult.Value;
            maxDim = Math.Max(s.MaxX - s.MinX, Math.Max(s.MaxY - s.MinY, s.MaxZ - s.MinZ));
        }

        double threshold = maxDim * noiseThreshold;
        var validLoops = loops.Where(l => LoopLength(l) > threshold).ToList();

        if (validLoops.Count == 0) return MeshErrors.NoPartingLineDetected;

        return Result.Success(new PartingLine(validLoops));
    }

    public Result<IMesh> GenerateSplitTool(IMesh referenceMesh, PartingLine partingLine, Vector3 pullDirection, MeshStatistics toolBounds)
    {
        if (!partingLine.IsValid) return MeshErrors.InvalidPartingLine;
        if (pullDirection == Vector3.Zero) return MeshErrors.InvalidPullDirection;

        var direction = Vector3.Normalize(pullDirection);

        // Largest projected loop is the outer silhouette; everything else is an internal
        // hole that needs its own shut-off tool piece.
        var ordered = partingLine.Loops
            .Select(loop => (loop, area: Math.Abs(SignedProjectedArea(loop, direction))))
            .OrderByDescending(t => t.area)
            .Select(t => t.loop)
            .ToList();

        var outerLoop = ordered[0];
        var holeLoops = ordered.Skip(1).ToList();

        // Work in a local frame where the pull direction is +Z (same technique CutMeshFeature
        // uses for its plane-aligned cutting box), then rotate the finished pieces back.
        var rotation = RotationFromZTo(direction);
        var inverseRotation = Quaternion.Inverse(rotation);

        var corners = BoxCorners(toolBounds).Select(c => Vector3.Transform(c, inverseRotation)).ToList();

        double span = Math.Max(toolBounds.MaxX - toolBounds.MinX,
            Math.Max(toolBounds.MaxY - toolBounds.MinY, toolBounds.MaxZ - toolBounds.MinZ));
        float margin = (float)span + 10.0f;

        float minLocalX = corners.Min(c => c.X) - margin;
        float maxLocalX = corners.Max(c => c.X) + margin;
        float minLocalY = corners.Min(c => c.Y) - margin;
        float maxLocalY = corners.Max(c => c.Y) + margin;
        float maxLocalZ = corners.Max(c => c.Z) + margin;

        var outerLocal = outerLoop.Select(p => Vector3.Transform(p, inverseRotation)).ToList();
        float planeZ = outerLocal.Average(p => p.Z);

        // Main dividing solid: the tool footprint minus the body's own outer silhouette
        // (no cut is needed directly over the body - the mould material there, if any,
        // belongs to whichever internal-hole tool below covers it), extruded from the
        // parting height up to well past the tool bounds.
        var footprint = new Polygon2D
        {
            OuterBoundary = new[]
            {
                new Vector2(minLocalX, minLocalY),
                new Vector2(maxLocalX, minLocalY),
                new Vector2(maxLocalX, maxLocalY),
                new Vector2(minLocalX, maxLocalY),
            },
            Holes = new[] { (IReadOnlyList<Vector2>)outerLocal.Select(p => new Vector2(p.X, p.Y)).ToList() }
        };

        var skirtResult = _engine.Generators.ExtrudePolygon(footprint, planeZ, maxLocalZ);
        if (skirtResult.IsFailure) return skirtResult.Error;

        var pieces = new List<IMesh> { skirtResult.Value };

        // Each internal hole gets its own shut-off piece, sized to just that hole's
        // footprint and positioned at that loop's own height - spatially disjoint from the
        // skirt above (the skirt explicitly excludes the outer loop's interior) so the two
        // never touch, letting the whole tool still resolve in a single boolean pass.
        foreach (var hole in holeLoops)
        {
            var holeLocal = hole.Select(p => Vector3.Transform(p, inverseRotation)).ToList();
            float holePlaneZ = holeLocal.Average(p => p.Z);

            var holePolygon = new Polygon2D
            {
                OuterBoundary = holeLocal.Select(p => new Vector2(p.X, p.Y)).ToList()
            };

            var plugResult = _engine.Generators.ExtrudePolygon(holePolygon, holePlaneZ, maxLocalZ);
            if (plugResult.IsFailure) return plugResult.Error;

            pieces.Add(plugResult.Value);
        }

        var worldPieces = new List<IMesh>();
        foreach (var piece in pieces)
        {
            var rotated = _engine.Transforms.Rotate(piece, rotation);
            if (rotated.IsFailure) return rotated.Error;
            worldPieces.Add(rotated.Value);
        }

        return _engine.CombineMeshes(worldPieces);
    }

    // --- Helpers ---

    private static Vector3 Interpolate(MR.Vector3f a, MR.Vector3f b, double sa, double sb)
    {
        float t = (float)(Math.Abs(sa) / (Math.Abs(sa) + Math.Abs(sb)));
        return new Vector3(a.x + t * (b.x - a.x), a.y + t * (b.y - a.y), a.z + t * (b.z - a.z));
    }

    private static double LoopLength(IReadOnlyList<Vector3> loop)
    {
        double len = 0;
        for (int i = 0; i < loop.Count; i++)
            len += Vector3.Distance(loop[i], loop[(i + 1) % loop.Count]);
        return len;
    }

    /// <summary>Signed area of a loop's projection onto the plane perpendicular to <paramref name="direction"/>.</summary>
    private static double SignedProjectedArea(IReadOnlyList<Vector3> loop, Vector3 direction)
    {
        var rotation = RotationFromZTo(direction);
        var inverse = Quaternion.Inverse(rotation);
        var local = loop.Select(p => Vector3.Transform(p, inverse)).ToList();

        double area = 0;
        for (int i = 0; i < local.Count; i++)
        {
            var p0 = local[i];
            var p1 = local[(i + 1) % local.Count];
            area += (p0.X * p1.Y) - (p1.X * p0.Y);
        }
        return area / 2.0;
    }

    /// <summary>Rotation that maps world +Z onto <paramref name="target"/> (assumed normalized).</summary>
    private static Quaternion RotationFromZTo(Vector3 target)
    {
        var zAxis = Vector3.UnitZ;
        var axis = Vector3.Cross(zAxis, target);
        float dot = Vector3.Dot(zAxis, target);

        if (dot < -0.9999f) return Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI);
        if (dot > 0.9999f) return Quaternion.Identity;

        return Quaternion.Normalize(new Quaternion(axis, 1 + dot));
    }

    private static IEnumerable<Vector3> BoxCorners(MeshStatistics bounds)
    {
        var min = new Vector3((float)bounds.MinX, (float)bounds.MinY, (float)bounds.MinZ);
        var max = new Vector3((float)bounds.MaxX, (float)bounds.MaxY, (float)bounds.MaxZ);

        yield return new Vector3(min.X, min.Y, min.Z);
        yield return new Vector3(max.X, min.Y, min.Z);
        yield return new Vector3(max.X, max.Y, min.Z);
        yield return new Vector3(min.X, max.Y, min.Z);
        yield return new Vector3(min.X, min.Y, max.Z);
        yield return new Vector3(max.X, min.Y, max.Z);
        yield return new Vector3(max.X, max.Y, max.Z);
        yield return new Vector3(min.X, max.Y, max.Z);
    }

    /// <summary>
    /// Stitches isoline segments (pairs of 3D points, one per triangle zero-crossing) into
    /// closed loops via a simple adjacency-graph walk. Direct C# port of the marching-triangles
    /// isoline extraction used elsewhere for parting-line generation - the geometry is engine-
    /// agnostic, only the scalar-field sampling above is MeshLib-specific.
    /// </summary>
    private sealed class IsolineGraph
    {
        private const float ToleranceSq = 0.001f * 0.001f;
        private readonly List<Vector3> _nodes = new();
        private readonly Dictionary<int, List<int>> _adjacency = new();

        public void AddSegment(Vector3 p0, Vector3 p1)
        {
            if (Vector3.DistanceSquared(p0, p1) < ToleranceSq) return;

            int id0 = GetOrAddNode(p0);
            int id1 = GetOrAddNode(p1);
            AddEdge(id0, id1);
            AddEdge(id1, id0);
        }

        private int GetOrAddNode(Vector3 p)
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (Vector3.DistanceSquared(_nodes[i], p) < ToleranceSq) return i;
            }

            _nodes.Add(p);
            _adjacency[_nodes.Count - 1] = new List<int>();
            return _nodes.Count - 1;
        }

        private void AddEdge(int from, int to)
        {
            if (!_adjacency[from].Contains(to)) _adjacency[from].Add(to);
        }

        public List<List<Vector3>> ExtractLoops()
        {
            var loops = new List<List<Vector3>>();
            var visited = new HashSet<int>();

            foreach (var startNode in _adjacency.Keys)
            {
                if (visited.Contains(startNode)) continue;

                var loop = new List<Vector3>();
                int curr = startNode;
                int prev = -1;
                bool closed = false;

                while (true)
                {
                    visited.Add(curr);
                    loop.Add(_nodes[curr]);

                    int next = -1;
                    foreach (var n in _adjacency[curr])
                    {
                        if (n == prev) continue;
                        if (n == startNode && loop.Count > 2) { closed = true; break; }
                        if (!visited.Contains(n)) { next = n; break; }
                    }

                    if (closed || next == -1) break;

                    prev = curr;
                    curr = next;
                }

                if (closed) loops.Add(loop);
            }

            return loops;
        }
    }
}
