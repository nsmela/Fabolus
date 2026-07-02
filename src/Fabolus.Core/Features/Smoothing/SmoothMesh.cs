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
    /// Smooths the specified mesh in place. Always re-derives from BaseMesh (the mesh's own
    /// pristine ancestor) rather than the currently-smoothed geometry, so repeat Apply calls
    /// don't stack/degrade - and never forks a new mesh, so there's only ever one Workspace
    /// entry for this mesh, matching Rotate/Translate.
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
        var baseMesh = activeMesh.Metadata.BaseMesh.GetValueOrDefault(activeMesh);

        var applyResult = settings.Apply(Engine, baseMesh);
        if (applyResult.IsFailure) return applyResult.Error;

        // Finalize metadata and update workspace
        var finalMesh = applyResult.Value;

        var topology = Engine.Evaluators.ValidateTopology(finalMesh).Value;
        var stats = Engine.Evaluators.GetStatistics(finalMesh).Value;
        var metadata = activeMesh.Metadata.WithProperties(m => m
            .Set(MeshIOKeys.Stats, stats)
            .Set(MeshIOKeys.Topology, topology));
        metadata = metadata.WithCommand(settings);
        // Deliberately propagates from activeMesh, not from the pipeline's own BaseMesh
        // output: the pipeline's input was baseMesh itself (a clone, once one exists), whose
        // own metadata never records "I am a base" - checking it would re-clone on every
        // repeat Apply instead of reusing the one already established on activeMesh.
        metadata = metadata.WithPropagatedBaseMesh(activeMesh);

        finalMesh = finalMesh.WithMetadata(metadata);

        return workspace.UpdateMesh(finalMesh);
    }
}
