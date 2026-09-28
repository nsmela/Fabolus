using BasicResults;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Moulds;

public sealed class GenerateMould
{
    private readonly IGeometryEngine _geometryEngine;

    public GenerateMould(IGeometryEngine geometryEngine)
    {
        _geometryEngine = geometryEngine;
    }

    public Result<Workspace> Execute(Workspace workspace, Guid meshId, MouldDefinition mouldDefinition)
    {
        var meshResult = workspace.GetMesh(meshId);
        if (meshResult.IsFailure) return meshResult.Error;

        var recordResult = workspace.GetRecord(meshId);
        if (recordResult.IsFailure) return recordResult.Error;

        var applyResult = mouldDefinition.Apply(_geometryEngine, meshResult.Value);
        if (applyResult.IsFailure) return applyResult.Error;

        // The mould is built with booleans, so the geometry that comes back is neither operand and
        // carries no annotations - it is measured fresh below. The entry it fills is unchanged:
        // its identity, name and history all live on the record, which is why this replaces the
        // entry's geometry in place rather than forking a new one.
        var mouldMesh = applyResult.Value.WithMeasurements(_geometryEngine);

        var record = recordResult.Value.WithCommand(mouldDefinition with { TargetMeshId = meshId });

        return workspace.UpdateMesh(meshId, mouldMesh, record);
    }
}
