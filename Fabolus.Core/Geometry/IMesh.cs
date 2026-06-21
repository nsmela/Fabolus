using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Geometry;

/// <summary>
/// Represents a 3D mesh in the workspace.
/// </summary>
public interface IMesh : IDisposable
{
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
    /// Is this mesh generated based on another mesh?
    /// Returns empty if this is the original mesh.
    /// </summary>
    IMesh OriginalMesh { get; }
    
    /// <summary>
    /// Creates a deep copy of this mesh.
    /// </summary>
    IMesh Clone();
    
    /// <summary>
    /// Creates a new mesh with updated metadata.
    /// </summary>
    IMesh WithMetadata(MeshMetadata metadata);

}
