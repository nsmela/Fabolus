using Fabolus.Core.Common;
using Fabolus.Core.Geometry;

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

        var generateResult = mouldDefinition.Generate(_geometryEngine, mesh);
        if (generateResult.IsFailure) return generateResult.Error;

        var mouldMesh = generateResult.Value;

        // Subtract the target mesh from the mould
        var targetSubtractedResult = _geometryEngine.Booleans.Subtract(mouldMesh, mesh);
        if (targetSubtractedResult.IsFailure) return targetSubtractedResult.Error;
        
        mouldMesh = targetSubtractedResult.Value;

        // Subtract the air channels from the mould
        foreach (var channel in mouldDefinition.AirChannels)
        {
            var channelMeshResult = channel.DomainModel.Generate(_geometryEngine, Features.AirChannels.AirChannelRenderMode.Full);
            if (channelMeshResult.IsFailure) return channelMeshResult.Error;

            var subtractedResult = _geometryEngine.Booleans.Subtract(mouldMesh, channelMeshResult.Value);
            if (subtractedResult.IsFailure) return subtractedResult.Error;

            mouldMesh = subtractedResult.Value;
        }

        var finalMesh = mouldMesh.WithMetadata(mouldMesh.Metadata.WithMouldDefinition(mouldDefinition with { TargetMeshId = meshId }));
        
        var addResult = workspace.AddMesh(finalMesh);
        if (addResult.IsFailure) return addResult;
        
        return addResult.Value.SetActiveMesh(finalMesh.Metadata.Id);
    }
}
