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
    Result<IMesh> Subtract(IMesh meshA, IMesh meshB);
    
    /// <summary>
    /// Computes the intersection of two meshes.
    /// </summary>
    Result<IMesh> Intersect(IMesh meshA, IMesh meshB);

}
