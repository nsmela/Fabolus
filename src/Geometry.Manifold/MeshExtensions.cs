using System.Numerics;
using Fabolus.Core.Geometry;

namespace GeometryManifold;

/// <summary>
/// Vertex housekeeping shared across the engine: welding coincident vertices and dropping the
/// ones nothing references.
/// </summary>
/// <remarks>
/// The native side does its own welding - Manifold's merge is what the boolean kernel leans on -
/// but the managed operations need the same thing without going near a native handle. Topology
/// validation, statistics and import all measure a mesh that has to be indexed first: an STL has
/// no index buffer at all, so straight off disk every edge looks like a boundary and every mesh
/// would read as open.
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
