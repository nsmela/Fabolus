using System.Numerics;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace GeometryManifold;

/// <summary>
/// Manifold-backed implementation of <see cref="IMesh"/>. Like its MeshLib counterpart it holds
/// plain vertex/triangle arrays rather than a live native handle: Manifold only accepts closed,
/// manifold input, so operations that do not need that guarantee (transforms, statistics, render
/// data) still work on meshes a native handle would have rejected outright.
/// </summary>
internal sealed class ManifoldMesh : IMesh
{
    public Vector3[] Vertices { get; }
    public int[] Triangles { get; }
    public MeshMetadata Metadata { get; }

    public int VertexCount => Vertices.Length;
    public int TriangleCount => Triangles.Length / 3;
    public bool IsEmpty => VertexCount == 0 || TriangleCount == 0;

    internal ManifoldMesh(Vector3[] vertices, int[] triangles, MeshMetadata metadata)
    {
        Vertices = vertices;
        Triangles = triangles;
        Metadata = metadata;
    }

    public IMesh WithMetadata(MeshMetadata metadata) =>
        new ManifoldMesh(Vertices, Triangles, metadata);
}
