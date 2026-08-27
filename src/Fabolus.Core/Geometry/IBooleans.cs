using System.Numerics;
using Fabolus.Core.Common;

namespace Fabolus.Core.Geometry;

/// <summary>
/// Interface for Boolean operations on meshes.
/// </summary>
public interface IBooleans
{
    /// <summary>
    /// Computes the union of two meshes.
    /// </summary>
    Result<IMesh> Union(IMesh meshA, IMesh meshB);
    
    /// <summary>
    /// Subtracts meshB from meshA.
    /// </summary>
    /// <param name="shiftB">
    /// Moves <paramref name="meshB"/> by this much for the operation only - the result is expressed
    /// against the mesh where it actually sits. See <see cref="Intersect"/> for what it is for.
    /// </param>
    Result<IMesh> Subtract(IMesh meshA, IMesh meshB, Vector3 shiftB = default);

    /// <summary>
    /// Computes the intersection of two meshes.
    /// </summary>
    /// <param name="shiftB">
    /// Moves <paramref name="meshB"/> by this much for the operation only, leaving where it really is
    /// untouched.
    ///
    /// <para>
    /// This is how a clearance between two boolean results is meant to be produced. Thickening a
    /// cutter to open a gap puts the two results' faces on the same surface, and the boolean resolves
    /// coincident input by virtually displacing vertices ("Simulation of Simplicity"), so what comes
    /// back is a valid mesh of zero volume. Shifting the cutter instead gives each operation its own
    /// non-coincident geometry. See MeshLib discussion 4933.
    /// </para>
    /// </param>
    Result<IMesh> Intersect(IMesh meshA, IMesh meshB, Vector3 shiftB = default);

}
