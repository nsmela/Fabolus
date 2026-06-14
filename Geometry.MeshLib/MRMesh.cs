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

    public IMesh OriginalMesh { get; }

    public int VertexCount => (int)Mesh.topology.getValidVerts().count();

    public int TriangleCount => (int)Mesh.topology.getValidFaces().count();

    public bool IsEmpty => throw new NotImplementedException();

    internal MRMesh(MR.Mesh mesh, MeshMetadata metadata, IMesh? originalMesh = null)
    {
        Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        OriginalMesh = originalMesh;
    }

    public IMesh Clone() => new MRMesh(new MR.Mesh(Mesh), Metadata);

    public IMesh WithMetadata(MeshMetadata metadata) => new MRMesh(Mesh, metadata);
    
}
