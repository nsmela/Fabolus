using Fabolus.Core.Common;

namespace Fabolus.Core.Geometry;

/// <summary>
/// Provides operations to modify mesh topology and structure (e.g., smoothing, decimation, subdivision).
/// Every operation returns a NEW mesh the caller owns - never its input instance, even when
/// the operation is a no-op - so callers can dispose pipeline intermediates unconditionally.
/// </summary>
public interface IGeometryModifiers
{
    /// <summary>
    /// Offsets the mesh surface by a specific distance using implicit remeshing.
    /// </summary>
    /// <param name="mesh">The mesh to offset.</param>
    /// <param name="offset">The distance to offset.</param>
    /// <param name="inwards">If true, offsets inwards.</param>
    /// <param name="resolution">The resolution of the offset operation. Higher values result in more detailed meshes.</param>
    Result<IMesh> Offset(IMesh input, float offsetDistance, float cellSize = 0.0f);

    Result<IMesh> OffsetDouble(IMesh input, float offsetDistance, int iterations = 1, float cellSize = 0.0f);

    /// <summary>
    /// Resizes (decimates) the mesh to a target triangle count.
    /// </summary>
    /// <param name="mesh">The mesh to resize.</param>
    /// <param name="targetTriangleCount">The desired triangle count.</param>
    Result<IMesh> Resize(IMesh mesh, int targetTriangleCount);

    /// <summary>
    /// Repairs common mesh faults (degeneracies, non-manifold edges, small holes).
    /// </summary>
    Result<IMesh> Repair(IMesh input);

    /// <summary>
    /// Specifically resolves self-intersecting faces in the mesh - see
    /// <see cref="SelfIntersectionRepair"/> for the two ways of doing it.
    /// </summary>
    Result<IMesh> RepairSelfIntersections(
        IMesh input, SelfIntersectionRepair method = SelfIntersectionRepair.Relax);
}

/// <summary>
/// How self-intersecting faces are resolved. The two are different operations, not settings of one,
/// and they fail differently - a fold that relaxation cannot pull apart is exactly the case cutting
/// handles, and a thin sheet that cutting perforates is exactly the case relaxation leaves intact.
/// </summary>
public enum SelfIntersectionRepair
{
    /// <summary>
    /// Move the vertices around each crossing until the faces come apart, expanding the worked region
    /// a few rings out so the correction spreads instead of denting.
    ///
    /// <para>
    /// The zero value and the default, because it is what every existing caller has been getting: the
    /// underlying settings default to it, so this is the behaviour already measured throughout the
    /// parting pipeline. It preserves the surface exactly where it does not intersect, which is why
    /// it is safe on a thin cutter - nothing is removed, so there is nothing to leave a hole.
    /// </para>
    ///
    /// <para>
    /// It cannot resolve a genuine fold. Relaxation only slides vertices along the surface it already
    /// has, so where two sheets have passed through each other there is no arrangement of those
    /// vertices that separates them.
    /// </para>
    /// </summary>
    Relax,

    /// <summary>
    /// Cut the intersecting region out and re-triangulate the hole it leaves.
    ///
    /// <para>
    /// Handles the fold that relaxation cannot, because it does not try to keep the offending faces -
    /// it replaces them. The cost is that it is destructive where it acts: on a sheet thinner than
    /// the region it excises it can take out both sides and leave a hole, and a cutter with a hole in
    /// it does not sever a mould.
    /// </para>
    /// </summary>
    CutAndFill,
}
