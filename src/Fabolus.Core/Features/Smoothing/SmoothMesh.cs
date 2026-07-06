using Fabolus.Core.Common;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Smoothing;

/// <summary>
/// Smooths a mesh using a volumetric Erosion-Dilation-Resize pipeline.
/// This approach produces high-quality, manifold, and feature-preserved surfaces.
/// </summary>
public sealed class SmoothMesh(IGeometryEngine Engine) {
    /// <summary>
    /// Smooths the specified mesh in place. Records the new SmoothSettings (replacing any
    /// prior one - overwrite, not stack) and replays the full updated Commands list against
    /// BaseMesh, so any sibling command already applied (e.g. a prior Rotate) is preserved in
    /// the result instead of being silently discarded, and repeat Apply calls don't
    /// stack/degrade. Never forks a new mesh, so there's only ever one Workspace entry for
    /// this mesh, matching Rotate/Translate.
    /// </summary>
    /// <param name="workspace">The current workspace.</param>
    /// <param name="settings">The smoothing parameters to apply.</param>
    public Result<Workspace> Execute(
        Workspace workspace,
        SmoothSettings settings)
    {
        var getMeshResult = workspace.GetActiveMesh();
        if (getMeshResult.IsFailure) return getMeshResult.Error;

        var activeMesh = getMeshResult.Value;
        var updatedMetadata = activeMesh.Metadata.WithCommand(settings);

        // BaseMesh is guaranteed present - Workspace.AddMesh establishes it for every mesh
        // the moment it enters the workspace. The copy is consumed by the replay.
        var baseMesh = activeMesh.Metadata.GetBaseMesh().Value;
        var replayResult = CommandReplay.Apply(Engine, baseMesh, updatedMetadata.Commands);
        if (replayResult.IsFailure) return replayResult.Error;

        var finalMesh = replayResult.Value;

        var topology = Engine.Evaluators.ValidateTopology(finalMesh).Value;
        var stats = Engine.Evaluators.GetStatistics(finalMesh).Value;
        // BaseMesh carries forward automatically here (updatedMetadata was built from
        // activeMesh.Metadata, which already has it).
        var metadata = updatedMetadata.WithProperties(m => m
            .Set(MeshIOKeys.Stats, stats)
            .Set(MeshIOKeys.Topology, topology));

        finalMesh = finalMesh.WithMetadata(metadata);

        return workspace.UpdateMesh(finalMesh);
    }
}
