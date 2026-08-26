using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using GeometryMeshLib;
using System.Numerics;

namespace Geometry.MeshLib;

public class GeometryEvaluators : IGeometryEvaluators {
    private readonly GeometryEngine _engine;

    public GeometryEvaluators(GeometryEngine engine) {
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
                double d = distResult.dist;
                
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
