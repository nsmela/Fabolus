using Fabolus.Core.Common;

namespace Fabolus.Core.Geometry;

/// <summary>
/// Provides operations to modify mesh topology and structure (e.g., smoothing, decimation, subdivision).
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
    /// Specifically resolves self-intersecting faces in the mesh.
    /// </summary>
    Result<IMesh> RepairSelfIntersections(IMesh input);
}
