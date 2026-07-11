using System.Numerics;
using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.PartingSplit;

/// <summary>
/// Splits a mould mesh into two pieces along an already-generated <see cref="PartingLine"/>
/// (see <see cref="PartingLineFeature"/>). Each resulting piece records a <see cref="SplitCommand"/>
/// in its metadata so the split can be reconstructed on import without re-deriving the parting
/// line from scratch.
/// </summary>
public sealed class SplitMouldFeature
{
    private readonly IGeometryEngine _engine;

    public SplitMouldFeature(IGeometryEngine engine)
    {
        _engine = engine;
    }

    public Result<Workspace> Execute(Workspace workspace, Guid mouldMeshId, PartingLine partingLine, Vector3 pullDirection)
    {
        if (!partingLine.IsValid) return MeshErrors.InvalidPartingLine;
        if (pullDirection == Vector3.Zero) return MeshErrors.InvalidPullDirection;

        var meshResult = workspace.GetMesh(mouldMeshId);
        if (meshResult.IsFailure) return meshResult.Error;

        var mould = meshResult.Value;
        var direction = Vector3.Normalize(pullDirection);

        var positiveCommand = new SplitCommand(partingLine, direction, PartingSide.Positive);
        var positiveResult = positiveCommand.Apply(_engine, mould);
        if (positiveResult.IsFailure) return positiveResult.Error;

        var negativeCommand = new SplitCommand(partingLine, direction, PartingSide.Negative);
        var negativeResult = negativeCommand.Apply(_engine, mould);
        if (negativeResult.IsFailure) return negativeResult.Error;

        var positiveMetadata = mould.Metadata.WithProperties(m => m
                .Set(CoreKeys.Id, Guid.NewGuid())
                .Set(CoreKeys.Name, $"{mould.Metadata.Name} (Positive)")
                .Set(CoreKeys.CreatedBy, "Split"))
            .WithDerivedFrom(mouldMeshId)
            .WithCommand(positiveCommand);

        var negativeMetadata = mould.Metadata.WithProperties(m => m
                .Set(CoreKeys.Id, Guid.NewGuid())
                .Set(CoreKeys.Name, $"{mould.Metadata.Name} (Negative)")
                .Set(CoreKeys.CreatedBy, "Split"))
            .WithDerivedFrom(mouldMeshId)
            .WithCommand(negativeCommand);

        var positivePiece = positiveResult.Value.WithMetadata(positiveMetadata);
        var negativePiece = negativeResult.Value.WithMetadata(negativeMetadata);

        var addPositiveResult = workspace.AddMesh(positivePiece, setActive: false);
        if (addPositiveResult.IsFailure) return addPositiveResult.Error;
        workspace = addPositiveResult.Value;

        var addNegativeResult = workspace.AddMesh(negativePiece, setActive: false);
        if (addNegativeResult.IsFailure) return addNegativeResult.Error;
        workspace = addNegativeResult.Value;

        return workspace.SetActiveMesh(Guid.Empty);
    }
}
