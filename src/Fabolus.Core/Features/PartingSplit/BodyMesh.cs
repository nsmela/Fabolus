using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.PartingSplit;

/// <summary>
/// The surface a parting line is traced on: the body as it stood after the Transform-stage commands
/// and before a mould was generated around it.
///
/// <para>
/// Named rather than passed as a bare <see cref="IMesh"/> because the distinction matters. The line
/// has to be traced on the body, not on the mould shell around it - the shell's outer offset surface
/// does not carry the same silhouette crossings, and tracing it gives a line in the wrong place. A
/// named type is what stops the two being swapped by accident.
/// </para>
/// </summary>
public class BodyMesh
{
    public IMesh Mesh { get; }

    /// <summary>
    /// The mould this body was recovered from, when it was recovered from one.
    ///
    /// <para>
    /// Optional because a body does not need a mould to exist. Recovering one from a mould is the
    /// usual route - that is what <see cref="Create(IGeometryEngine, MouldMesh)"/> does - but a
    /// caller that already holds the body, such as a test tracing a line on a generated solid, has
    /// nothing to name here. Requiring it made those callers unable to construct the type at all.
    /// </para>
    /// </summary>
    public Maybe<MouldMesh> ParentMould { get; }

    private BodyMesh(IMesh mesh, Maybe<MouldMesh> parentMould)
    {
        Mesh = mesh;
        ParentMould = parentMould;
    }

    /// <summary>Wraps a mesh that is already the body, with no mould behind it.</summary>
    public static Result<BodyMesh> Create(IMesh mesh) =>
        mesh is null ? MeshErrors.NullSource : new BodyMesh(mesh, Maybe<MouldMesh>.None());

    /// <summary>
    /// Recovers the body from inside a mould, by replaying the mould's command history back to the
    /// point just before the Mould command ran.
    /// </summary>
    public static Result<BodyMesh> Create(IGeometryEngine engine, MouldMesh mould)
    {
        if (mould is null) return MeshErrors.NullSource;

        var result = CommandReplay.GetMeshAtStage(engine, mould.Mesh, CommandPriority.Transform);
        if (result.IsFailure) return result.Error;

        return new BodyMesh(result.Value, Maybe<MouldMesh>.Some(mould));
    }
}
