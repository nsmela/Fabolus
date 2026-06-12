namespace Fabolus.Core.Geometry;

/// <summary>
/// Mesh data prepared for rendering.
/// Zero-allocation spans for WebGL/GPU upload.
/// </summary>
public sealed class RenderData
{
    /// <summary>
    /// Vertex positions (x, y, z interleaved).
    /// Length = VertexCount * 3
    /// </summary>
    public required double[] Vertices { get; init; }
    
    /// <summary>
    /// Triangle indices.
    /// Length = TriangleCount * 3
    /// </summary>
    public required int[] Triangles { get; init; }
    
    /// <summary>
    /// Vertex normals (nx, ny, nz interleaved).
    /// Length = VertexCount * 3
    /// </summary>
    public double[]? Normals { get; init; }
    
    /// <summary>
    /// Vertex colors (r, g, b interleaved, 0-1 range).
    /// Length = VertexCount * 3
    /// </summary>
    public double[]? Colors { get; init; }
}
