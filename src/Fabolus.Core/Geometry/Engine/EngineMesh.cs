using System.Collections.Immutable;
using System.Numerics;
using Fabolus.Core.Common;
using Fabolus.Core.Geometry.Metadata;
using GE = GeometryEngine.Core.Geometry;
using GEP = GeometryEngine.Core.Geometry.Primitives;

namespace Fabolus.Core.Geometry.Engine;

/// <summary>
/// The <see cref="IMesh"/> the GeometryEngine adapter hands out: plain arrays and metadata, with
/// no native handle behind it, so a mesh a native kernel would reject still transforms, measures
/// and renders.
/// </summary>
internal sealed class EngineMesh : IMesh
{
    public Vector3[] Vertices { get; }
    public int[] Triangles { get; }
    public MeshMetadata Metadata { get; }

    public int VertexCount => Vertices.Length;
    public int TriangleCount => Triangles.Length / 3;
    public bool IsEmpty => VertexCount == 0 || TriangleCount == 0;

    internal EngineMesh(Vector3[] vertices, int[] triangles, MeshMetadata metadata)
    {
        Vertices = vertices;
        Triangles = triangles;
        Metadata = metadata;
    }

    public IMesh WithMetadata(MeshMetadata metadata) => new EngineMesh(Vertices, Triangles, metadata);
}

/// <summary>
/// Crossing between Fabolus's meshes - single-precision arrays carrying a metadata bag - and
/// GeometryEngine's - immutable double-precision arrays carrying a name. Every widening is exact,
/// so a mesh that goes in and comes back unchanged is bit-for-bit the mesh that went in.
/// </summary>
internal static class EngineConversions
{
    public static Result<GE.IMesh> ToEngine(this IMesh mesh)
    {
        if (mesh is null) return MeshErrors.NullSource;

        var vertices = ImmutableArray.CreateBuilder<GEP.Vec3>(mesh.VertexCount);
        foreach (var v in mesh.Vertices)
        {
            vertices.Add(new GEP.Vec3(v.X, v.Y, v.Z));
        }

        var created = GE.ImmutableMesh.Create(
            vertices.MoveToImmutable(),
            ImmutableArray.Create(mesh.Triangles),
            GE.MeshMetadata.Named(SafeName(mesh.Metadata)));

        return created.IsSuccess ? Result.Success(created.Value) : created.Error.ToFabolus();
    }

    public static IMesh ToFabolus(this GE.IMesh mesh, MeshMetadata metadata)
    {
        var vertices = new Vector3[mesh.VertexCount];
        for (int i = 0; i < vertices.Length; i++)
        {
            var v = mesh.Vertices[i];
            vertices[i] = new Vector3((float)v.X, (float)v.Y, (float)v.Z);
        }

        return new EngineMesh(vertices, mesh.Triangles.ToArray(), metadata);
    }

    public static Error ToFabolus(this GeometryEngine.Core.Common.Error error) => new(error.Code, error.Description);

    public static GEP.Vec3 ToEngine(this Vector3 v) => new(v.X, v.Y, v.Z);

    public static Vector3 ToFabolus(this GEP.Vec3 v) => new((float)v.X, (float)v.Y, (float)v.Z);

    public static GEP.Vec2 ToEngine(this Vector2 v) => new(v.X, v.Y);

    public static Vector2 ToFabolus(this GEP.Vec2 v) => new((float)v.X, (float)v.Y);

    public static GE.PlanarPolygon ToEngine(this Polygon2D polygon) => new(
        [.. polygon.OuterBoundary.Select(ToEngine)],
        [.. polygon.Holes.Select(hole => hole.Select(ToEngine).ToImmutableArray())]);

    public static Polygon2D ToFabolus(this GE.PlanarPolygon polygon) => new()
    {
        OuterBoundary = polygon.Outer.Select(ToFabolus).ToList(),
        Holes = polygon.Holes.Select(hole => (IReadOnlyList<Vector2>)hole.Select(ToFabolus).ToList()).ToList(),
    };

    public static MeshMetadata NewMetadata(string name, string createdBy) =>
        new MeshMetadata().WithProperties(m =>
            m.Set(CoreKeys.Id, Guid.NewGuid())
             .Set(CoreKeys.Name, name)
             .Set(CoreKeys.CreatedBy, createdBy));

    /// <summary>A mesh's name if it has one. Metadata built by hand in tests may not.</summary>
    private static string SafeName(MeshMetadata? metadata) =>
        metadata?.GetProperty(CoreKeys.Name).GetValueOrDefault("mesh") ?? "mesh";
}
