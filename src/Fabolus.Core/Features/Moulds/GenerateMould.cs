using Fabolus.Core.Common;
using Fabolus.Core.Features.MeshIO;
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

        var mesh = meshResult.Value;

        var applyResult = mouldDefinition.Apply(_geometryEngine, mesh);
        if (applyResult.IsFailure) return applyResult.Error;

        var mouldMesh = applyResult.Value;

        // Boolean operations hand back bare metadata (just Id/Name/CreatedBy), so the final
        // metadata is built from the source mesh's own metadata instead (mesh.Metadata, not
        // mouldMesh.Metadata) - Id/Commands/BaseMesh all carry forward automatically (BaseMesh
        // is guaranteed present - Workspace.AddMesh establishes it for every mesh the moment
        // it enters the workspace), same as Smoothing and Rotate/Translate now that Mould
        // operates in place too.
        var statsResult = _geometryEngine.Evaluators.GetStatistics(mouldMesh);
        if (statsResult.IsFailure) return statsResult.Error;

        var topologyResult = _geometryEngine.Evaluators.ValidateTopology(mouldMesh);
        if (topologyResult.IsFailure) return topologyResult.Error;

        var metadata = mesh.Metadata.WithProperties(m => m
            .Set(MeshIOKeys.Stats, statsResult.Value)
            .Set(MeshIOKeys.Topology, topologyResult.Value));

        metadata = metadata.WithCommand(mouldDefinition with { TargetMeshId = meshId });

        var finalMesh = mouldMesh.WithMetadata(metadata);

        return workspace.UpdateMesh(finalMesh);
    }
}
