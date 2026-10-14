using BasicResults;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.PartingSplit;

public static class MouldMeshErrors
{
    public static readonly Error NoMouldMetadata = new("MouldMesh.NoMouldMetadata", "The mesh has no mould in its history");
}

/// <summary>
/// A mesh known to be a generated mould, together with the way back to the body it was built
/// around - which is what a parting line is traced on.
///
/// <para>
/// The way back depends on where the mould came from. One in the workspace has a record, and the
/// body is its history replayed to the Transform stage. One being rebuilt by a replay has no record
/// of its own yet, but the replay has already passed through the body on its way to the mould, so
/// it is taken from there rather than replayed a second time.
/// </para>
/// </summary>
public sealed class MouldMesh
{
    private readonly Func<IGeometryEngine, Result<IMesh>> _body;

    public IMesh Mesh { get; }

    private MouldMesh(IMesh mesh, Func<IGeometryEngine, Result<IMesh>> body)
    {
        Mesh = mesh;
        _body = body;
    }

    /// <summary>A mould in the workspace: <paramref name="record"/> is its entry, which names it as a mould.</summary>
    public static Result<MouldMesh> Create(IMesh mesh, MeshRecord record)
    {
        var result = IsMould(mesh, record);
        if (result.IsFailure) return result.Error;

        return new MouldMesh(mesh, engine => CommandReplay.GetMeshAtStage(engine, mesh, record, CommandPriority.Transform));
    }

    /// <summary>A mould a replay has just rebuilt, with the stages it passed through on the way.</summary>
    public static Result<MouldMesh> FromReplay(IMesh mesh, ReplayStages stages)
    {
        if (mesh is null) return MeshErrors.NullSource;
        if (!stages.Passed(CommandPriority.Mould)) return MouldMeshErrors.NoMouldMetadata;

        var body = stages.MeshAt(CommandPriority.Transform);
        return new MouldMesh(mesh, _ => Result.Success(body));
    }

    public static Result IsMould(IMesh mesh, MeshRecord record)
    {
        if (mesh is null || record is null) return MeshErrors.NullSource;
        if (record.MouldDefinition() is null) return MouldMeshErrors.NoMouldMetadata;

        return Result.Success();
    }

    /// <summary>The body this mould was built around, as it stood after the Transform-stage commands.</summary>
    public Result<IMesh> RecoverBody(IGeometryEngine engine) => _body(engine);
}
