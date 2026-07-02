using Fabolus.Core.Common;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Smoothing;

public sealed class ResetSmoothing {
    private readonly IGeometryEngine _engine;

    public ResetSmoothing(IGeometryEngine engine) {
        _engine = engine;
    }

    /// <summary>
    /// Undoes smoothing in place: replays this mesh's own Commands (minus SmoothSettings, and
    /// anything higher-priority that depended on it, e.g. a generated Mould) against its
    /// BaseMesh, so any other applied operations (e.g. a prior rotation) are preserved. No
    /// separate Workspace entry to remove or reactivate - Smoothing never forks.
    /// </summary>
    public Result<Workspace> Execute(Workspace workspace) {
        var getMeshResult = workspace.GetActiveMesh();
        if (getMeshResult.IsFailure) return getMeshResult.Error;

        var activeMesh = getMeshResult.Value;

        var smoothResult = activeMesh.Metadata.GetSmoothing();
        if (smoothResult.HasNoValue) return workspace;

        var baseMesh = activeMesh.Metadata.BaseMesh.Value;
        var revertedMetadata = activeMesh.Metadata.WithoutCommand<SmoothSettings>();

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
