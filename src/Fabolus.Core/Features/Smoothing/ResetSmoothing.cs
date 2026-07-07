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
    /// The mesh as it would look with only its smoothing removed: BaseMesh with all remaining
    /// commands (e.g. a rotation) replayed on top. This is the aligned "unsmoothed twin" of
    /// the current geometry - comparing against raw BaseMesh instead would drift out of
    /// alignment as soon as any transform is applied after smoothing, since BaseMesh stays
    /// pristine and never rotates/translates.
    /// Always returns an owned mesh the caller must dispose - never a shared instance.
    /// </summary>
    public Result<IMesh> ComputeUnsmoothedMesh(IMesh mesh) {
        var baseCopy = mesh.Metadata.GetBaseMesh();
        if (baseCopy.HasNoValue) return MetadataErrors.MissingBaseMesh;

        var revertedMetadata = mesh.Metadata.WithoutCommand<SmoothSettings>();
        return CommandReplay.Apply(_engine, baseCopy.Value, revertedMetadata.Commands);
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

        if (activeMesh.Metadata.DerivedFrom.HasValue) {
            var parentId = activeMesh.Metadata.DerivedFrom.Value;
            var workspaceResult = workspace.RemoveMesh(activeMesh.Metadata.Id);
            if (workspaceResult.IsFailure) return workspaceResult.Error;
            return workspaceResult.Value.SetActiveMesh(parentId);
        }

        // Legacy behavior for meshes smoothed before the fork-on-smooth update
        var revertedMetadata = activeMesh.Metadata.WithoutCommand<SmoothSettings>();

        var replayResult = ComputeUnsmoothedMesh(activeMesh);
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
