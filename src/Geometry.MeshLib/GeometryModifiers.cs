using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

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
            using var model = new MR.Mesh(mrMesh.Mesh); // Deep copy
            using var mp = new MR.MeshPart(model);
            using var parms = new MR.OffsetParameters()
            {
                voxelSize = cellSize > 0 ? cellSize : MR.suggestVoxelSize(mp, 1e6f),
            };

            var result = MR.offsetMesh(mp, offsetDistance, parms);
            
            var newMetadata = input.Metadata.WithProperties(m =>
                m.Set(CoreKeys.Name, $"Offset ({input.Metadata.Name})")
                 .Set(CoreKeys.CreatedBy, $"Offset({offsetDistance})"));
            return _engine.CreateMesh(result, newMetadata);
        }
        catch (Exception ex)
        {
            return new Error("Geometry.OffsetFailed", ex.ToString());
        }
    }

    public Result<IMesh> OffsetDouble(IMesh input, float offsetDistance, int iterations = 1, float cellSize = 0.0f)
    {
        // Even the no-op path returns a new mesh - modifiers never return their input
        // instance, so callers can dispose pipeline intermediates unconditionally.
        if (iterations < 1) return Result.Success(input.Clone());
        if (input is not MRMesh mrMesh) return GeometryErrors.InvalidMeshType;

        try
        {
            var currentMesh = new MR.Mesh(mrMesh.Mesh); // Deep copy
            
            for (int i = 0; i < iterations; i++)
            {
                using var mp = new MR.MeshPart(currentMesh);
                using var parms = new MR.OffsetParameters()
                {
                    voxelSize = cellSize > 0 ? cellSize : MR.suggestVoxelSize(mp, 1e6f),
                };

                var nextMesh = MR.doubleOffsetMesh(mp, offsetDistance, -offsetDistance, parms);
                if (nextMesh.points.vec.size() == 0) 
                {
                    nextMesh.Dispose();
                    break;
                }
                currentMesh.Dispose();
                currentMesh = nextMesh;
            }

            var newMetadata = input.Metadata.WithProperties(m =>
                m.Set(CoreKeys.Name, $"DoubleOffset ({input.Metadata.Name})")
                 .Set(CoreKeys.CreatedBy, $"OffsetDouble({offsetDistance}, {iterations})"));
            return _engine.CreateMesh(currentMesh, newMetadata);
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
            {
                // Already at/below target: nothing to decimate, but still hand back a new
                // mesh (the clone just made) - modifiers never return their input instance,
                // so callers can dispose pipeline intermediates unconditionally.
                return _engine.CreateMesh(clone, mrMesh.Metadata);
            }

            int toDelete = currentTriangles - targetTriangleCount;

            using var settings = new MR.DecimateSettings();
            settings.maxDeletedFaces = toDelete;

            MR.decimateMesh(clone, settings);

            var newMetadata = mrMesh.Metadata.WithProperties(m =>
                m.Set(CoreKeys.Name, $"Resized ({mrMesh.Metadata.Name})")
                 .Set(CoreKeys.CreatedBy, $"Resize({targetTriangleCount})"));
            return _engine.CreateMesh(clone, newMetadata);
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
            using var parms = new MR.FixMeshDegeneraciesParams();
            MR.fixMeshDegeneracies(mesh, parms);
            MR.fixMultipleEdges(mesh);

            var newMetadata = input.Metadata.WithProperties(m =>
                m.Set(CoreKeys.Name, $"Repaired ({input.Metadata.Name})")
                 .Set(CoreKeys.CreatedBy, "Repair"));
            return _engine.CreateMesh(mesh, newMetadata);
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
            using var settings = new MR.SelfIntersections.Settings();
            MR.SelfIntersections.fix(mesh, settings);

            var newMetadata = input.Metadata.WithProperties(m =>
                m.Set(CoreKeys.Name, $"Repaired SI ({input.Metadata.Name})")
                 .Set(CoreKeys.CreatedBy, "RepairSelfIntersections"));
            return _engine.CreateMesh(mesh, newMetadata);
        }
        catch (Exception ex)
        {
            return new Error("Geometry.RepairSelfIntersectionsFailed", ex.ToString());
        }
    }
}
