using Fabolus.Core.Common;
using System.Numerics;

namespace Fabolus.Core.Geometry;

public interface IGeometryEvaluators {

    /// <summary>
    /// Calculates vertex colors for a mesh based on its distance to another mesh.
    /// Used for deviation mapping (heatmap).
    /// </summary>
    Result<double[]> CalculateDeviationColors(IMesh current, IMesh original, double maxDeviation = 0.4);

    /// <summary>
    /// Computes the outward unit normal of every vertex, in the same order that
    /// <see cref="GetRenderData"/> emits vertices. Used for direction-relative
    /// per-vertex analysis such as overhang colouring.
    /// </summary>
    Result<IReadOnlyList<Vector3>> ComputeVertexNormals(IMesh mesh);

    /// <summary>
    /// Calculates statistics for a mesh.
    /// </summary>
    Result<MeshStatistics> GetStatistics(IMesh mesh);

    /// <summary>
    /// Returns zero-allocation memory spans for rendering (WebGL/Three.js).
    /// </summary>
    Result<RenderData> GetRenderData(IMesh mesh);

    /// <summary>
    /// Checks if a mesh consists of multiple disconnected components.
    /// </summary>
    Result<bool> HasMultipleComponents(IMesh mesh);

    /// <summary>
    /// Measures how thick a solid is, by probing inward from every face along its own normal until
    /// the probe comes out the far side. See <see cref="WallThickness"/> for what the result means.
    ///
    /// <para>
    /// Purely a measurement of the shape - it reads the same however finely the mesh is tessellated,
    /// which is what makes it useful on bodies where curvature-based measures are at the mercy of
    /// the triangle size.
    /// </para>
    /// </summary>
    Result<WallThickness> MeasureWallThickness(IMesh mesh, WallThicknessOptions options);

    /// <summary>
    /// Separates a single mesh into multiple disjoint components.
    /// </summary>
    Result<IEnumerable<IMesh>> SeparateComponents(IMesh mesh);

    /// <summary>
    /// Validates the topology of a mesh.
    /// </summary>
    Result<TopologyValidation> ValidateTopology(IMesh mesh);

    /// <summary>
    /// Casts a ray against the mesh and returns the closest intersection point, normal, and distance.
    /// </summary>
    Result<RaycastHit> Raycast(IMesh mesh, Vector3 rayOrigin, Vector3 rayDirection);
}
