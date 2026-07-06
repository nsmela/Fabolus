using Fabolus.Core.Common;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Moulds;

public sealed class ClearMould {
    private readonly IGeometryEngine _engine;

    public ClearMould(IGeometryEngine engine) {
        _engine = engine;
    }

    /// <summary>
    /// Undoes mould generation in place: replays this mesh's own Commands (minus the
    /// MouldDefinition) against its BaseMesh, so any other applied operations (e.g. a prior
    /// rotation) are preserved. No separate Workspace entry to remove or reactivate - Mould
    /// never forks.
    /// </summary>
    public Result<Workspace> Execute(Workspace workspace) {
        var getMeshResult = workspace.GetActiveMesh();
        if (getMeshResult.IsFailure) return getMeshResult.Error;

        var activeMesh = getMeshResult.Value;

        var mouldResult = activeMesh.Metadata.MouldDefinition();
        if (mouldResult.HasNoValue) return workspace;

        // The copy is consumed by the replay.
        var baseMesh = activeMesh.Metadata.GetBaseMesh().Value;
        var revertedMetadata = activeMesh.Metadata.WithoutCommand<MouldDefinition>();

        var replayResult = CommandReplay.Apply(_engine, baseMesh, revertedMetadata.Commands);
        if (replayResult.IsFailure) return replayResult.Error;

        var currentMesh = replayResult.Value;

        var topology = _engine.Evaluators.ValidateTopology(currentMesh).Value;
        var stats = _engine.Evaluators.GetStatistics(currentMesh).Value;
        var metadata = revertedMetadata.WithProperties(m => m
            .Set(MeshIOKeys.Stats, stats)
            .Set(MeshIOKeys.Topology, topology));

        var finalMesh = currentMesh.WithMetadata(metadata);
        return workspace.UpdateMesh(finalMesh);
    }
}
