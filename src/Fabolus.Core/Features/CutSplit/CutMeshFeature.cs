using BasicResults;
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
    ///
    /// This is the one feature that genuinely forks: the halves are new workspace entries with
    /// their own identities, not new geometry for the entry <paramref name="record"/> names. It
    /// mints those identities here rather than leaving the caller to, because naming the halves
    /// after the mesh they were cut from is part of cutting it.
    /// </summary>
    public Result<(CutHalf Top, CutHalf Bottom)> Execute(IMesh mesh, MeshRecord record, Vector3 planeOrigin, Vector3 planeNormal)
    {
        if (mesh is null) return new Error("CutMesh.NullMesh", "Mesh cannot be null.");
        if (planeNormal == Vector3.Zero) return new Error("CutMesh.InvalidNormal", "Plane normal cannot be zero.");

        var statsResult = _engine.Evaluators.GetStatistics(mesh);
        if (statsResult.IsFailure) return statsResult.Error;
        
        var stats = statsResult.Value;
        float dx = (float)(stats.BoundsMax.X - stats.BoundsMin.X);
        float dy = (float)(stats.BoundsMax.Y - stats.BoundsMin.Y);
        float dz = (float)(stats.BoundsMax.Z - stats.BoundsMin.Z);
        float maxDim = Math.Max(dx, Math.Max(dy, dz)) * 2f;
        if (maxDim < 100f) maxDim = 100f;

        float d = maxDim / 2f;
        Vector3[] vertices = {
            new Vector3(-d, -d, 0),
             new Vector3(d, -d, 0),
             new Vector3(d, d, 0),
            new Vector3(-d, d, 0),
            new Vector3(-d, -d, maxDim),
             new Vector3(d, -d, maxDim),
             new Vector3(d, d, maxDim),
            new Vector3(-d, d, maxDim)
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

        var cubeResult = _engine.CreateMesh([.. vertices], [.. triangles], new MeshMetadata("CutCube", "System"));
        if (cubeResult.IsFailure) return cubeResult.Error;
        var cubeMesh = cubeResult.Value;
        
        var zAxis = Vector3.UnitZ;
        var normal = planeNormal.Normalize();
        var axis = zAxis.Cross(normal);
        double dot = zAxis.Dot(normal);
        
        System.Numerics.Quaternion q;
        if (dot < -0.9999) q = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitX, (float)Math.PI);
        else if (dot > 0.9999) q = System.Numerics.Quaternion.Identity;
        else q = System.Numerics.Quaternion.Normalize(new System.Numerics.Quaternion((float)axis.X, (float)axis.Y, (float)axis.Z, (float)(1 + dot)));
        
        var rotatedCubeResult = _engine.Transforms.Rotate(cubeMesh, q);
        if (rotatedCubeResult.IsFailure) return rotatedCubeResult.Error;

        var positionedCubeResult = _engine.Transforms.Translate(rotatedCubeResult.Value, planeOrigin.X, planeOrigin.Y, planeOrigin.Z);
        if (positionedCubeResult.IsFailure) return positionedCubeResult.Error;
        var positionedCube = positionedCubeResult.Value;
        
        var topResult = _engine.Booleans.Intersect(mesh, positionedCube); 
        var bottomResult = _engine.Booleans.Subtract(mesh, positionedCube); 
        
        if (topResult.IsFailure) return topResult.Error;
        if (bottomResult.IsFailure) return bottomResult.Error;

        var top = topResult.Value.WithMeasurements(_engine);
        var bottom = bottomResult.Value.WithMeasurements(_engine);

        return Result<(CutHalf, CutHalf)>.Success((
            new CutHalf(top, Half(record, "Top")),
            new CutHalf(bottom, Half(record, "Bottom"))));
    }

    // A half starts its own history: the cut is what produced it, and the geometry it was cut
    // from is its base mesh rather than the original's.
    private static MeshRecord Half(MeshRecord source, string side) => new() {
        Id = Guid.NewGuid(),
        Name = $"{source.Name} ({side})",
        CreatedBy = "CutSplit",
    };
}

/// <summary>One side of a cut: the geometry, and the workspace entry it should be added under.</summary>
public readonly record struct CutHalf(IMesh Mesh, MeshRecord Record);
