using BasicResults;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Decal;

public sealed class ClearDecals
{
    private readonly IGeometryEngine _engine;

    public ClearDecals(IGeometryEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Reverts text embossing/engraving on a mesh and its record: removes the Decal commands and
    /// replays the remaining upstream commands against the record's BaseMesh. A mesh with no
    /// decals comes back untouched.
    /// </summary>
    public Result<(IMesh Mesh, MeshRecord Record)> Clear(IMesh mesh, MeshRecord record)
    {
        if (mesh is null)
            return MeshErrors.NullSource;

        if (record.TextDecals().Count == 0)
            return Result<(IMesh, MeshRecord)>.Success((mesh, record));

        var reverted = record
            .WithoutCommand<DecalCommand>()
            .WithoutCommand<MouldDecalCommand>();

        if (reverted.BaseMesh is null)
            return MetadataErrors.MissingBaseMesh;

        var replayResult = CommandReplay.Apply(_engine, reverted.BaseMesh, reverted.Commands);
        if (replayResult.IsFailure) return replayResult.Error;

        var cleared = replayResult.Value.WithMeasurements(_engine);
        return Result<(IMesh, MeshRecord)>.Success((cleared, reverted));
    }

    /// <summary>
    /// Reverts text embossing/engraving on the active workspace mesh.
    /// </summary>
    public Result<Workspace> Execute(Workspace workspace)
    {
        var meshResult = workspace.GetActiveMesh();
        if (meshResult.IsFailure) return meshResult.Error;

        var recordResult = workspace.GetActiveRecord();
        if (recordResult.IsFailure) return recordResult.Error;

        var clearResult = Clear(meshResult.Value, recordResult.Value);
        if (clearResult.IsFailure) return clearResult.Error;

        var (mesh, record) = clearResult.Value;
        return workspace.UpdateMesh(record.Id, mesh, record);
    }
}
