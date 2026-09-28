using BasicResults;
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
    public Result<IMesh> ComputeUnsmoothedMesh(MeshRecord record) {
        if (record.BaseMesh is null) return MetadataErrors.MissingBaseMesh;

        var reverted = record.WithoutCommand<SmoothSettings>();
        return CommandReplay.Apply(_engine, record.BaseMesh, reverted.Commands);
    }

    /// <summary>
    /// Undoes smoothing in place: replays this mesh's own Commands (minus SmoothSettings, and
    /// anything higher-priority that depended on it, e.g. a generated Mould) against its
    /// BaseMesh, so any other applied operations (e.g. a prior rotation) are preserved. No
    /// separate Workspace entry to remove or reactivate - Smoothing never forks.
    /// </summary>
    public Result<Workspace> Execute(Workspace workspace) {
        var recordResult = workspace.GetActiveRecord();
        if (recordResult.IsFailure) return recordResult.Error;

        var record = recordResult.Value;
        if (record.Smoothing() is null) return workspace;

        var replayResult = ComputeUnsmoothedMesh(record);
        if (replayResult.IsFailure) return replayResult.Error;

        var mesh = replayResult.Value.WithMeasurements(_engine);
        return workspace.UpdateMesh(record.Id, mesh, record.WithoutCommand<SmoothSettings>());
    }
}
