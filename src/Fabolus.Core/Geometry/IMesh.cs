using System.Numerics;
using Fabolus.Core.Geometry.Metadata;
namespace Fabolus.Core.Geometry;

/// <summary>
/// Represents a 3D mesh in the workspace, backed by pure C# data.
/// </summary>
public interface IMesh
{
    /// <summary>
    /// The raw vertex coordinates.
    /// </summary>
    Vector3[] Vertices { get; }

    /// <summary>
    /// The raw triangle indices.
    /// </summary>
    int[] Triangles { get; }

    /// <summary>
    /// All metadata associated with this mesh.
    /// </summary>
    MeshMetadata Metadata { get; }

    /// <summary>
    /// Number of vertices in the mesh.
    /// </summary>
    int VertexCount { get; }
    
    /// <summary>
    /// Number of triangles in the mesh.
    /// </summary>
    int TriangleCount { get; }
    
    /// <summary>
    /// If the mesh contains no Vertices and Triangles.
    /// </summary>
    bool IsEmpty { get; }

    /// <summary>
    /// Creates a new mesh with updated metadata.
    /// </summary>
    IMesh WithMetadata(MeshMetadata metadata);
}
