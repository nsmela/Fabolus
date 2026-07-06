using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace GeometryMeshLib;

/// <summary>
/// MeshLib-specific implementation of IMesh wrapping MR.Mesh natively.
/// Internal to prevent leaking MeshLib types to the core domain.
/// </summary>
internal sealed class MRMesh : IMesh
{
    internal MR.Mesh Mesh { get; }

    public MeshMetadata Metadata { get; }

    public int VertexCount => (int)Mesh.topology.getValidVerts().count();

    public int TriangleCount => (int)Mesh.topology.getValidFaces().count();

    public bool IsEmpty => throw new NotImplementedException();

    private bool _ownsNativeMesh;
    private bool _disposed;

    internal MRMesh(MR.Mesh mesh, MeshMetadata metadata, bool ownsNativeMesh = true)
    {
        Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _ownsNativeMesh = ownsNativeMesh;
    }

    public IMesh Clone() => new MRMesh(new MR.Mesh(Mesh), Metadata, ownsNativeMesh: true);

    /// <summary>
    /// Returns a new wrapper around the same native mesh with different metadata,
    /// transferring native-memory ownership to it - the old wrapper stays readable but
    /// disposing it becomes a no-op. Usage is linear (mesh = mesh.WithMetadata(...)), so
    /// exactly one owner exists per native mesh at any time.
    /// </summary>
    public IMesh WithMetadata(MeshMetadata metadata)
    {
        var next = new MRMesh(Mesh, metadata, _ownsNativeMesh);
        _ownsNativeMesh = false;
        return next;
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        if (_ownsNativeMesh)
        {
            Mesh?.Dispose();
        }
        _disposed = true;
    }
}
