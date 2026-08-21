using System.Numerics;
using Fabolus.Core.Common;
using Fabolus.Core.Features.Emboss;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace GeometryMeshLib.Text;

public static class TextMeshBuilder
{
    /// <summary>
    /// Builds an extruded 3D solid mesh from 2D polygon outlines in the tangent frame.
    /// </summary>
    public static Result<IMesh> BuildPrism(
        IGeometryEngine engine,
        IReadOnlyList<Polygon2D> outlines,
        DecalFrame frame,
        float depth,
        float sink,
        float overshoot,
        float maxEdgeLength = 0f)
    {
        if (outlines == null || outlines.Count == 0)
            return new Error("TextMeshBuilder.EmptyOutlines", "No outline contours provided to build text mesh.");

        using var contours = new MR.Std.Vector_StdVectorMRVector2f();

        foreach (var poly in outlines)
        {
            if (poly.OuterBoundary.Count < 3) continue;

            using var outerContour = new MR.Std.Vector_MRVector2f();
            foreach (var pt in poly.OuterBoundary)
            {
                outerContour.pushBack(new MR.Vector2f(pt.X, pt.Y));
            }
            contours.pushBack(outerContour);

            foreach (var hole in poly.Holes)
            {
                if (hole.Count < 3) continue;
                using var holeContour = new MR.Std.Vector_MRVector2f();
                foreach (var pt in hole)
                {
                    holeContour.pushBack(new MR.Vector2f(pt.X, pt.Y));
                }
                contours.pushBack(holeContour);
            }
        }

        if (contours.size() == 0)
            return new Error("TextMeshBuilder.EmptyOutlines", "Valid 2D contours required.");

        var polyMesh = MR.PlanarTriangulation.triangulateContours(contours, null);
        if (polyMesh is null || polyMesh.topology.getValidFaces().count() == 0)
            return new Error("TextMeshBuilder.TriangulationFailed", "Planar triangulation failed for text outlines.");

        float zMin = sink;
        float zMax = sink + depth + overshoot;

        // Subdivide in 2D if maxEdgeLength specified
        List<Vector2> pts2D = [];
        List<(int A, int B, int C)> faces2D = [];

        var pVerts = polyMesh.topology.getValidVerts();
        var pPts = polyMesh.points.vec;
        ulong vertCap = pPts.size();
        var vertIndexMap = new Dictionary<int, int>();

        for (ulong i = 0; i < vertCap; i++)
        {
            var vid = new MR.VertId((int)i);
            if (pVerts.test(vid))
            {
                var pt = pPts[i];
                vertIndexMap[(int)i] = pts2D.Count;
                pts2D.Add(new Vector2(pt.x, pt.y));
            }
        }

        var pFaces = polyMesh.topology.getValidFaces();
        ulong faceCap = polyMesh.topology.faceCapacity();

        for (ulong i = 0; i < faceCap; i++)
        {
            var fid = new MR.FaceId((int)i);
            if (pFaces.test(fid))
            {
                var tri = polyMesh.topology.getTriVerts(fid);
                int a = vertIndexMap[tri.elems._0.get()];
                int b = vertIndexMap[tri.elems._1.get()];
                int c = vertIndexMap[tri.elems._2.get()];

                var vA = pts2D[a];
                var vB = pts2D[b];
                var vC = pts2D[c];

                // Ensure counter-clockwise winding in 2D
                float cross = (vB.X - vA.X) * (vC.Y - vA.Y) - (vB.Y - vA.Y) * (vC.X - vA.X);
                if (cross < 0)
                {
                    int temp = b; b = c; c = temp;
                }

                faces2D.Add((a, b, c));
            }
        }

        polyMesh.Dispose();

        if (maxEdgeLength > 0f)
        {
            Subdivide2D(pts2D, faces2D, maxEdgeLength);
        }

        // Extrude to 3D
        int ptCount = pts2D.Count;
        var vertices = new List<double>(ptCount * 2 * 3);
        var triangles = new List<int>(faces2D.Count * 2 * 3 + ptCount * 6);

        var bottomMap = new int[ptCount];
        var topMap = new int[ptCount];

        for (int i = 0; i < ptCount; i++)
        {
            var p = pts2D[i];

            // Bottom vertex (zMin)
            var pBot = frame.ToWorld(p.X, p.Y, zMin);
            bottomMap[i] = vertices.Count / 3;
            vertices.Add(pBot.X);
            vertices.Add(pBot.Y);
            vertices.Add(pBot.Z);

            // Top vertex (zMax)
            var pTop = frame.ToWorld(p.X, p.Y, zMax);
            topMap[i] = vertices.Count / 3;
            vertices.Add(pTop.X);
            vertices.Add(pTop.Y);
            vertices.Add(pTop.Z);
        }

        var edgeCounts = new Dictionary<(int, int), int>();

        foreach (var (a, b, c) in faces2D)
        {
            // Bottom face (clockwise / normal -N)
            triangles.Add(bottomMap[a]);
            triangles.Add(bottomMap[c]);
            triangles.Add(bottomMap[b]);

            // Top face (counter-clockwise / normal +N)
            triangles.Add(topMap[a]);
            triangles.Add(topMap[b]);
            triangles.Add(topMap[c]);

            AddEdge(edgeCounts, a, b);
            AddEdge(edgeCounts, b, c);
            AddEdge(edgeCounts, c, a);
        }

        // Side walls on boundary edges
        foreach (var (edge, count) in edgeCounts)
        {
            var (a, b) = edge;
            if (!edgeCounts.ContainsKey((b, a)))
            {
                triangles.Add(bottomMap[a]);
                triangles.Add(bottomMap[b]);
                triangles.Add(topMap[b]);

                triangles.Add(bottomMap[a]);
                triangles.Add(topMap[b]);
                triangles.Add(topMap[a]);
            }
        }

        var meshResult = engine.CreateMesh(vertices.ToArray().AsSpan(), triangles.ToArray().AsSpan());
        if (meshResult.IsFailure) return meshResult;

        var metadata = new MeshMetadata().WithProperties(m =>
            m.Set(CoreKeys.Id, Guid.NewGuid())
             .Set(CoreKeys.Name, "Text Prism")
             .Set(CoreKeys.CreatedBy, "TextMeshBuilder.BuildPrism"));

        return Result.Success(meshResult.Value.WithMetadata(metadata));
    }

    private static void AddEdge(Dictionary<(int, int), int> edgeCounts, int a, int b)
    {
        edgeCounts[(a, b)] = edgeCounts.TryGetValue((a, b), out int count) ? count + 1 : 1;
    }

    private static void Subdivide2D(List<Vector2> pts, List<(int A, int B, int C)> faces, float maxEdgeLength)
    {
        float maxEdgeLenSq = maxEdgeLength * maxEdgeLength;
        int maxIterations = 3;

        for (int iter = 0; iter < maxIterations; iter++)
        {
            bool splitAny = false;
            var edgeMidpoints = new Dictionary<(int, int), int>();

            int GetOrAddMidpoint(int i1, int i2)
            {
                int min = Math.Min(i1, i2);
                int max = Math.Max(i1, i2);
                if (!edgeMidpoints.TryGetValue((min, max), out int midIdx))
                {
                    var mid = (pts[min] + pts[max]) * 0.5f;
                    midIdx = pts.Count;
                    pts.Add(mid);
                    edgeMidpoints[(min, max)] = midIdx;
                }
                return midIdx;
            }

            var newFaces = new List<(int A, int B, int C)>(faces.Count * 2);

            foreach (var (a, b, c) in faces)
            {
                float dAbSq = Vector2.DistanceSquared(pts[a], pts[b]);
                float dBcSq = Vector2.DistanceSquared(pts[b], pts[c]);
                float dCaSq = Vector2.DistanceSquared(pts[c], pts[a]);

                bool splitAb = dAbSq > maxEdgeLenSq;
                bool splitBc = dBcSq > maxEdgeLenSq;
                bool splitCa = dCaSq > maxEdgeLenSq;

                if (!splitAb && !splitBc && !splitCa)
                {
                    newFaces.Add((a, b, c));
                    continue;
                }

                splitAny = true;

                if (splitAb && splitBc && splitCa)
                {
                    int mAb = GetOrAddMidpoint(a, b);
                    int mBc = GetOrAddMidpoint(b, c);
                    int mCa = GetOrAddMidpoint(c, a);

                    newFaces.Add((a, mAb, mCa));
                    newFaces.Add((mAb, b, mBc));
                    newFaces.Add((mBc, c, mCa));
                    newFaces.Add((mAb, mBc, mCa));
                }
                else if (splitAb && splitBc)
                {
                    int mAb = GetOrAddMidpoint(a, b);
                    int mBc = GetOrAddMidpoint(b, c);

                    newFaces.Add((a, mAb, c));
                    newFaces.Add((mAb, b, mBc));
                    newFaces.Add((mAb, mBc, c));
                }
                else if (splitBc && splitCa)
                {
                    int mBc = GetOrAddMidpoint(b, c);
                    int mCa = GetOrAddMidpoint(c, a);

                    newFaces.Add((b, mBc, a));
                    newFaces.Add((mBc, c, mCa));
                    newFaces.Add((mBc, mCa, a));
                }
                else if (splitAb && splitCa)
                {
                    int mAb = GetOrAddMidpoint(a, b);
                    int mCa = GetOrAddMidpoint(c, a);

                    newFaces.Add((a, mAb, mCa));
                    newFaces.Add((mAb, b, c));
                    newFaces.Add((mAb, c, mCa));
                }
                else if (splitAb)
                {
                    int mAb = GetOrAddMidpoint(a, b);
                    newFaces.Add((a, mAb, c));
                    newFaces.Add((mAb, b, c));
                }
                else if (splitBc)
                {
                    int mBc = GetOrAddMidpoint(b, c);
                    newFaces.Add((b, mBc, a));
                    newFaces.Add((mBc, c, a));
                }
                else // splitCa
                {
                    int mCa = GetOrAddMidpoint(c, a);
                    newFaces.Add((c, mCa, b));
                    newFaces.Add((mCa, a, b));
                }
            }

            faces.Clear();
            faces.AddRange(newFaces);

            if (!splitAny) break;
        }
    }

    /// <summary>
    /// Projects each vertex of a text prism onto the curved surface of the target mesh along the frame's normal.
    /// </summary>
    public static Result<IMesh> ProjectPrism(
        IGeometryEngine engine,
        IMesh targetMesh,
        DecalFrame frame,
        IMesh prismMesh,
        List<string>? warnings = null)
    {
        if (targetMesh == null || prismMesh == null)
            return Result.Success(prismMesh!);

        using var mlTargetMesh = targetMesh.ToMRMesh();
        using var spatial = new MR.ObjectMesh();
        using var sharedPtr = new MR.Std.SharedPtr_MRMesh(mlTargetMesh);
        spatial.setMesh(sharedPtr);

        using var targetPart = new MR.MeshPart(mlTargetMesh);

        var vertices = prismMesh.Vertices;
        var triangles = prismMesh.Triangles;
        var projectedVerts = new double[vertices.Length * 3];

        bool hadMiss = false;
        bool hadLargeDeviation = false;

        float rayOffset = 150.0f;
        var rayDir = -frame.N;

        for (int i = 0; i < vertices.Length; i++)
        {
            var vWorld = vertices[i];
            var vLocal = frame.ToLocal(vWorld); // (u, v, zLocal)

            var rayOrigin = frame.Origin + vLocal.X * frame.U + vLocal.Y * frame.V + rayOffset * frame.N;
            var mrOrigin = new MR.Vector3f(rayOrigin.X, rayOrigin.Y, rayOrigin.Z);
            var mrDir = new MR.Vector3f(rayDir.X, rayDir.Y, rayDir.Z);

            using var line = new MR.Line3f(mrOrigin, mrDir);
            using var hit = spatial.worldRayIntersection(line, null);

            Vector3 hitPoint;
            Vector3 hitNormal = frame.N;

            if (hit is not null)
            {
                float dist = hit.distanceAlongLine;
                hitPoint = rayOrigin + rayDir * dist;

                // Find normal at hit point
                var ptRef = new MR.Vector3f(hitPoint.X, hitPoint.Y, hitPoint.Z);
                using var distResultOpt = MR.findSignedDistance(in ptRef, targetPart, null, null);
                if (distResultOpt is not null)
                {
                    using var distResult = distResultOpt.value();
                    var fid = distResult.proj.face;
                    if (mlTargetMesh.topology.getValidFaces().test(fid))
                    {
                        var tri = mlTargetMesh.topology.getTriVerts(fid);
                        var p0 = mlTargetMesh.points.vec[(ulong)tri.elems._0.get()];
                        var p1 = mlTargetMesh.points.vec[(ulong)tri.elems._1.get()];
                        var p2 = mlTargetMesh.points.vec[(ulong)tri.elems._2.get()];

                        var e1 = new Vector3(p1.x - p0.x, p1.y - p0.y, p1.z - p0.z);
                        var e2 = new Vector3(p2.x - p0.x, p2.y - p0.y, p2.z - p0.z);
                        var cross = Vector3.Cross(e1, e2);
                        if (cross.LengthSquared() > 1e-8f)
                        {
                            hitNormal = Vector3.Normalize(cross);
                        }
                    }
                }

                float dot = Vector3.Dot(hitNormal, frame.N);
                if (dot < 0.5f) // > 60 degrees
                {
                    hadLargeDeviation = true;
                }
            }
            else
            {
                // Fallback: reverse raycast in +N direction
                var revOrigin = frame.Origin + vLocal.X * frame.U + vLocal.Y * frame.V - rayOffset * frame.N;
                var mrRevOrigin = new MR.Vector3f(revOrigin.X, revOrigin.Y, revOrigin.Z);
                var mrRevDir = new MR.Vector3f(frame.N.X, frame.N.Y, frame.N.Z);

                using var revLine = new MR.Line3f(mrRevOrigin, mrRevDir);
                using var revHit = spatial.worldRayIntersection(revLine, null);

                if (revHit is not null)
                {
                    hitPoint = revOrigin + frame.N * revHit.distanceAlongLine;
                }
                else
                {
                    hadMiss = true;
                    hitPoint = frame.Origin + vLocal.X * frame.U + vLocal.Y * frame.V;
                }
            }

            var finalPos = hitPoint + hitNormal * vLocal.Z;
            projectedVerts[i * 3] = finalPos.X;
            projectedVerts[i * 3 + 1] = finalPos.Y;
            projectedVerts[i * 3 + 2] = finalPos.Z;
        }

        if (hadMiss && warnings != null)
            warnings.Add("Label extends past the surface");

        if (hadLargeDeviation && warnings != null)
            warnings.Add("Surface too curved for this size");

        var createResult = engine.CreateMesh(projectedVerts.AsSpan(), triangles.AsSpan());
        if (createResult.IsFailure) return createResult;

        return Result.Success(createResult.Value.WithMetadata(prismMesh.Metadata));
    }
}
