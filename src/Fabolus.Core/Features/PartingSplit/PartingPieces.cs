using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.PartingSplit;

/// <summary>
/// Which of a parting cut's disconnected components are pieces of the mould, and which are debris.
///
/// <para>
/// The boolean that cuts the mould leaves specks where the cutter grazes a wall: on scalp_bolus the
/// view's recipe gives two halves of 57% and 43% and seven specks of under a tenth of a cubic
/// millimetre along the rim. Counted as pieces, they let a cut that never severed the mould - one
/// piece holding all of it, as flat_vulva tilted 30-60 degrees about X was cut - pass for a split,
/// and a separated export wrote each of them to a file of its own (issue #88).
/// </para>
/// </summary>
internal static class PartingPieces
{
    /// <summary>
    /// The share of the whole a component has to hold to be a piece. A half of a mould is tens of
    /// percent; the specks are under a millionth. A hundredth sits far from both.
    /// </summary>
    public const double LeastShare = 0.01;

    /// <summary>
    /// The components holding at least <see cref="LeastShare"/> of their total volume, biggest
    /// first. Ordered by volume so the same recipe names the same piece first on every run, whatever
    /// order the component walk found them in.
    /// </summary>
    public static IReadOnlyList<IMesh> Substantial(IEnumerable<IMesh> components)
    {
        var sized = components
            .Select(c => (Mesh: c, Volume: Volume(c)))
            .OrderByDescending(c => c.Volume)
            .ToList();

        double total = sized.Sum(c => c.Volume);
        if (total <= 0.0) return sized.Select(c => c.Mesh).ToList();

        return sized
            .Where(c => c.Volume >= total * LeastShare)
            .Select(c => c.Mesh)
            .ToList();
    }

    /// <summary>Enclosed volume by the divergence theorem, for ranking the cut pieces by size.</summary>
    public static double Volume(IMesh mesh)
    {
        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;

        double total = 0;
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            total += vertices[triangles[i]].Dot(vertices[triangles[i + 1]].Cross(vertices[triangles[i + 2]])) / 6.0;
        }

        return Math.Abs(total);
    }
}
