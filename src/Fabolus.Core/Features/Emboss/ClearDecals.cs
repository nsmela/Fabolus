using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Emboss;

public sealed class ClearDecals
{
    private readonly IGeometryEngine _engine;

    public ClearDecals(IGeometryEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Reverts text embossing/engraving directly on an <see cref="IMesh"/>:
    /// removes Decal commands and replays remaining upstream commands against its BaseMesh.
    /// </summary>
    public Result<IMesh> Clear(IMesh mesh)
    {
        if (mesh is null)
            return MeshErrors.NullSource;

        var decalsResult = mesh.Metadata.TextDecals();
        if (decalsResult.HasNoValue)
            return Result.Success(mesh);

        var baseMeshResult = mesh.Metadata.GetBaseMesh();
        if (baseMeshResult.HasNoValue)
            return MetadataErrors.MissingBaseMesh;

        var baseMesh = baseMeshResult.Value;

        var revertedMetadata = mesh.Metadata
            .WithoutCommand<DecalCommand>()
            .WithoutCommand<MouldDecalCommand>()
            .WithoutCommand<TextEmbossCommand>()
            .WithoutCommand<MouldTextEmbossCommand>();

        var replayResult = CommandReplay.Apply(_engine, baseMesh, revertedMetadata.Commands);
        if (replayResult.IsFailure) return replayResult.Error;

        var currentMesh = replayResult.Value;
        var finalMesh = currentMesh.WithRefreshedStatsAndTopology(_engine, revertedMetadata);

        return Result.Success(finalMesh);
    }

    /// <summary>
    /// Reverts text embossing/engraving on the active workspace mesh.
    /// </summary>
    public Result<Workspace> Execute(Workspace workspace)
    {
        var getMeshResult = workspace.GetActiveMesh();
        if (getMeshResult.IsFailure) return getMeshResult.Error;

        var activeMesh = getMeshResult.Value;

        var clearResult = Clear(activeMesh);
        if (clearResult.IsFailure) return clearResult.Error;

        return workspace.UpdateMesh(clearResult.Value);
    }
}
