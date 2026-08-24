using Fabolus.Core.Common;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Emboss;

public sealed class ClearTextEmboss
{
    private readonly IGeometryEngine _engine;

    public ClearTextEmboss(IGeometryEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Reverts text embossing/engraving: removes TextEmbossCommand and TextDecal metadata,
    /// then replays remaining upstream commands against the BaseMesh.
    /// Downstream commands that depended on this geometry (like Mould) are cleared.
    /// </summary>
    public Result<Workspace> Execute(Workspace workspace)
    {
        var getMeshResult = workspace.GetActiveMesh();
        if (getMeshResult.IsFailure) return getMeshResult.Error;

        var activeMesh = getMeshResult.Value;

        var decalsResult = activeMesh.Metadata.TextDecals();
        if (decalsResult.HasNoValue) return workspace;

        var baseMeshResult = activeMesh.Metadata.GetBaseMesh();
        if (baseMeshResult.HasNoValue) return workspace;

        var baseMesh = baseMeshResult.Value;

        var revertedMetadata = activeMesh.Metadata
            .WithoutCommand<TextEmbossCommand>()
            .WithoutCommand<MouldTextEmbossCommand>()
            .WithoutProperty(TextEmbossKeys.TextDecals);

        var replayResult = CommandReplay.Apply(_engine, baseMesh, revertedMetadata.Commands);
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
