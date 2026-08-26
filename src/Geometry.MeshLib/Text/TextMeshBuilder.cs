using System.Numerics;
using Fabolus.Core.Common;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace GeometryMeshLib.Text;

public static class TextMeshBuilder
{
    /// <summary>How close to the baseline a point must sit to be placed on it directly rather than projected.</summary>
    private const float BaselineVerticalTolerance = 1e-4f;

    /// <summary>Below this squared length a cross product is treated as degenerate, not a normal.</summary>
    private const float DegenerateCrossLengthSquared = 1e-8f;

    /// <summary>Below this the two baseline samples are effectively coincident, so don't interpolate between them.</summary>
    private const float MinInterpolationSpan = 1e-6f;

    /// <summary>Millimetres to step along the surface when marching out the baseline. Smaller follows curvature more closely at the cost of more distance queries.</summary>
    private const float BaselineStepSizeMm = 0.5f;

    /// <summary>Millimetres of baseline marched past each end of the text, so glyphs at the extremes still sample a frame either side of themselves.</summary>
    private const float BaselineMarginMm = 2.0f;

    /// <summary>Millimetres to back the projection ray off along the frame normal before firing it at the surface. Must clear the tallest mesh this is used on.</summary>
    private const float ProjectionRayOffsetMm = 150.0f;

    /// <summary>Dot product below which the hit surface has turned more than 60 degrees away from the decal frame - too curved for the text to sit flat.</summary>
    private const float MaxSurfaceDeviationDot = 0.5f;

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
        float maxEdgeLength = 0f,
        IMesh? targetMesh = null)
    {
        if (outlines is null || outlines.Count == 0)
            return DecalErrors.EmptyOutlines;

        // 1. Resample polygon boundaries if maxEdgeLength > 0
        var resampledPolys = maxEdgeLength > 0f ? ResamplePolygons(outlines, maxEdgeLength) : outlines;

        using var contours = new MR.Std.Vector_StdVectorMRVector2f();

        foreach (var poly in resampledPolys)
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
            return DecalErrors.EmptyOutlines;

        // using: the extraction below indexes vertIndexMap by face vertex and will throw on a
        // face referencing a vertex outside getValidVerts(), which would otherwise leak the
        // native mesh past the bare Dispose() at the end of the block.
        using var polyMesh = MR.PlanarTriangulation.triangulateContours(contours, null);
        if (polyMesh is null || polyMesh.topology.getValidFaces().count() == 0)
            return DecalErrors.TriangulationFailed;

        float zMin = sink;
        float zMax = depth + overshoot;

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

        // Extrude to 3D
        int ptCount = pts2D.Count;
        var vertices = new List<double>(ptCount * 2 * 3);
        var triangles = new List<int>(faces2D.Count * 2 * 3 + ptCount * 6);

        var bottomMap = new int[ptCount];
        var topMap = new int[ptCount];

        MR.Mesh? mlTargetMesh = null;
        MR.MeshPart? targetPart = null;
        List<BaselineFrame>? baseline = null;

        if (targetMesh is not null)
        {
            try
            {
                mlTargetMesh = targetMesh.ToMRMesh();
                targetPart = new MR.MeshPart(mlTargetMesh);

                // Seeded from the first point, not from 0: seeding at 0 forces the range to
                // straddle the origin, which silently widens the sampled baseline for any
                // outline set that does not already centre on it.
                float minX = ptCount > 0 ? pts2D[0].X : 0f;
                float maxX = minX;
                for (int i = 1; i < ptCount; i++)
                {
                    if (pts2D[i].X < minX) minX = pts2D[i].X;
                    if (pts2D[i].X > maxX) maxX = pts2D[i].X;
                }

                baseline = BuildSurfaceBaseline(mlTargetMesh, targetPart, frame, minX, maxX, stepSize: BaselineStepSizeMm);
            }
            catch
            {
                mlTargetMesh?.Dispose();
                targetPart?.Dispose();
                mlTargetMesh = null;
                targetPart = null;
                baseline = null;
            }
        }

        bool hasSurface = baseline is not null && !ReferenceEquals(mlTargetMesh, null) && !ReferenceEquals(targetPart, null);

        try
        {
            for (int i = 0; i < ptCount; i++)
            {
                var p = pts2D[i];
                Vector3 pSurface;
                Vector3 localNorm;

                if (hasSurface)
                {
                    var baseFrame = SampleBaseline(baseline!, p.X);

                    if (MathF.Abs(p.Y) < BaselineVerticalTolerance)
                    {
                        pSurface = baseFrame.Position;
                        localNorm = baseFrame.N;
                    }
                    else
                    {
                        Vector3 proposed = baseFrame.Position + p.Y * baseFrame.V;
                        var ptRef = new MR.Vector3f(proposed.X, proposed.Y, proposed.Z);
                        using var distResultOpt = MR.findSignedDistance(in ptRef, targetPart, null, null);
                        if (distResultOpt is not null)
                        {
                            using var distResult = distResultOpt.value();
                            var fid = distResult.proj.face;
                            if (fid.valid() && mlTargetMesh!.topology.getValidFaces().test(fid))
                            {
                                var tri = mlTargetMesh.topology.getTriVerts(fid);
                                var v0 = mlTargetMesh.points.vec[(ulong)tri.elems._0.get()];
                                var v1 = mlTargetMesh.points.vec[(ulong)tri.elems._1.get()];
                                var v2 = mlTargetMesh.points.vec[(ulong)tri.elems._2.get()];

                                var v0Vec = new Vector3(v0.x, v0.y, v0.z);
                                var e1 = new Vector3(v1.x - v0.x, v1.y - v0.y, v1.z - v0.z);
                                var e2 = new Vector3(v2.x - v0.x, v2.y - v0.y, v2.z - v0.z);
                                var cross = Vector3.Cross(e1, e2);
                                Vector3 norm = cross.LengthSquared() > DegenerateCrossLengthSquared ? Vector3.Normalize(cross) : baseFrame.N;
                                if (Vector3.Dot(norm, baseFrame.N) < 0f) norm = -norm;

                                pSurface = proposed - Vector3.Dot(proposed - v0Vec, norm) * norm;
                                localNorm = norm;
                            }
                            else
                            {
                                pSurface = proposed;
                                localNorm = baseFrame.N;
                            }
                        }
                        else
                        {
                            pSurface = proposed;
                            localNorm = baseFrame.N;
                        }
                    }
                }
                else
                {
                    pSurface = frame.Origin + p.X * frame.U + p.Y * frame.V;
                    localNorm = frame.N;
                }

                var pBot = pSurface + zMin * localNorm;
                var pTop = pSurface + zMax * localNorm;

                bottomMap[i] = vertices.Count / 3;
                vertices.Add(pBot.X);
                vertices.Add(pBot.Y);
                vertices.Add(pBot.Z);

                topMap[i] = vertices.Count / 3;
                vertices.Add(pTop.X);
                vertices.Add(pTop.Y);
                vertices.Add(pTop.Z);
            }
        }
        finally
        {
            targetPart?.Dispose();
            mlTargetMesh?.Dispose();
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

    private static IReadOnlyList<Polygon2D> ResamplePolygons(IReadOnlyList<Polygon2D> polygons, float maxEdgeLength)
    {
        var result = new List<Polygon2D>(polygons.Count);
        foreach (var poly in polygons)
        {
            var newOuter = ResampleRing(poly.OuterBoundary, maxEdgeLength);
            var newHoles = new List<IReadOnlyList<Vector2>>(poly.Holes.Count);
            foreach (var hole in poly.Holes)
            {
                newHoles.Add(ResampleRing(hole, maxEdgeLength));
            }
            result.Add(new Polygon2D
            {
                OuterBoundary = newOuter,
                Holes = newHoles
            });
        }
        return result;
    }

    private static IReadOnlyList<Vector2> ResampleRing(IReadOnlyList<Vector2> ring, float maxEdgeLength)
    {
        if (ring.Count < 3) return ring;
        var newPts = new List<Vector2>();
        int count = ring.Count;

        for (int i = 0; i < count; i++)
        {
            var pA = ring[i];
            var pB = ring[(i + 1) % count];
            newPts.Add(pA);

            float dist = Vector2.Distance(pA, pB);
            if (dist > maxEdgeLength)
            {
                int segments = (int)MathF.Ceiling(dist / maxEdgeLength);
                for (int s = 1; s < segments; s++)
                {
                    float t = (float)s / segments;
                    newPts.Add(Vector2.Lerp(pA, pB, t));
                }
            }
        }
        return newPts;
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
        if (targetMesh is null || prismMesh is null)
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

        float rayOffset = ProjectionRayOffsetMm;
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
                        if (cross.LengthSquared() > DegenerateCrossLengthSquared)
                        {
                            hitNormal = Vector3.Normalize(cross);
                        }
                    }
                }

                float dot = Vector3.Dot(hitNormal, frame.N);
                if (dot < MaxSurfaceDeviationDot)
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

        if (hadMiss && warnings is not null)
            warnings.Add("Label extends past the surface");

        if (hadLargeDeviation && warnings is not null)
            warnings.Add("Surface too curved for this size");

        var createResult = engine.CreateMesh(projectedVerts.AsSpan(), triangles.AsSpan());
        if (createResult.IsFailure) return createResult;

        return Result.Success(createResult.Value.WithMetadata(prismMesh.Metadata));
    }

    private readonly struct BaselineFrame
    {
        public readonly float ArcLength;
        public readonly Vector3 Position;
        public readonly Vector3 U;
        public readonly Vector3 V;
        public readonly Vector3 N;

        public BaselineFrame(float arcLength, Vector3 position, Vector3 u, Vector3 v, Vector3 n)
        {
            ArcLength = arcLength;
            Position = position;
            U = u;
            V = v;
            N = n;
        }
    }

    private static List<BaselineFrame> BuildSurfaceBaseline(
        MR.Mesh mlMesh,
        MR.MeshPart targetPart,
        DecalFrame frame,
        float minX,
        float maxX,
        float stepSize = BaselineStepSizeMm)
    {
        var frames = new List<BaselineFrame>();

        var centerFrame = new BaselineFrame(0f, frame.Origin, frame.U, frame.V, frame.N);

        // March positive direction (+U)
        var posFrames = new List<BaselineFrame>();
        float maxDist = MathF.Max(0f, maxX + BaselineMarginMm);
        int posSteps = (int)MathF.Ceiling(maxDist / stepSize);

        Vector3 currPos = frame.Origin;
        Vector3 currU = frame.U;
        Vector3 currV = frame.V;
        Vector3 currN = frame.N;

        for (int i = 1; i <= posSteps; i++)
        {
            float s = i * stepSize;
            Vector3 proposed = currPos + currU * stepSize;
            var ptRef = new MR.Vector3f(proposed.X, proposed.Y, proposed.Z);
            using var distResultOpt = MR.findSignedDistance(in ptRef, targetPart, null, null);
            if (distResultOpt is not null)
            {
                using var distResult = distResultOpt.value();
                var fid = distResult.proj.face;
                if (fid.valid() && mlMesh.topology.getValidFaces().test(fid))
                {
                    var tri = mlMesh.topology.getTriVerts(fid);
                    var v0 = mlMesh.points.vec[(ulong)tri.elems._0.get()];
                    var v1 = mlMesh.points.vec[(ulong)tri.elems._1.get()];
                    var v2 = mlMesh.points.vec[(ulong)tri.elems._2.get()];

                    var v0Vec = new Vector3(v0.x, v0.y, v0.z);
                    var e1 = new Vector3(v1.x - v0.x, v1.y - v0.y, v1.z - v0.z);
                    var e2 = new Vector3(v2.x - v0.x, v2.y - v0.y, v2.z - v0.z);
                    var cross = Vector3.Cross(e1, e2);
                    Vector3 norm = cross.LengthSquared() > DegenerateCrossLengthSquared ? Vector3.Normalize(cross) : currN;
                    if (Vector3.Dot(norm, currN) < 0f) norm = -norm;

                    Vector3 nextPos = proposed - Vector3.Dot(proposed - v0Vec, norm) * norm;
                    
                    var uProj = currU - Vector3.Dot(currU, norm) * norm;
                    Vector3 nextU = uProj.LengthSquared() > DegenerateCrossLengthSquared ? Vector3.Normalize(uProj) : currU;
                    Vector3 nextV = Vector3.Normalize(Vector3.Cross(norm, nextU));
                    if (Vector3.Dot(nextV, currV) < 0f) nextV = -nextV;

                    currPos = nextPos;
                    currU = nextU;
                    currV = nextV;
                    currN = norm;

                    posFrames.Add(new BaselineFrame(s, currPos, currU, currV, currN));
                    continue;
                }
            }

            currPos = proposed;
            posFrames.Add(new BaselineFrame(s, currPos, currU, currV, currN));
        }

        // March negative direction (-U)
        var negFrames = new List<BaselineFrame>();
        float minDist = MathF.Min(0f, minX - BaselineMarginMm);
        int negSteps = (int)MathF.Ceiling(MathF.Abs(minDist) / stepSize);

        currPos = frame.Origin;
        currU = -frame.U;
        currV = frame.V;
        currN = frame.N;

        for (int i = 1; i <= negSteps; i++)
        {
            float s = -i * stepSize;
            Vector3 proposed = currPos + currU * stepSize;
            var ptRef = new MR.Vector3f(proposed.X, proposed.Y, proposed.Z);
            using var distResultOpt = MR.findSignedDistance(in ptRef, targetPart, null, null);
            if (distResultOpt is not null)
            {
                using var distResult = distResultOpt.value();
                var fid = distResult.proj.face;
                if (fid.valid() && mlMesh.topology.getValidFaces().test(fid))
                {
                    var tri = mlMesh.topology.getTriVerts(fid);
                    var v0 = mlMesh.points.vec[(ulong)tri.elems._0.get()];
                    var v1 = mlMesh.points.vec[(ulong)tri.elems._1.get()];
                    var v2 = mlMesh.points.vec[(ulong)tri.elems._2.get()];

                    var v0Vec = new Vector3(v0.x, v0.y, v0.z);
                    var e1 = new Vector3(v1.x - v0.x, v1.y - v0.y, v1.z - v0.z);
                    var e2 = new Vector3(v2.x - v0.x, v2.y - v0.y, v2.z - v0.z);
                    var cross = Vector3.Cross(e1, e2);
                    Vector3 norm = cross.LengthSquared() > DegenerateCrossLengthSquared ? Vector3.Normalize(cross) : currN;
                    if (Vector3.Dot(norm, currN) < 0f) norm = -norm;

                    Vector3 nextPos = proposed - Vector3.Dot(proposed - v0Vec, norm) * norm;
                    
                    var uProj = currU - Vector3.Dot(currU, norm) * norm;
                    Vector3 nextU = uProj.LengthSquared() > DegenerateCrossLengthSquared ? Vector3.Normalize(uProj) : currU;
                    Vector3 nextV = Vector3.Normalize(Vector3.Cross(norm, -nextU));
                    if (Vector3.Dot(nextV, currV) < 0f) nextV = -nextV;

                    currPos = nextPos;
                    currU = nextU;
                    currV = nextV;
                    currN = norm;

                    negFrames.Add(new BaselineFrame(s, currPos, -currU, currV, currN));
                    continue;
                }
            }

            currPos = proposed;
            negFrames.Add(new BaselineFrame(s, currPos, -currU, currV, currN));
        }

        for (int i = negFrames.Count - 1; i >= 0; i--)
        {
            frames.Add(negFrames[i]);
        }
        frames.Add(centerFrame);
        frames.AddRange(posFrames);

        return frames;
    }

    private static BaselineFrame SampleBaseline(IReadOnlyList<BaselineFrame> frames, float u)
    {
        if (frames.Count == 0)
            return new BaselineFrame(u, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        if (frames.Count == 1 || u <= frames[0].ArcLength)
            return frames[0];
        if (u >= frames[^1].ArcLength)
            return frames[^1];

        int low = 0, high = frames.Count - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (frames[mid].ArcLength < u)
                low = mid + 1;
            else
                high = mid - 1;
        }

        int idx0 = Math.Max(0, low - 1);
        int idx1 = Math.Min(frames.Count - 1, idx0 + 1);

        var f0 = frames[idx0];
        var f1 = frames[idx1];
        float span = f1.ArcLength - f0.ArcLength;
        float t = span > MinInterpolationSpan ? Math.Clamp((u - f0.ArcLength) / span, 0f, 1f) : 0f;

        Vector3 pos = Vector3.Lerp(f0.Position, f1.Position, t);
        Vector3 uDir = Vector3.Normalize(Vector3.Lerp(f0.U, f1.U, t));
        Vector3 vDir = Vector3.Normalize(Vector3.Lerp(f0.V, f1.V, t));
        Vector3 nDir = Vector3.Normalize(Vector3.Lerp(f0.N, f1.N, t));

        return new BaselineFrame(u, pos, uDir, vDir, nDir);
    }
}
