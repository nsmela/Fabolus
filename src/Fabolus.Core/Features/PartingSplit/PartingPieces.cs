using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.PartingSplit;

/// <summary>
/// Which of a parting cut's disconnected components are pieces of the mould, and which are specks.
///
/// <para>
/// A speck is a sliver of mould trapped between the cutter and the body. Where the parting line runs
/// along a face that is square to the cut's axis - the top of an extruded shell, between its two
/// skins - the flange lies in that face's plane from the line out to the face's far edge. A cutter a
/// tenth of a millimetre thick lying along a faceted face dips just under it and rises just over it,
/// and wherever the face bulges between two dips the mould above it is shut off. On scalp_bolus the
/// view's recipe left seven, 0.001-0.04 mm thick, beside two halves 13 and 24 mm thick. Counted as
/// pieces they let a cut that never severed the mould pass for a split, and a separated export wrote
/// each to a file of its own (issue #88).
/// </para>
///
/// <para>
/// Told apart by thickness rather than size: a speck is a skin, however far it spreads, and a piece
/// of mould is as thick as the mould's wall, however little of it there is. Across every body
/// measured the specks were at most 0.17 mm thick and the halves at least 4.3 mm.
/// </para>
/// </summary>
internal static class PartingPieces
{
    /// <summary>
    /// The mean thickness below which a component is a speck. Three times the thickest speck
    /// measured, and an eighth of the thinnest half; a cube of mould has to be 1.5 mm across to clear
    /// it.
    /// </summary>
    public const double LeastThicknessMm = 0.5;

    /// <summary>Whether a cut component is a sliver rather than a piece of the mould.</summary>
    public static bool IsSpeck(IMesh component) => MeanThickness(component) < LeastThicknessMm;

    /// <summary>
    /// The components that are pieces of the mould, biggest first. Ordered by volume so the same
    /// recipe names the same piece first on every run, whatever order the component walk found them in.
    /// </summary>
    public static IReadOnlyList<IMesh> Substantial(IEnumerable<IMesh> components) =>
        components
            .Where(c => !IsSpeck(c))
            .OrderByDescending(Volume)
            .ToList();

    /// <summary>
    /// Twice the volume over the surface area: a slab's thickness, since a slab of thickness h and
    /// area a encloses ha and is bounded by 2a.
    /// </summary>
    public static double MeanThickness(IMesh mesh)
    {
        double area = Area(mesh);
        return area > 0.0 ? 2.0 * Volume(mesh) / area : 0.0;
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

    private static double Area(IMesh mesh)
    {
        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;

        double area = 0;
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            var a = vertices[triangles[i]];
            area += (vertices[triangles[i + 1]] - a).Cross(vertices[triangles[i + 2]] - a).Length / 2.0;
        }

        return area;
    }
}
