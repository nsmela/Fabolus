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

            var crossings = new List<Vector3>(3);
            if (Math.Sign(s0) != Math.Sign(s1)) crossings.Add(Interpolate(pa, pb, s0, s1));
            if (Math.Sign(s1) != Math.Sign(s2)) crossings.Add(Interpolate(pb, pc, s1, s2));
            if (Math.Sign(s2) != Math.Sign(s0)) crossings.Add(Interpolate(pc, pa, s2, s0));

            var uniqueCrossings = new List<Vector3>(3);
            foreach (var cr in crossings)
            {
                if (!uniqueCrossings.Any(u => Vector3.DistanceSquared(u, cr) < 1e-6f))
                    uniqueCrossings.Add(cr);
            }

            if (uniqueCrossings.Count == 2)
                graph.AddSegment(uniqueCrossings[0], uniqueCrossings[1]);
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
        var skirtResult = CreateContouredPrism(outerLocal, true, maxLocalZ, minLocalX, maxLocalX, minLocalY, maxLocalY);
        if (skirtResult.IsFailure) return skirtResult.Error;

        var pieces = new List<IMesh> { skirtResult.Value };

        // Each internal hole gets its own shut-off piece, sized to just that hole's
        // footprint and positioned at that loop's own height - spatially disjoint from the
        // skirt above (the skirt explicitly excludes the outer loop's interior) so the two
        // never touch, letting the whole tool still resolve in a single boolean pass.
        foreach (var hole in holeLoops)
        {
            var holeLocal = hole.Select(p => Vector3.Transform(p, inverseRotation)).ToList();

            var plugResult = CreateContouredPrism(holeLocal, false, maxLocalZ, minLocalX, maxLocalX, minLocalY, maxLocalY);
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

    private Result<IMesh> CreateContouredPrism(IReadOnlyList<Vector3> localLoop, bool isOuter, float maxLocalZ, float minLocalX, float maxLocalX, float minLocalY, float maxLocalY)
    {
        using var contours = new MR.Std.Vector_StdVectorMRVector2f();

        float planeZ = localLoop.Average(p => p.Z);

        if (isOuter)
        {
            using var outerContour = new MR.Std.Vector_MRVector2f();
            outerContour.pushBack(new MR.Vector2f(minLocalX, minLocalY));
            outerContour.pushBack(new MR.Vector2f(maxLocalX, minLocalY));
            outerContour.pushBack(new MR.Vector2f(maxLocalX, maxLocalY));
            outerContour.pushBack(new MR.Vector2f(minLocalX, maxLocalY));
            outerContour.pushBack(new MR.Vector2f(minLocalX, minLocalY)); // close
            contours.pushBack(outerContour);
        }

        double area = 0;
        for (int i = 0; i < localLoop.Count; i++)
        {
            var p0 = localLoop[i];
            var p1 = localLoop[(i + 1) % localLoop.Count];
            area += (p0.X * p1.Y) - (p1.X * p0.Y);
        }

        bool isCcw = area > 0;
        bool needsReverse = isOuter ? isCcw : !isCcw; // Outer skirt hole must be CW. Inner plug boundary must be CCW.

        var orderedLoop = needsReverse ? localLoop.Reverse().ToList() : localLoop.ToList();

        using var innerContour = new MR.Std.Vector_MRVector2f();
        foreach (var p in orderedLoop)
        {
            innerContour.pushBack(new MR.Vector2f(p.X, p.Y));
        }
        innerContour.pushBack(new MR.Vector2f(orderedLoop[0].X, orderedLoop[0].Y)); // close
        contours.pushBack(innerContour);

        var polyMesh = MR.PlanarTriangulation.triangulateContours(contours, null);
        if (polyMesh is null || polyMesh.topology.getValidFaces().count() == 0)
            return new Error("Geometry.TriangulationFailed", "Failed to triangulate parting tool footprint.");

        ulong ptsCount = polyMesh.points.vec.size();
        var pPts = polyMesh.points.vec;
        var pVerts = polyMesh.topology.getValidVerts();

        var bottomZ = new float[ptsCount];
        for (ulong i = 0; i < ptsCount; i++)
        {
            var vid = new MR.VertId((int)i);
            if (!pVerts.test(vid)) continue;
            var v = pPts[i];

            float bestDistSq = float.MaxValue;
            float bestZ = planeZ;
            foreach (var p in localLoop)
            {
                float distSq = (p.X - v.x) * (p.X - v.x) + (p.Y - v.y) * (p.Y - v.y);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestZ = p.Z;
                }
            }

            // Always use the Z coordinate of the nearest parting line point.
            // This ensures the parting flange spreads strictly outwards (horizontally in local space),
            // perpendicular to the pull direction, without slanting up/down to a flat plane.
            bottomZ[i] = bestZ;
        }

        var vertices = new List<double>();
        var triangles = new List<int>();

        var bottomMap = new int[ptsCount];
        var topMap = new int[ptsCount];

        for (ulong i = 0; i < ptsCount; i++)
        {
            var vid = new MR.VertId((int)i);
            if (!pVerts.test(vid)) continue;
            var v = pPts[i];

            bottomMap[i] = vertices.Count / 3;
            vertices.Add(v.x); vertices.Add(v.y); vertices.Add(bottomZ[i]);

            topMap[i] = vertices.Count / 3;
            vertices.Add(v.x); vertices.Add(v.y); vertices.Add(maxLocalZ);
        }

        var pFaces = polyMesh.topology.getValidFaces();
        ulong faceCap = polyMesh.topology.faceCapacity();

        var directedEdges = new HashSet<(int, int)>();

        void AddDirectedEdge(int a, int b)
        {
            if (directedEdges.Contains((b, a)))
                directedEdges.Remove((b, a)); // Internal edge, cancels out
            else
                directedEdges.Add((a, b)); // New boundary edge candidate
        }

        for (ulong i = 0; i < faceCap; i++)
        {
            var fid = new MR.FaceId((int)i);
            if (!pFaces.test(fid)) continue;
            var tri = polyMesh.topology.getTriVerts(fid);
            int a = tri.elems._0.get();
            int b = tri.elems._1.get();
            int c = tri.elems._2.get();

            // Bottom face (inverted normal so it faces down)
            triangles.Add(bottomMap[a]); triangles.Add(bottomMap[c]); triangles.Add(bottomMap[b]);
            // Top face (normal faces up)
            triangles.Add(topMap[a]); triangles.Add(topMap[b]); triangles.Add(topMap[c]);

            AddDirectedEdge(a, b);
            AddDirectedEdge(b, c);
            AddDirectedEdge(c, a);
        }

        foreach (var edge in directedEdges)
        {
            int a = edge.Item1;
            int b = edge.Item2;

            int bA = bottomMap[a], bB = bottomMap[b];
            int tA = topMap[a], tB = topMap[b];

            // Triangles for side wall.
            // edge a -> b has the interior of the polygon to its left.
            // To make the normal point OUT of the solid (into the hole),
            // the vertices must be ordered CCW when viewed from outside the solid.
            triangles.Add(bA); triangles.Add(bB); triangles.Add(tB);
            triangles.Add(tB); triangles.Add(tA); triangles.Add(bA);
        }
        
        return _engine.CreateMesh(vertices.ToArray().AsSpan(), triangles.ToArray().AsSpan());
    }

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
