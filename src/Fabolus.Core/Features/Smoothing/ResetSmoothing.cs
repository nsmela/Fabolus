using System.Linq;
using Fabolus.Core.Common;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.Smoothing;

public sealed class ResetSmoothing {
    private readonly IGeometryEngine _engine;

    public ResetSmoothing(IGeometryEngine engine) {
        _engine = engine;
    }

    /// <summary>
    /// Undoes smoothing in place: replays this mesh's own Commands (minus SmoothSettings)
    /// against its BaseMesh, so any other applied operations (e.g. a prior rotation) are
    /// preserved. No separate Workspace entry to remove or reactivate - Smoothing never forks.
    /// </summary>
    public Result<Workspace> Execute(Workspace workspace) {
        var getMeshResult = workspace.GetActiveMesh();
        if (getMeshResult.IsFailure) return getMeshResult.Error;

        var activeMesh = getMeshResult.Value;

        var smoothResult = activeMesh.Metadata.GetSmoothing();
        if (smoothResult.HasNoValue) return workspace;

        var baseMesh = activeMesh.Metadata.BaseMesh.GetValueOrDefault(activeMesh);
        var remainingCommands = activeMesh.Metadata.Commands.Where(c => c is not SmoothSettings).ToList();

        IMesh currentMesh = baseMesh;
        foreach (var command in remainingCommands) {
            var applyResult = command.Apply(_engine, currentMesh);
            if (applyResult.IsFailure) return applyResult.Error;

            currentMesh = applyResult.Value;
        }

        var topology = _engine.Evaluators.ValidateTopology(currentMesh).Value;
        var stats = _engine.Evaluators.GetStatistics(currentMesh).Value;
        var metadata = activeMesh.Metadata.WithProperties(m => m
            .Set(MeshIOKeys.Stats, stats)
            .Set(MeshIOKeys.Topology, topology));
        metadata = metadata.WithoutCommand<SmoothSettings>();

        var finalMesh = currentMesh.WithMetadata(metadata);
        return workspace.UpdateMesh(finalMesh);
    }
}
