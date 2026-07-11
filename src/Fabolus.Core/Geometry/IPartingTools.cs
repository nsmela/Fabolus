using System.Numerics;
using Fabolus.Core.Common;

namespace Fabolus.Core.Geometry;

/// <summary>
/// Provides mould-parting-line operations: finding where a mesh's silhouette (relative to a
/// pull direction) crosses itself, and building the solid tool geometry used to split a mould
/// along that silhouette. Implementations work directly against the underlying engine's native
/// mesh type to avoid round-tripping through IMesh's flat vertex/triangle arrays.
/// </summary>
public interface IPartingTools
{
    /// <summary>
    /// Computes the parting line of <paramref name="mesh"/> for the given pull direction: the
    /// set of closed loops where the surface normal is perpendicular to the pull direction
    /// (i.e. the silhouette seen looking along that direction). A mesh with an internal hole
    /// along the pull direction (e.g. a tunnel) produces more than one loop - everything past
    /// the largest (outer) loop is an internal hole that needs its own shut-off surface.
    /// </summary>
    /// <param name="mesh">The mesh to analyse - should be the base/body mesh, not a mould shell.</param>
    /// <param name="pullDirection">The direction the two halves will be pulled apart along.</param>
    /// <param name="noiseThreshold">
    /// Loops shorter than this fraction of the mesh's largest dimension are discarded as noise.
    /// </param>
    Result<PartingLine> GeneratePartingLine(IMesh mesh, Vector3 pullDirection, float noiseThreshold = 0.1f);

    /// <summary>
    /// Builds a single watertight solid that can be used to split a mould along
    /// <paramref name="partingLine"/>: the "positive" side (the half in the direction of
    /// <paramref name="pullDirection"/>) is enclosed by the tool, the "negative" side is not.
    /// Internal holes get their own shut-off patch merged into the same solid (never touching
    /// the main dividing surface) so the whole split can be done with a single boolean pass -
    /// Intersect(mould, tool) for the positive piece, Subtract(mould, tool) for the negative one.
    /// </summary>
    /// <param name="referenceMesh">
    /// The mesh <paramref name="partingLine"/> was generated from. Not yet used by the current
    /// (flat dividing plane + per-hole plug) implementation, but kept in the signature so a
    /// future contour-following tool (matching the body's curvature instead of a flat plane,
    /// to avoid undercuts on complex shapes) doesn't need a breaking signature change.
    /// </param>
    /// <param name="partingLine">The loops to build the tool around.</param>
    /// <param name="pullDirection">The direction that defines which side is "positive".</param>
    /// <param name="toolBounds">
    /// Bounds the resulting tool must fully cover (typically the mould's own statistics, since
    /// the mould is larger than the body the parting line was computed from).
    /// </param>
    Result<IMesh> GenerateSplitTool(IMesh referenceMesh, PartingLine partingLine, Vector3 pullDirection, MeshStatistics toolBounds);
}
