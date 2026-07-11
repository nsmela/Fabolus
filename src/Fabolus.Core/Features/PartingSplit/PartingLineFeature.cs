using System.Numerics;
using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.PartingSplit;

/// <summary>
/// Computes the parting line for a mould, cheap enough for live preview while the user drags
/// the pull-direction gizmo. Always analyses the *base body* the mould was generated around
/// (as it stood right before the Mould command ran - i.e. after Rotate/Translate/Smoothing but
/// before the mould shell was generated), never the mould shell itself, since the mould's outer
/// offset surface doesn't reliably carry the same silhouette crossings as the body it encloses.
/// </summary>
public sealed class PartingLineFeature
{
    private readonly IGeometryEngine _engine;

    public PartingLineFeature(IGeometryEngine engine)
    {
        _engine = engine;
    }

    public Result<PartingLine> Execute(IMesh mouldMesh, Vector3 pullDirection)
    {
        if (mouldMesh is null) return MeshErrors.NullSource;
        if (pullDirection == Vector3.Zero) return MeshErrors.InvalidPullDirection;

        var bodyResult = CommandReplay.GetMeshAtStage(_engine, mouldMesh, CommandPriority.Transform);
        if (bodyResult.IsFailure) return bodyResult.Error;

        return _engine.PartingTools.GeneratePartingLine(bodyResult.Value, Vector3.Normalize(pullDirection));
    }
}
