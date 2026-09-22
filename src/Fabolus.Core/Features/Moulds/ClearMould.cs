using BasicResults;
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
        var recordResult = workspace.GetActiveRecord();
        if (recordResult.IsFailure) return recordResult.Error;

        var record = recordResult.Value;
        if (record.MouldDefinition() is null) return workspace;

        var reverted = record.WithoutCommand<MouldDefinition>();
        if (reverted.BaseMesh is null) return MetadataErrors.MissingBaseMesh;

        var replayResult = CommandReplay.Apply(_engine, reverted.BaseMesh, reverted.Commands);
        if (replayResult.IsFailure) return replayResult.Error;

        var mesh = replayResult.Value.WithMeasurements(_engine);
        return workspace.UpdateMesh(reverted.Id, mesh, reverted);
    }
}
