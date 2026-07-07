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
        try
        {
            using var model = input.ToMRMesh();
            using var mp = new MR.MeshPart(model);
            using var parms = new MR.OffsetParameters()
            {
                voxelSize = cellSize > 0 ? cellSize : MR.suggestVoxelSize(mp, 1e6f),
            };

            using var result = MR.offsetMesh(mp, offsetDistance, parms);
            
            var newMetadata = input.Metadata.WithProperties(m =>
                m.Set(CoreKeys.Name, $"Offset ({input.Metadata.Name})")
                 .Set(CoreKeys.CreatedBy, $"Offset({offsetDistance})"));
            return Result.Success(result.ToIMesh(newMetadata));
        }
        catch (Exception ex)
        {
            return new Error("Geometry.OffsetFailed", ex.ToString());
        }
    }

    public Result<IMesh> OffsetDouble(IMesh input, float offsetDistance, int iterations = 1, float cellSize = 0.0f)
    {
        if (iterations < 1) return Result.Success(input);
        
        try
        {
            var currentMesh = input.ToMRMesh();
            
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
                 
            var result = Result.Success(currentMesh.ToIMesh(newMetadata));
            currentMesh.Dispose();
            return result;
        }
        catch (Exception ex)
        {
            return new Error("Geometry.OffsetDoubleFailed", ex.ToString());
        }
    }

    public Result<IMesh> Resize(IMesh mesh, int targetTriangleCount)
    {
        try
        {
            using var clone = mesh.ToMRMesh();
            int currentTriangles = (int)clone.topology.getValidFaces().count();
            if (currentTriangles <= targetTriangleCount)
            {
                return Result.Success(mesh);
            }

            int toDelete = currentTriangles - targetTriangleCount;

            using var settings = new MR.DecimateSettings();
            settings.maxDeletedFaces = toDelete;

            MR.decimateMesh(clone, settings);

            var newMetadata = mesh.Metadata.WithProperties(m =>
                m.Set(CoreKeys.Name, $"Resized ({mesh.Metadata.Name})")
                 .Set(CoreKeys.CreatedBy, $"Resize({targetTriangleCount})"));
            return Result.Success(clone.ToIMesh(newMetadata));
        }
        catch (Exception ex)
        {
            return new Error("Geometry.ResizeFailed", ex.ToString());
        }
    }

    public Result<IMesh> Repair(IMesh input)
    {
        try
        {
            using var mesh = input.ToMRMesh();
            using var parms = new MR.FixMeshDegeneraciesParams();
            MR.fixMeshDegeneracies(mesh, parms);
            MR.fixMultipleEdges(mesh);

            var newMetadata = input.Metadata.WithProperties(m =>
                m.Set(CoreKeys.Name, $"Repaired ({input.Metadata.Name})")
                 .Set(CoreKeys.CreatedBy, "Repair"));
            return Result.Success(mesh.ToIMesh(newMetadata));
        }
        catch (Exception ex)
        {
            return new Error("Geometry.RepairFailed", ex.ToString());
        }
    }

    public Result<IMesh> RepairSelfIntersections(IMesh input)
    {
        try
        {
            using var mesh = input.ToMRMesh();
            using var settings = new MR.SelfIntersections.Settings();
            MR.SelfIntersections.fix(mesh, settings);

            var newMetadata = input.Metadata.WithProperties(m =>
                m.Set(CoreKeys.Name, $"Repaired SI ({input.Metadata.Name})")
                 .Set(CoreKeys.CreatedBy, "RepairSelfIntersections"));
            return Result.Success(mesh.ToIMesh(newMetadata));
        }
        catch (Exception ex)
        {
            return new Error("Geometry.RepairSelfIntersectionsFailed", ex.ToString());
        }
    }
}
