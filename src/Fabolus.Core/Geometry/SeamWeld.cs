namespace Fabolus.Core.Geometry;

/// <summary>
/// Which of a mesh's vertices are the same point, for code that needs edge adjacency.
///
/// <para>
/// Only vertices on a seam - an edge only one triangle uses - are matched by position. A triangle
/// soup is all seams, so it welds whole, as the MeshLib-era display geometry needed. Where the
/// triangles already close up by index there is nothing to weld, and matching by position anyway
/// can only fuse sheets the mesh holds apart.
/// </para>
///
/// <para>
/// GeometryEngine's offsets produce exactly that: where a smoothed body touches itself, the
/// level-set mesher emits the point once per sheet at the same position. Welding those by position
/// made two to sixteen edges with three or four faces on the saved chin and larynx moulds, and the
/// parting trace refused the body. Moving the copies apart instead was tried and makes crossings:
/// the sheets meet at an angle often enough that any step, in any direction, passes one through
/// the other.
/// </para>
/// </summary>
internal static class SeamWeld
{
    /// <summary>
    /// The welded index of every vertex, numbered from zero in first-seen order, with
    /// <paramref name="count"/> distinct points. Seam vertices are matched on a grid of
    /// <paramref name="gridMm"/>; every other vertex is a point of its own.
    /// </summary>
    public static int[] Weld(IMesh mesh, double gridMm, out int count)
    {
        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;

        var edgeUse = new Dictionary<(int, int), int>(triangles.Length);
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                int a = triangles[i + k];
                int b = triangles[i + ((k + 1) % 3)];
                var key = a < b ? (a, b) : (b, a);
                edgeUse[key] = edgeUse.GetValueOrDefault(key) + 1;
            }
        }

        var onSeam = new bool[vertices.Length];
        foreach (var ((a, b), uses) in edgeUse)
        {
            if (uses != 1) continue;
            onSeam[a] = true;
            onSeam[b] = true;
        }

        var lookup = new Dictionary<(int, int, int), int>();
        var welded = new int[vertices.Length];
        count = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            if (!onSeam[i])
            {
                welded[i] = count++;
                continue;
            }

            var v = vertices[i];
            var key = (
                (int)Math.Round(v.X / gridMm),
                (int)Math.Round(v.Y / gridMm),
                (int)Math.Round(v.Z / gridMm));

            if (!lookup.TryGetValue(key, out int id))
            {
                id = count++;
                lookup[key] = id;
            }
            welded[i] = id;
        }

        return welded;
    }
}
