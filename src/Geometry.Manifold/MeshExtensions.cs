using System.Numerics;
using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using MNManifold = ManifoldNET.Manifold;
using MNMeshGL = ManifoldNET.MeshGL;
using MNManifoldError = ManifoldNET.ManifoldError;

namespace GeometryManifold;

/// <summary>
/// Conversion between the engine's plain <see cref="IMesh"/> data and Manifold's native
/// <see cref="MNManifold"/> handles.
/// </summary>
/// <remarks>
/// Manifold refuses anything that is not a closed two-manifold, and it decides that purely from
/// shared vertex indices - two triangles only meet along an edge if they name the same vertex
/// index. STL has no index buffer at all and its importer emits three fresh vertices per triangle,
/// so every mesh is welded on the way in; without that even a perfect cube comes back
/// <see cref="MNManifoldError.NotManifold"/>.
/// </remarks>
internal static class MeshExtensions
{
    /// <summary>
    /// Vertices closer together than this (in mm, the unit the whole app works in) are the same
    /// vertex. Small enough to keep genuinely distinct geometry apart at print resolution, large
    /// enough to close the float rounding an STL round-trip introduces.
    /// </summary>
    private const float WeldTolerance = 1e-5f;

    /// <summary>
    /// Converts to a native Manifold. Fails rather than returning an empty manifold when the input
    /// is not a solid, so callers report why instead of silently losing the geometry.
    /// </summary>
    public static Result<MNManifold> ToManifold(this IMesh mesh)
    {
        if (mesh is null) return GeometryErrors.NullMesh;

        var (vertices, triangles) = Weld(mesh.Vertices, mesh.Triangles);
        if (triangles.Length == 0) return GeometryErrors.InvalidMesh;

        var vertProperties = new float[vertices.Length * 3];
        for (int i = 0; i < vertices.Length; i++)
        {
            vertProperties[i * 3] = vertices[i].X;
            vertProperties[i * 3 + 1] = vertices[i].Y;
            vertProperties[i * 3 + 2] = vertices[i].Z;
        }

        var triVerts = new uint[triangles.Length];
        for (int i = 0; i < triangles.Length; i++)
        {
            triVerts[i] = (uint)triangles[i];
        }

        var meshGL = new MNMeshGL(vertProperties, triVerts, 3, null);
        var manifold = MNManifold.Create(meshGL);

        var status = manifold.Status;
        if (status != MNManifoldError.NoError)
        {
            manifold.Dispose();
            meshGL.Dispose();
            return ManifoldErrors.FromStatus(status);
        }

        meshGL.Dispose();
        return Result.Success(manifold);
    }

    /// <summary>
    /// Reads a native Manifold back into plain arrays. Manifold always hands back a compacted,
    /// indexed mesh, so unlike the MeshLib path there are no invalid slots to skip.
    /// </summary>
    public static IMesh ToIMesh(this MNManifold manifold, MeshMetadata metadata)
    {
        using var meshGL = manifold.MeshGL;

        int numProp = (int)meshGL.PropertiesNumber;
        var vertProperties = meshGL.VerticesProperties;
        var triVerts = meshGL.TriangleVertices;

        int vertexCount = numProp > 0 ? vertProperties.Length / numProp : 0;
        var vertices = new Vector3[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            int offset = i * numProp;
            vertices[i] = new Vector3(vertProperties[offset], vertProperties[offset + 1], vertProperties[offset + 2]);
        }

        var triangles = new int[triVerts.Length];
        for (int i = 0; i < triVerts.Length; i++)
        {
            triangles[i] = (int)triVerts[i];
        }

        return new ManifoldMesh(vertices, triangles, metadata);
    }

    /// <summary>
    /// Merges vertices that occupy the same point and drops the triangles that collapse as a
    /// result. Snapping to a grid of <see cref="WeldTolerance"/> keeps this O(n) - a coordinate
    /// comparison would need every pair.
    /// </summary>
    public static (Vector3[] Vertices, int[] Triangles) Weld(Vector3[] vertices, int[] triangles)
    {
        if (vertices.Length == 0 || triangles.Length == 0)
            return (Array.Empty<Vector3>(), Array.Empty<int>());

        var lookup = new Dictionary<(long, long, long), int>(vertices.Length);
        var welded = new List<Vector3>(vertices.Length);
        var remap = new int[vertices.Length];

        for (int i = 0; i < vertices.Length; i++)
        {
            var v = vertices[i];
            var key = (
                (long)MathF.Round(v.X / WeldTolerance),
                (long)MathF.Round(v.Y / WeldTolerance),
                (long)MathF.Round(v.Z / WeldTolerance));

            if (lookup.TryGetValue(key, out int existing))
            {
                remap[i] = existing;
                continue;
            }

            remap[i] = welded.Count;
            lookup[key] = welded.Count;
            welded.Add(v);
        }

        var keptTriangles = new List<int>(triangles.Length);
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            int a = remap[triangles[i]];
            int b = remap[triangles[i + 1]];
            int c = remap[triangles[i + 2]];

            // A triangle whose corners welded together has no area left to contribute, and
            // Manifold rejects the whole mesh over a single one.
            if (a == b || b == c || c == a) continue;

            keptTriangles.Add(a);
            keptTriangles.Add(b);
            keptTriangles.Add(c);
        }

        return (welded.ToArray(), keptTriangles.ToArray());
    }

    /// <summary>
    /// Drops vertices no triangle references and renumbers the rest, mirroring MeshLib's pack().
    /// </summary>
    public static (Vector3[] Vertices, int[] Triangles) Compact(Vector3[] vertices, int[] triangles)
    {
        var remap = new int[vertices.Length];
        Array.Fill(remap, -1);

        var kept = new List<Vector3>(vertices.Length);
        var newTriangles = new int[triangles.Length];

        for (int i = 0; i < triangles.Length; i++)
        {
            int index = triangles[i];
            if (remap[index] < 0)
            {
                remap[index] = kept.Count;
                kept.Add(vertices[index]);
            }
            newTriangles[i] = remap[index];
        }

        return (kept.ToArray(), newTriangles);
    }
}

/// <summary>
/// Maps Manifold's construction failures onto the engine's error vocabulary.
/// </summary>
internal static class ManifoldErrors
{
    public static Error FromStatus(MNManifoldError status) => status switch
    {
        MNManifoldError.NotManifold => new Error(
            "Manifold.NotManifold",
            "The mesh is not a closed solid: it has boundary or non-manifold edges."),
        MNManifoldError.NonFiniteVertex => new Error(
            "Manifold.NonFiniteVertex",
            "The mesh contains a vertex with a non-finite coordinate."),
        MNManifoldError.VertexIndexOutOfBounds => new Error(
            "Manifold.VertexIndexOutOfBounds",
            "A triangle references a vertex that does not exist."),
        _ => new Error("Manifold.InvalidMesh", $"Manifold rejected the mesh: {status}."),
    };

    public static readonly Error EmptyResult =
        new("Manifold.EmptyResult", "The operation produced an empty manifold.");
}
