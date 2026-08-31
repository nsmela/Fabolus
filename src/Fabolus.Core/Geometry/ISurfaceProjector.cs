using System.Numerics;

namespace Fabolus.Core.Geometry;

/// <summary>
/// Maps a point to the closest point on a surface, over and over, against one mesh.
///
/// <para>
/// A type rather than a bare <see cref="Func{Vector3, Vector3}"/> because the cost is all in the
/// setup: answering a query needs a spatial index over the mesh, and building that per call would
/// dwarf every caller here - a loop relaxation projects each of its points once per pass, which runs
/// to tens of thousands of queries against the same unchanging surface. Holding the projector holds
/// the index, and disposing it releases the native mesh behind it.
/// </para>
///
/// <para>
/// Pure geometry callers in Fabolus.Core take this as an optional argument, so they stay independent
/// of any engine while still being able to keep a result on the surface it came from - see
/// <see cref="ThicknessParting"/>.
/// </para>
/// </summary>
public interface ISurfaceProjector : IDisposable
{
    /// <summary>
    /// The closest point on the surface to <paramref name="point"/>. Unsigned and unconditional: a
    /// point already on the surface comes back where it started, and one inside comes back on the
    /// nearest face rather than being pushed through.
    /// </summary>
    Vector3 Project(Vector3 point);
}
