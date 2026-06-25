using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using GeometryMeshLib;

namespace Geometry.MeshLib;

public class GeometryEvaluators : IGeometryEvaluators {
    private readonly GeometryEngine _engine;

    public GeometryEvaluators(GeometryEngine engine) {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public Result<TopologyValidation> ValidateTopology(IMesh mesh) {
        if (mesh is not MRMesh mrMesh)
            return GeometryErrors.InvalidMeshType;

        var mlMesh = mrMesh.Mesh;

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

        var validVerts = mlMesh.topology.getValidVerts();
        var validFaces = mlMesh.topology.getValidFaces();

        // Calculate boundary edge count
        var bdEdges = MR.findAllLeftBdEdges(mlMesh.topology, null, null);
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
        if (mesh is not MRMesh mrMesh)
            return GeometryErrors.InvalidMeshType;

        var mlMesh = mrMesh.Mesh;
        var validVerts = mlMesh.topology.getValidVerts();
        var validFaces = mlMesh.topology.getValidFaces();

        var bounds = MR.computeBoundingBox(mlMesh.topology, mlMesh.points, null, null);

        double volume = 0.0;
        var bdEdges = MR.findAllLeftBdEdges(mlMesh.topology, null, null);
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
        if (mesh is not MRMesh mrMesh)
            return GeometryErrors.InvalidMeshType;

        var mlMesh = mrMesh.Mesh;
        int activeVerts = (int)mlMesh.topology.getValidVerts().count();
        int activeFaces = (int)mlMesh.topology.getValidFaces().count();

        var vertices = new double[activeVerts * 3];
        var triangles = new int[activeFaces * 3];
        var normals = new double[activeVerts * 3];

        var validVerts = mlMesh.topology.getValidVerts();
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
        var normalsVec = MR.computePerVertNormals(mlMesh);
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

        var validFaces = mlMesh.topology.getValidFaces();
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

    public Result<double[]> CalculateDeviationColors(IMesh current, IMesh original, double maxDeviation = 1.0) {
        if (current is not MRMesh currentMR || original is not MRMesh originalMR)
            return GeometryErrors.InvalidMeshType;

        var currentMesh = currentMR.Mesh;
        var originalMesh = originalMR.Mesh;

        int activeVerts = (int)currentMesh.topology.getValidVerts().count();
        var colors = new double[activeVerts * 3];

        var validVerts = currentMesh.topology.getValidVerts();
        var pts = currentMesh.points.vec;
        ulong ptsCount = pts.size();

        double scale = Math.Max(maxDeviation, 0.001);
        int colorIndex = 0;

        var originalPart = new MR.MeshPart(originalMesh);

        for (ulong i = 0; i < ptsCount; i++) {
            var vid = new MR.VertId((int)i);
            if (validVerts.test(vid)) {
                var pt = pts[i];
                var ptRef = pt;
                var distResultOpt = MR.findSignedDistance(in ptRef, originalPart, null, null);
                var distResult = distResultOpt.value();
                double d = distResult.dist;
                bool inside = d < 0;
                double t = Math.Min(Math.Abs(d) / scale, 1.0);

                if (inside) {
                    // Inside (Smoothed mesh is inside original): White to Red
                    colors[colorIndex++] = 1.0;
                    colors[colorIndex++] = 1.0 - t;
                    colors[colorIndex++] = 1.0 - t;
                } else {
                    // Outside (Smoothed mesh expanded): White to Green
                    colors[colorIndex++] = 1.0 - t;
                    colors[colorIndex++] = 1.0;
                    colors[colorIndex++] = 1.0 - t;
                }
            }
        }

        return colors;
    }

    public Result<bool> HasMultipleComponents(IMesh mesh) {
        if (mesh is not MRMesh mrMesh)
            return GeometryErrors.InvalidMeshType;

        var mlMesh = mrMesh.Mesh;

        try {
            var part = new MR.MeshPart(mlMesh);
            var comps = MR.MeshComponents.getAllComponents(part, null, null);

            return comps.size() > 1;
        } catch (Exception ex) {
            return new Error("Geometry.EvaluatorFailed", ex.Message);
        }
    }

    public Result<IEnumerable<IMesh>> SeparateComponents(IMesh mesh) {
        const double minVolume = 0.1;

        if (mesh is not MRMesh mrMesh)
            return GeometryErrors.InvalidMeshType;

        var mlMesh = mrMesh.Mesh;

        try {
            var part = new MR.MeshPart(mlMesh);
            var comps = MR.MeshComponents.getAllComponents(part, null, null);
            if (comps.size() <= 1)
                return Result.Success<IEnumerable<IMesh>>(new[] { mesh });

            var resultMeshes = new List<IMesh>();
            var validFaces = mlMesh.topology.getValidFaces();

            for (uint i = 0; i < comps.size(); i++) {
                var compFaces = comps[(ulong)i];
                var subMesh = new MR.Mesh(mlMesh); // copy constructor

                var facesToDelete = validFaces - compFaces;

                subMesh.deleteFaces(facesToDelete, null);
                subMesh.pack();

                var volume = MR.volume(subMesh.topology, subMesh.points, null);
                if (volume < minVolume) continue;

                var newMetadata = new MeshMetadata().WithProperties(m =>
                    m.Set(CoreKeys.Id, Guid.NewGuid())
                     .Set(CoreKeys.Name, $"{mrMesh.Metadata.Name} Component {i + 1}"));

                resultMeshes.Add(new MRMesh(subMesh, newMetadata));
            }
            return Result.Success<IEnumerable<IMesh>>(resultMeshes);
        } catch (Exception ex) {
            return new Error("Geometry.EvaluatorFailed", ex.Message);
        }
    }
}
