using Fabolus.Core.Common;

namespace Fabolus.Core.Geometry;

public interface IGeometryEvaluators {
    /// <summary>
    /// Validates the topology of a mesh.
    /// </summary>
    Result<TopologyValidation> ValidateTopology(IMesh mesh);

    /// <summary>
    /// Calculates statistics for a mesh.
    /// </summary>
    Result<MeshStatistics> GetStatistics(IMesh mesh);

    /// <summary>
    /// Returns zero-allocation memory spans for rendering (WebGL/Three.js).
    /// </summary>
    Result<RenderData> GetRenderData(IMesh mesh);

    /// <summary>
    /// Calculates vertex colors for a mesh based on its distance to another mesh.
    /// Used for deviation mapping (heatmap).
    /// </summary>
    Result<double[]> CalculateDeviationColors(IMesh current, IMesh original, double maxDeviation = 1.0);
}
