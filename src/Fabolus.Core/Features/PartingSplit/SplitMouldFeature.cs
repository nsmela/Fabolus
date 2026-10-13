using BasicResults;
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
        var (record, validated, normalizedLine) = prep.Value;

        // Split once here; each piece still records its own SplitCommand so it can be rebuilt
        // independently on import (SplitCommand.Apply reruns this same split and keeps its side).
        var splitResult = new PartingMeshFeature(_engine).SplitMould(validated, normalizedLine, meshParameters);
        if (splitResult.IsFailure) return splitResult.Error;

        var (positiveMesh, negativeMesh) = splitResult.Value;

        var addPositiveResult = workspace.AddMesh(
            positiveMesh.Measured(_engine),
            PieceRecord(record, "Positive", new SplitCommand(normalizedLine, meshParameters, PartingSide.Positive)),
            setActive: false);
        if (addPositiveResult.IsFailure) return addPositiveResult.Error;
        workspace = addPositiveResult.Value;

        var addNegativeResult = workspace.AddMesh(
            negativeMesh.Measured(_engine),
            PieceRecord(record, "Negative", new SplitCommand(normalizedLine, meshParameters, PartingSide.Negative)),
            setActive: false);
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
        var (record, validated, normalizedLine) = prep.Value;

        var cutResult = new PartingMeshFeature(_engine).CutMould(validated, normalizedLine, meshParameters);
        if (cutResult.IsFailure) return cutResult.Error;

        var addResult = workspace.AddMesh(
            cutResult.Value.Measured(_engine),
            PieceRecord(record, "Cut", new CutCommand(normalizedLine, meshParameters, mode)),
            setActive: false);
        if (addResult.IsFailure) return addResult.Error;
        workspace = addResult.Value;

        return workspace.SetActiveMesh(Guid.Empty);
    }

    /// <summary>Validates the mould and normalizes the pull direction - shared by both commit paths.</summary>
    private static Result<(MeshRecord Record, MouldMesh Validated, PartingLineParameters NormalizedLine)> Prepare(
        Workspace workspace, Guid mouldMeshId, PartingLineParameters lineParameters)
    {
        if (lineParameters.PullDirection == Vector3.Zero) return MeshErrors.InvalidPullDirection;

        var meshResult = workspace.GetMesh(mouldMeshId);
        if (meshResult.IsFailure) return meshResult.Error;

        var recordResult = workspace.GetRecord(mouldMeshId);
        if (recordResult.IsFailure) return recordResult.Error;

        var mouldResult = MouldMesh.Create(meshResult.Value, recordResult.Value);
        if (mouldResult.IsFailure) return mouldResult.Error;

        var normalized = lineParameters with { PullDirection = lineParameters.PullDirection.Normalize() };
        return (recordResult.Value, mouldResult.Value, normalized);
    }

    /// <summary>
    /// A piece's entry: a new identity named after the mould, carrying the mould's whole history with
    /// the parting <paramref name="command"/> on the end. Unlike a plane cut, a piece keeps the
    /// history rather than starting its own - the parting line is traced on the body inside the
    /// mould, so rebuilding a piece means rebuilding the body and the mould first.
    /// </summary>
    private static MeshRecord PieceRecord(MeshRecord mould, string suffix, IMeshCommand command) =>
        (mould with
        {
            Id = Guid.NewGuid(),
            Name = $"{mould.Name} ({suffix})",
            CreatedBy = "Split",
            PendingMould = null,
        }).WithCommand(command);
}
