using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using GeometryMeshLib;
using System.Numerics;

namespace Geometry.MeshLib;

public class Evaluators : IGeometryEvaluators {
    private readonly GeometryEngine _engine;

    public Evaluators(GeometryEngine engine) {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public Result<IReadOnlyList<Vector3>> ComputeVertexNormals(IMesh mesh) {
        using var mlMesh = mesh.ToMRMesh();
        using var validVerts = mlMesh.topology.getValidVerts();
        var pts = mlMesh.points.vec;
        ulong ptsCount = pts.size();

        using var normalsVec = MR.computePerVertNormals(mlMesh);

        var normals = new List<Vector3>((int)validVerts.count());
        for (ulong i = 0; i < ptsCount; i++) {
            var vid = new MR.VertId((int)i);
            if (!validVerts.test(vid)) continue;

            var n = normalsVec[vid];
            var vector = new Vector3(n.x, n.y, n.z);
            var length = vector.Length();
            normals.Add(length > 1e-12f ? vector / length : Vector3.Zero);
        }

        return Result.Success<IReadOnlyList<Vector3>>(normals);
    }

    public Result<TopologyValidation> ValidateTopology(IMesh mesh) {
        using var mlMesh = mesh.ToMRMesh();

        int selfInts = 0;
        int nonManifoldEdges = 0;
        bool isManifold = true;

        try {
            selfInts = (int)MR.SelfIntersections.getFaces(mlMesh).count();

            var multipleEdges = MR.findMultipleEdges(mlMesh.topology, null);
            nonManifoldEdges = (int)multipleEdges.size();

            isManifold = nonManifoldEdges == 0;
        } catch {
            // Fail-safe default
        }

        using var validVerts = mlMesh.topology.getValidVerts();
        using var validFaces = mlMesh.topology.getValidFaces();

        // Calculate boundary edge count
        using var bdEdges = MR.findAllLeftBdEdges(mlMesh.topology, null, null);
        int boundaryEdges = (int)bdEdges.count();

        // Check degenerate triangles
        bool hasDegenerate = false;
        ulong faceCap = mlMesh.topology.faceCapacity();
        for (ulong i = 0; i < faceCap; i++) {
            var fid = new MR.FaceId((int)i);
            if (validFaces.test(fid)) {
                if (MR.area(mlMesh.topology, mlMesh.points, fid) < 1e-6) {
                    hasDegenerate = true;
                    break;
                }
            }
        }

        // Check orphaned vertices
        bool hasOrphaned = mlMesh.points.vec.size() > validVerts.count();

        return new TopologyValidation {
            HasCorruptTopology = false, // MeshLib topology is built and compacted
            IsWatertight = boundaryEdges == 0,
            IsManifold = isManifold,
            HasOrphanedVertices = hasOrphaned,
            HasDegenerateTriangles = hasDegenerate,
            VertexCount = (int)validVerts.count(),
            TriangleCount = (int)validFaces.count(),
            BoundaryEdgeCount = boundaryEdges,
            NonManifoldEdgeCount = nonManifoldEdges,
            SelfIntersectionCount = selfInts
        };
    }

    public Result<MeshStatistics> GetStatistics(IMesh mesh) {
        using var mlMesh = mesh.ToMRMesh();
        using var validVerts = mlMesh.topology.getValidVerts();
        using var validFaces = mlMesh.topology.getValidFaces();

        var bounds = MR.computeBoundingBox(mlMesh.topology, mlMesh.points, null, null);

        double volume = 0.0;
        using var bdEdges = MR.findAllLeftBdEdges(mlMesh.topology, null, null);
        bool isClosed = bdEdges.count() == 0;

        if (isClosed) {
            volume = MR.volume(mlMesh.topology, mlMesh.points, null) / 1000.0;
        }

        double surfaceArea = MR.area(mlMesh.topology, mlMesh.points, (MR.Const_FaceBitSet?)null);

        // Count edges: each valid undirected edge is a unique edge.
        int edgeCount = (int)mlMesh.topology.undirectedEdgeSize();

        return new MeshStatistics {
            VertexCount = (int)validVerts.count(),
            TriangleCount = (int)validFaces.count(),
            EdgeCount = edgeCount,
            BoundaryEdgeCount = (int)bdEdges.count(),
            Volume = volume,
            SurfaceArea = surfaceArea,
            MinX = bounds.min.x,
            MinY = bounds.min.y,
            MinZ = bounds.min.z,
            MaxX = bounds.max.x,
            MaxY = bounds.max.y,
            MaxZ = bounds.max.z
        };
    }

    public Result<RenderData> GetRenderData(IMesh mesh) {
        using var mlMesh = mesh.ToMRMesh();
        using var validVerts = mlMesh.topology.getValidVerts();
        using var validFaces = mlMesh.topology.getValidFaces();
        
        int activeVerts = (int)validVerts.count();
        int activeFaces = (int)validFaces.count();

        var vertices = new double[activeVerts * 3];
        var triangles = new int[activeFaces * 3];
        var normals = new double[activeVerts * 3];

        var pts = mlMesh.points.vec;
        ulong ptsCount = pts.size();

        var vertexIdToIndex = new Dictionary<int, int>();
        int indexCounter = 0;

        int vIndex = 0;
        for (ulong i = 0; i < ptsCount; i++) {
            var vid = new MR.VertId((int)i);
            if (validVerts.test(vid)) {
                var pt = pts[i];
                vertices[vIndex++] = pt.x;
                vertices[vIndex++] = pt.y;
                vertices[vIndex++] = pt.z;
                vertexIdToIndex[(int)i] = indexCounter++;
            }
        }

        // Calculate vertex normals
        using var normalsVec = MR.computePerVertNormals(mlMesh);
        int nIndex = 0;
        for (ulong i = 0; i < ptsCount; i++) {
            var vid = new MR.VertId((int)i);
            if (validVerts.test(vid)) {
                var norm = normalsVec[vid];
                normals[nIndex++] = norm.x;
                normals[nIndex++] = norm.y;
                normals[nIndex++] = norm.z;
            }
        }

        ulong faceCap = mlMesh.topology.faceCapacity();
        int tIndex = 0;
        for (ulong i = 0; i < faceCap; i++) {
            var fid = new MR.FaceId((int)i);
            if (validFaces.test(fid)) {
                var triVerts = mlMesh.topology.getTriVerts(fid);
                triangles[tIndex++] = vertexIdToIndex[triVerts.elems._0.get()];
                triangles[tIndex++] = vertexIdToIndex[triVerts.elems._1.get()];
                triangles[tIndex++] = vertexIdToIndex[triVerts.elems._2.get()];
            }
        }

        return new RenderData {
            Vertices = vertices,
            Triangles = triangles,
            Normals = normals
        };
    }

    public Result<double[]> CalculateDeviationColors(IMesh current, IMesh original, double maxDeviation = 0.4) {
        using var currentMesh = current.ToMRMesh();
        using var originalMesh = original.ToMRMesh();

        using var validVerts = currentMesh.topology.getValidVerts();
        int activeVerts = (int)validVerts.count();
        var colors = new double[activeVerts * 3];

        var pts = currentMesh.points.vec;
        ulong ptsCount = pts.size();

        double scale = Math.Max(maxDeviation, 0.001);
        int colorIndex = 0;

        using var originalPart = new MR.MeshPart(originalMesh);
        var gradient = Fabolus.Core.Features.Overhangs.ColourGradient.SmoothingDeviation;

        for (ulong i = 0; i < ptsCount; i++) {
            var vid = new MR.VertId((int)i);
            if (validVerts.test(vid)) {
                var pt = pts[i];
                var ptRef = pt;
                using var distResultOpt = MR.findSignedDistance(in ptRef, originalPart, null, null);
                using var distResult = distResultOpt.value();
                double d = distResult?.dist ?? 0;
                
                // Map distance from [-scale, scale] to [0, 1]
                double t = Math.Clamp((d + scale) / (2.0 * scale), 0.0, 1.0);
                
                var color = gradient.Sample((float)t);
                colors[colorIndex++] = color.R;
                colors[colorIndex++] = color.G;
                colors[colorIndex++] = color.B;
            }
        }

        return colors;
    }

    /// <summary>
    /// Probes inward from each face along its own normal and reports where the probe came out the
    /// far side. Inside the solid the signed distance is negative, outside it is positive, so the
    /// crossing is the point where that flips - the search brackets it on a coarse sweep and then
    /// bisects, rather than stepping finely all the way, which is what keeps this to a couple of
    /// dozen probes a face instead of hundreds.
    /// </summary>
    public Result<WallThickness> MeasureWallThickness(IMesh mesh, WallThicknessOptions options) {
        if (mesh is null) return MeshErrors.NullSource;
        options ??= WallThicknessOptions.Default;

        if (options.MaxThicknessMm <= 0f || options.CoarseSteps < 1 || options.ToleranceMm <= 0f)
            return new Error("Geometry.InvalidThicknessOptions",
                "Search distance, step count and tolerance must all be positive.");

        try {
            using var mlMesh = mesh.ToMRMesh();
            using var part = new MR.MeshPart(mlMesh);

            var vertices = mesh.Vertices;
            var triangles = mesh.Triangles;
            int faceCount = triangles.Length / 3;

            var perFace = new float[faceCount];
            var partner = new int[faceCount];
            var faceArea = new float[faceCount];
            Array.Fill(partner, -1);
            float coarse = options.MaxThicknessMm / options.CoarseSteps;
            var measured = new List<float>(faceCount);

            for (int f = 0; f < faceCount; f++) {
                var a = vertices[triangles[f * 3]];
                var b = vertices[triangles[(f * 3) + 1]];
                var c = vertices[triangles[(f * 3) + 2]];

                var cross = Vector3.Cross(b - a, c - a);
                faceArea[f] = cross.Length() * 0.5f;
                if (cross.LengthSquared() < 1e-12f) {
                    perFace[f] = float.PositiveInfinity;   // degenerate face, no normal to probe along
                    continue;
                }

                var origin = (a + b + c) / 3f;
                var inward = -Vector3.Normalize(cross);

                // Bracket: walk out until the probe reads outside.
                float previous = 0f;
                float exit = float.PositiveInfinity;
                for (int step = 1; step <= options.CoarseSteps; step++) {
                    float t = step * coarse;
                    if (SignedDistance(part, origin + (inward * t)) > 0f) { exit = t; break; }
                    previous = t;
                }

                if (float.IsPositiveInfinity(exit)) {
                    perFace[f] = float.PositiveInfinity;
                    continue;
                }

                // Bisect the bracket down to the requested tolerance.
                float low = previous, high = exit;
                while (high - low > options.ToleranceMm) {
                    float mid = (low + high) * 0.5f;
                    if (SignedDistance(part, origin + (inward * mid)) > 0f) high = mid;
                    else low = mid;
                }

                perFace[f] = high;
                partner[f] = FaceAt(part, origin + (inward * high));
                measured.Add(high);
            }

            return new WallThickness {
                PerFace = perFace,
                PerVertex = CarryToVertices(perFace, faceArea, triangles, vertices.Length),
                PartnerFace = partner,
                Statistics = Summarise(measured, faceCount),
                Options = options,
            };
        } catch (Exception ex) {
            return new Error("Geometry.EvaluatorFailed", ex.Message);
        }
    }

    /// <summary>
    /// Spreads the per-face measurement onto the vertices, weighted by face area so a fan of slivers
    /// around a vertex cannot outweigh the one broad face beside it. Faces that never exited carry no
    /// weight at all rather than counting as very thick.
    /// </summary>
    private static float[] CarryToVertices(
        float[] perFace, float[] faceArea, int[] triangles, int vertexCount) {
        var weighted = new float[vertexCount];
        var weight = new float[vertexCount];

        for (int f = 0; f < perFace.Length; f++) {
            if (!float.IsFinite(perFace[f])) continue;

            float w = faceArea[f];
            for (int corner = 0; corner < 3; corner++) {
                int v = triangles[(f * 3) + corner];
                weighted[v] += perFace[f] * w;
                weight[v] += w;
            }
        }

        var perVertex = new float[vertexCount];
        for (int v = 0; v < vertexCount; v++)
            perVertex[v] = weight[v] > 0f ? weighted[v] / weight[v] : float.PositiveInfinity;

        return perVertex;
    }

    /// <summary>
    /// Summarises the faces that returned a thickness. <paramref name="measured"/> is sorted here
    /// rather than by the caller, since every figure below wants it in order.
    /// </summary>
    private static WallThicknessStatistics Summarise(List<float> measured, int faceCount) {
        if (measured.Count == 0)
            return WallThicknessStatistics.Empty with { TotalFaces = faceCount };

        measured.Sort();

        double total = 0d;
        foreach (float value in measured) total += value;
        float mean = (float)(total / measured.Count);

        double variance = 0d;
        foreach (float value in measured) {
            double d = value - mean;
            variance += d * d;
        }

        return new WallThicknessStatistics {
            Median = measured[measured.Count / 2],
            Mean = mean,
            Minimum = measured[0],
            Maximum = measured[^1],
            StandardDeviation = (float)Math.Sqrt(variance / measured.Count),
            FifthPercentile = measured[(int)(measured.Count * 0.05)],
            NinetyFifthPercentile = measured[Math.Min(measured.Count - 1, (int)(measured.Count * 0.95))],
            MeasuredFaces = measured.Count,
            TotalFaces = faceCount,
        };
    }

    /// <summary>The face nearest a point - the one the probe came out through. -1 if none.</summary>
    private static int FaceAt(MR.MeshPart part, Vector3 point) {
        var query = new MR.Vector3f(point.X, point.Y, point.Z);
        using var found = MR.findProjection(in query, part, float.MaxValue, null, 0f, null);
        var face = found.proj.face;
        return face.valid() ? face.get() : -1;
    }

    private static float SignedDistance(MR.MeshPart part, Vector3 point) {
        var query = new MR.Vector3f(point.X, point.Y, point.Z);
        using var found = MR.findSignedDistance(in query, part, null, null);
        using var hit = found?.value();
        return hit is null ? float.MaxValue : (float)hit.dist;
    }

    public Result<bool> HasMultipleComponents(IMesh mesh) {
        try {
            using var mlMesh = mesh.ToMRMesh();
            using var part = new MR.MeshPart(mlMesh);
            using var comps = MR.MeshComponents.getAllComponents(part, null, null);

            return comps.size() > 1;
        } catch (Exception ex) {
            return new Error("Geometry.EvaluatorFailed", ex.Message);
        }
    }

    public Result<IEnumerable<IMesh>> SeparateComponents(IMesh mesh) {
        const double minVolume = 0.1;

        try {
            using var mlMesh = mesh.ToMRMesh();
            using var part = new MR.MeshPart(mlMesh);
            using var comps = MR.MeshComponents.getAllComponents(part, null, null);
            if (comps.size() <= 1)
                return Result.Success<IEnumerable<IMesh>>(new[] { mesh });

            var resultMeshes = new List<IMesh>();
            using var validFaces = mlMesh.topology.getValidFaces();

            for (uint i = 0; i < comps.size(); i++) {
                using var compFaces = comps[(ulong)i];
                using var subMesh = new MR.Mesh(mlMesh); // copy constructor

                using var facesToDelete = validFaces - compFaces;

                subMesh.deleteFaces(facesToDelete, null);
                subMesh.pack();

                var volume = MR.volume(subMesh.topology, subMesh.points, null);
                if (volume < minVolume) continue;

                var newMetadata = new MeshMetadata().WithProperties(m =>
                    m.Set(CoreKeys.Id, Guid.NewGuid())
                     .Set(CoreKeys.Name, $"{mesh.Metadata.Name} Component {i + 1}"));

                resultMeshes.Add(subMesh.ToIMesh(newMetadata));
            }
            return Result.Success<IEnumerable<IMesh>>(resultMeshes);
        } catch (Exception ex) {
            return new Error("Geometry.EvaluatorFailed", ex.Message);
        }
    }

    public Result<RaycastHit> Raycast(IMesh mesh, Vector3 rayOrigin, Vector3 rayDirection)
    {
        if (mesh is null)
            return MeshErrors.NullSource;

        float dirLen = rayDirection.Length();
        if (dirLen < 1e-6f)
            return new Error("Raycast.InvalidDirection", "Ray direction must be a non-zero vector.");

        var dir = rayDirection / dirLen;
        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;

        if (vertices is null || triangles is null || vertices.Length == 0 || triangles.Length == 0)
            return MeshErrors.RaycastMiss;

        float minT = float.MaxValue;
        bool found = false;
        Vector3 bestNormal = -dir;

        const float eps = 1e-7f;
        const float tol = 1e-4f;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            var v0 = vertices[triangles[i]];
            var v1 = vertices[triangles[i + 1]];
            var v2 = vertices[triangles[i + 2]];

            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var h = Vector3.Cross(dir, edge2);
            float a = Vector3.Dot(edge1, h);

            if (a > -eps && a < eps)
                continue;

            float f = 1.0f / a;
            var s = rayOrigin - v0;
            float u = f * Vector3.Dot(s, h);

            if (u < -tol || u > 1.0f + tol)
                continue;

            var q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(dir, q);

            if (v < -tol || u + v > 1.0f + tol)
                continue;

            float t = f * Vector3.Dot(edge2, q);

            if (t > eps && t < minT)
            {
                minT = t;
                found = true;

                var triCross = Vector3.Cross(edge1, edge2);
                if (triCross.LengthSquared() > 1e-8f)
                {
                    bestNormal = Vector3.Normalize(triCross);
                    if (Vector3.Dot(bestNormal, dir) > 0f)
                    {
                        bestNormal = -bestNormal;
                    }
                }
                else
                {
                    bestNormal = -dir;
                }
            }
        }

        if (!found)
            return MeshErrors.RaycastMiss;

        var hitPoint = rayOrigin + minT * dir;
        return Result.Success(new RaycastHit(hitPoint, bestNormal, minT));
    }
}
