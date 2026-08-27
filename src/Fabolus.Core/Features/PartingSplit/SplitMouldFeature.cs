using System.Numerics;
using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.PartingSplit;

/// <summary>
/// Commits a parting operation to the workspace: <see cref="Execute"/> splits a mould into two
/// pieces and adds both, <see cref="ExecuteCut"/> subtracts the parting mesh and adds the single
/// still-joined result. Each added piece records the command that produced it, so it can be
/// reconstructed on import from the same parameters this ran with.
///
/// This is the entry point for committing a parting; callers hold it directly. It depends on
/// <see cref="PartingMeshFeature"/> for the geometry and never the reverse - the two used to call
/// each other, each constructing the other on the fly, which made the layering circular.
/// </summary>
public sealed class SplitMouldFeature
{
    private readonly IGeometryEngine _engine;

    public SplitMouldFeature(IGeometryEngine engine)
    {
        _engine = engine;
    }

    public Result<Workspace> Execute(
        Workspace workspace,
        Guid mouldMeshId,
        PartingLineParameters lineParameters,
        PartingMeshParameters meshParameters)
    {
        var prep = Prepare(workspace, mouldMeshId, lineParameters);
        if (prep.IsFailure) return prep.Error;
        var (mould, validated, normalizedLine) = prep.Value;

        // Split once here; each piece still records its own SplitCommand so it can be rebuilt
        // independently on import (SplitCommand.Apply reruns this same split and keeps its side).
        var splitResult = new PartingMeshFeature(_engine).SplitMould(validated, normalizedLine, meshParameters);
        if (splitResult.IsFailure) return splitResult.Error;

        var (positiveMesh, negativeMesh) = splitResult.Value;

        var positivePiece = positiveMesh.WithMetadata(PieceMetadata(
            mould, mouldMeshId, "Positive", new SplitCommand(normalizedLine, meshParameters, PartingSide.Positive)));
        var negativePiece = negativeMesh.WithMetadata(PieceMetadata(
            mould, mouldMeshId, "Negative", new SplitCommand(normalizedLine, meshParameters, PartingSide.Negative)));

        var addPositiveResult = workspace.AddMesh(positivePiece, setActive: false);
        if (addPositiveResult.IsFailure) return addPositiveResult.Error;
        workspace = addPositiveResult.Value;

        var addNegativeResult = workspace.AddMesh(negativePiece, setActive: false);
        if (addNegativeResult.IsFailure) return addNegativeResult.Error;
        workspace = addNegativeResult.Value;

        return workspace.SetActiveMesh(Guid.Empty);
    }

    /// <summary>
    /// Cuts the mould and adds the single joined result, recording a <see cref="CutCommand"/> (with the
    /// export <paramref name="mode"/>) so the cut - and the user's separated/combined intent - replay on
    /// import. The geometry is one mesh either way; <paramref name="mode"/> only steers export.
    /// </summary>
    public Result<Workspace> ExecuteCut(
        Workspace workspace,
        Guid mouldMeshId,
        PartingLineParameters lineParameters,
        PartingMeshParameters meshParameters,
        PartingResultMode mode)
    {
        var prep = Prepare(workspace, mouldMeshId, lineParameters);
        if (prep.IsFailure) return prep.Error;
        var (mould, validated, normalizedLine) = prep.Value;

        var cutResult = new PartingMeshFeature(_engine).CutMould(validated, normalizedLine, meshParameters);
        if (cutResult.IsFailure) return cutResult.Error;

        var piece = cutResult.Value.WithMetadata(PieceMetadata(
            mould, mouldMeshId, "Cut", new CutCommand(normalizedLine, meshParameters, mode)));

        var addResult = workspace.AddMesh(piece, setActive: false);
        if (addResult.IsFailure) return addResult.Error;
        workspace = addResult.Value;

        return workspace.SetActiveMesh(Guid.Empty);
    }

    /// <summary>Validates the mould and normalizes the pull direction - shared by both commit paths.</summary>
    private static Result<(IMesh Mould, MouldMesh Validated, PartingLineParameters NormalizedLine)> Prepare(
        Workspace workspace, Guid mouldMeshId, PartingLineParameters lineParameters)
    {
        if (lineParameters.PullDirection == Vector3.Zero) return MeshErrors.InvalidPullDirection;

        var meshResult = workspace.GetMesh(mouldMeshId);
        if (meshResult.IsFailure) return meshResult.Error;

        var mouldResult = MouldMesh.Create(meshResult.Value);
        if (mouldResult.IsFailure) return mouldResult.Error;

        var normalized = lineParameters with { PullDirection = Vector3.Normalize(lineParameters.PullDirection) };
        return (meshResult.Value, mouldResult.Value, normalized);
    }

    private static MeshMetadata PieceMetadata(IMesh mould, Guid mouldMeshId, string suffix, IMeshCommand command) =>
        mould.Metadata.WithProperties(m => m
                .Set(CoreKeys.Id, Guid.NewGuid())
                .Set(CoreKeys.Name, $"{mould.Metadata.Name} ({suffix})")
                .Set(CoreKeys.CreatedBy, "Split"))
            .WithDerivedFrom(mouldMeshId)
            .WithCommand(command);
}
