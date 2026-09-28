using BasicResults;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Smoothing;

/// <summary>
/// Smooths a mesh using a volumetric Erosion-Dilation-Resize pipeline.
/// This approach produces high-quality, manifold, and feature-preserved surfaces.
/// </summary>
public sealed class SmoothMesh(IGeometryEngine Engine) {
    /// <summary>
    /// Smooths the active mesh in place. Records the new SmoothSettings (replacing any prior one -
    /// overwrite, not stack) and replays the full updated Commands list against BaseMesh, so any
    /// sibling command already applied (e.g. a prior Rotate) is preserved in the result instead of
    /// being silently discarded, and repeat Apply calls don't stack/degrade. Never forks a new
    /// mesh, so there's only ever one Workspace entry for this mesh, matching Rotate/Translate.
    /// </summary>
    /// <param name="workspace">The current workspace.</param>
    /// <param name="settings">The smoothing parameters to apply.</param>
    public Result<Workspace> Execute(Workspace workspace, SmoothSettings settings) {
        var recordResult = workspace.GetActiveRecord();
        if (recordResult.IsFailure) return recordResult.Error;

        var record = recordResult.Value.WithCommand(settings);
        if (record.BaseMesh is null) return MetadataErrors.MissingBaseMesh;

        var replayResult = CommandReplay.Apply(Engine, record.BaseMesh, record.Commands);
        if (replayResult.IsFailure) return replayResult.Error;

        var mesh = replayResult.Value.WithMeasurements(Engine);
        return workspace.UpdateMesh(record.Id, mesh, record);
    }
}
