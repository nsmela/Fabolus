using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Core.Features.MeshIO;
using System;
using System.Numerics;

namespace Fabolus.Core.Features.CutSplit;

public sealed class CutMeshFeature
{
    private readonly IGeometryEngine _engine;

    public CutMeshFeature(IGeometryEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Cuts a mesh with a plane, returning the top and bottom halves.
    /// The top half is in the direction of the plane normal.
    /// </summary>
    public Result<(IMesh top, IMesh bottom)> Execute(IMesh mesh, Vector3 planeOrigin, Vector3 planeNormal)
    {
        if (mesh is null) return new Error("CutMesh.NullMesh", "Mesh cannot be null.");
        if (planeNormal == Vector3.Zero) return new Error("CutMesh.InvalidNormal", "Plane normal cannot be zero.");

        var statsResult = _engine.Evaluators.GetStatistics(mesh);
        if (statsResult.IsFailure) return statsResult.Error;
        
        var stats = statsResult.Value;
        float dx = (float)(stats.MaxX - stats.MinX);
        float dy = (float)(stats.MaxY - stats.MinY);
        float dz = (float)(stats.MaxZ - stats.MinZ);
        float maxDim = Math.Max(dx, Math.Max(dy, dz)) * 2f;
        if (maxDim < 100f) maxDim = 100f;

        float d = maxDim / 2f;
        double[] vertices = {
            -d, -d, 0,
             d, -d, 0,
             d,  d, 0,
            -d,  d, 0,
            -d, -d, maxDim,
             d, -d, maxDim,
             d,  d, maxDim,
            -d,  d, maxDim
        };

        int[] triangles = {
            // Bottom
            0, 3, 1,
            1, 3, 2,
            // Top
            4, 5, 7,
            5, 6, 7,
            // Front (-Y)
            0, 1, 5,
            0, 5, 4,
            // Right (+X)
            1, 2, 6,
            1, 6, 5,
            // Back (+Y)
            2, 3, 7,
            2, 7, 6,
            // Left (-X)
            3, 0, 4,
            3, 4, 7
        };

        var cubeResult = _engine.CreateMesh(vertices.AsSpan(), triangles.AsSpan());
        if (cubeResult.IsFailure) return cubeResult.Error;
        var cubeMesh = cubeResult.Value;
        
        var zAxis = Vector3.UnitZ;
        var normal = Vector3.Normalize(planeNormal);
        var axis = Vector3.Cross(zAxis, normal);
        float dot = Vector3.Dot(zAxis, normal);
        
        Quaternion q;
        if (dot < -0.9999f) q = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI);
        else if (dot > 0.9999f) q = Quaternion.Identity;
        else q = Quaternion.Normalize(new Quaternion(axis, 1 + dot));
        
        var rotatedCubeResult = _engine.Transforms.Rotate(cubeMesh, q);
        if (rotatedCubeResult.IsFailure) return rotatedCubeResult.Error;

        var positionedCubeResult = _engine.Transforms.Translate(rotatedCubeResult.Value, planeOrigin.X, planeOrigin.Y, planeOrigin.Z);
        if (positionedCubeResult.IsFailure) return positionedCubeResult.Error;
        var positionedCube = positionedCubeResult.Value;
        
        var topResult = _engine.Booleans.Intersect(mesh, positionedCube); 
        var bottomResult = _engine.Booleans.Subtract(mesh, positionedCube); 
        
        if (topResult.IsFailure) return topResult.Error;
        if (bottomResult.IsFailure) return bottomResult.Error;

        var top = topResult.Value;
        var bottom = bottomResult.Value;

        // Add metadata, stats, and topology to the resulting meshes
        var topMetadata = top.Metadata.WithProperties(m => 
            m.Set(CoreKeys.Id, Guid.NewGuid())
             .Set(CoreKeys.Name, $"{mesh.Metadata.Name} (Top)")
             .Set(CoreKeys.CreatedBy, "CutSplit"));
        var topStatsResult = _engine.Evaluators.GetStatistics(top);
        if (topStatsResult.IsSuccess) topMetadata = topMetadata.WithMeshStats(topStatsResult.Value);
        var topTopologyResult = _engine.Evaluators.ValidateTopology(top);
        if (topTopologyResult.IsSuccess) topMetadata = topMetadata.WithTopology(topTopologyResult.Value);

        var bottomMetadata = bottom.Metadata.WithProperties(m => 
            m.Set(CoreKeys.Id, Guid.NewGuid())
             .Set(CoreKeys.Name, $"{mesh.Metadata.Name} (Bottom)")
             .Set(CoreKeys.CreatedBy, "CutSplit"));
        var bottomStatsResult = _engine.Evaluators.GetStatistics(bottom);
        if (bottomStatsResult.IsSuccess) bottomMetadata = bottomMetadata.WithMeshStats(bottomStatsResult.Value);
        var bottomTopologyResult = _engine.Evaluators.ValidateTopology(bottom);
        if (bottomTopologyResult.IsSuccess) bottomMetadata = bottomMetadata.WithTopology(bottomTopologyResult.Value);

        return Result.Success((top.WithMetadata(topMetadata), bottom.WithMetadata(bottomMetadata)));
    }
}
