using Fabolus.Core.Common;
using Fabolus.Core.Geometry;

namespace GeometryMeshLib;

public sealed class GeometryModifiers : IGeometryModifiers
{
    private readonly GeometryEngine _engine;

    public GeometryModifiers(GeometryEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public Result<IMesh> Offset(IMesh input, float offsetDistance, float cellSize = 0.0f)
    {
        if (input is not MRMesh mrMesh) return GeometryErrors.InvalidMeshType;

        try
        {
            var model = new MR.Mesh(mrMesh.Mesh); // Deep copy
            MR.MeshPart mp = new(model);
            MR.OffsetParameters parms = new()
            {
                voxelSize = cellSize > 0 ? cellSize : MR.suggestVoxelSize(mp, 1e6f),
            };

            var result = MR.offsetMesh(mp, offsetDistance, parms);
            return _engine.CreateMesh(result, input.Metadata, input.OriginalMesh ?? input);
        }
        catch (Exception ex)
        {
            return new Error("Geometry.OffsetFailed", ex.ToString());
        }
    }

    public Result<IMesh> OffsetDouble(IMesh input, float offsetDistance, int iterations = 1, float cellSize = 0.0f)
    {
        if (input is not MRMesh mrMesh) return GeometryErrors.InvalidMeshType;

        try
        {
            var model = new MR.Mesh(mrMesh.Mesh); // Deep copy
            MR.MeshPart mp = new(model);
            MR.OffsetParameters parms = new()
            {
                voxelSize = cellSize > 0 ? cellSize : MR.suggestVoxelSize(mp, 1e6f),
            };

            var resultMesh = model;
            for (int i = 0; i < iterations; i++)
            {
                resultMesh = MR.doubleOffsetMesh(mp, offsetDistance, -offsetDistance, parms);
                if (resultMesh.points.vec.size() == 0) break;
                mp = new MR.MeshPart(resultMesh);
            }

            return _engine.CreateMesh(resultMesh, input.Metadata, input.OriginalMesh ?? input);
        }
        catch (Exception ex)
        {
            return new Error("Geometry.OffsetDoubleFailed", ex.ToString());
        }
    }

    public Result<IMesh> Resize(IMesh mesh, int targetTriangleCount)
    {
        if (mesh is not MRMesh mrMesh) return GeometryErrors.InvalidMeshType;

        try
        {
            var clone = new MR.Mesh(mrMesh.Mesh);
            int currentTriangles = (int)clone.topology.getValidFaces().count();
            if (currentTriangles <= targetTriangleCount)
                return Result.Success(mesh);

            int toDelete = currentTriangles - targetTriangleCount;

            var settings = new MR.DecimateSettings();
            settings.maxDeletedFaces = toDelete;

            MR.decimateMesh(clone, settings);

            return _engine.CreateMesh(clone, mrMesh.Metadata, mrMesh.OriginalMesh ?? mrMesh);
        }
        catch (Exception ex)
        {
            return new Error("Geometry.ResizeFailed", ex.ToString());
        }
    }

    public Result<IMesh> Repair(IMesh input)
    {
        if (input is not MRMesh mrMesh) return GeometryErrors.InvalidMeshType;

        try
        {
            var mesh = new MR.Mesh(mrMesh.Mesh);
            MR.fixMeshDegeneracies(mesh, new MR.FixMeshDegeneraciesParams());
            MR.fixMultipleEdges(mesh);

            return _engine.CreateMesh(mesh, input.Metadata, input.OriginalMesh ?? input);
        }
        catch (Exception ex)
        {
            return new Error("Geometry.RepairFailed", ex.ToString());
        }
    }

    public Result<IMesh> RepairSelfIntersections(IMesh input)
    {
        if (input is not MRMesh mrMesh) return GeometryErrors.InvalidMeshType;

        try
        {
            var mesh = new MR.Mesh(mrMesh.Mesh);
            MR.SelfIntersections.fix(mesh, new MR.SelfIntersections.Settings());

            return _engine.CreateMesh(mesh, input.Metadata, input.OriginalMesh ?? input);
        }
        catch (Exception ex)
        {
            return new Error("Geometry.RepairSelfIntersectionsFailed", ex.ToString());
        }
    }
}
