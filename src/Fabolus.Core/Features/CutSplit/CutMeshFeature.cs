using BasicResults;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using GeometryEngine.Core.Geometry.Primitives;

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

        var normal = Direction.From(planeNormal);
        if (!normal.HasValue) return new Error("CutMesh.InvalidNormal", "Plane normal cannot be zero.");

        var splitResult = _engine.Booleans.Split(mesh, Plane.FromNormalAndPoint(normal.Value, planeOrigin));
        if (splitResult.IsFailure) return splitResult.Error;

        var top = splitResult.Value.Front.Measured(_engine);
        var bottom = splitResult.Value.Back.Measured(_engine);

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
