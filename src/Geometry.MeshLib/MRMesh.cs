using System.Numerics;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace GeometryMeshLib;

/// <summary>
/// MeshLib-specific implementation of IMesh wrapping MR.Mesh natively.
/// Internal to prevent leaking MeshLib types to the core domain.
/// </summary>
internal sealed class MRMesh : IMesh
{
    public Vector3[] Vertices { get; }
    public int[] Triangles { get; }
    public MeshMetadata Metadata { get; }

    public int VertexCount => Vertices.Length;
    public int TriangleCount => Triangles.Length / 3;
    public bool IsEmpty => VertexCount == 0 || TriangleCount == 0;

    internal MRMesh(Vector3[] vertices, int[] triangles, MeshMetadata metadata)
    {
        Vertices = vertices;
        Triangles = triangles;
        Metadata = metadata;
    }

    public IMesh WithMetadata(MeshMetadata metadata)
    {
        return new MRMesh(Vertices, Triangles, metadata);
    }
}
