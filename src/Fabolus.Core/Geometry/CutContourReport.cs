namespace Fabolus.Core.Geometry;

/// <summary>
/// What the curves where a cutter crosses a mould look like, which is the actual precondition for
/// cutting it.
///
/// <para>
/// A boolean needs the two meshes' intersection contours to be closed, orientable and free of
/// self-intersections - not, as its error message suggests, the meshes themselves. The usual way to
/// break that is a cutter that only <em>partly</em> crosses the mould: the contour runs off the edge
/// of the cutter and never closes. MeshLib reports that as "Bad contour on N mesh A faces, probably
/// mesh B has self-intersections", and the "probably" is doing a lot of work - the message names a
/// cause that is often not the one present.
/// </para>
///
/// <para>
/// Measuring the contours directly tells the two apart, which matters because they call for opposite
/// fixes: an unclosed contour means the flange has to reach further, while a genuinely
/// self-intersecting cutter means the flange has to be shaped better.
/// </para>
/// </summary>
/// <param name="ContourCount">How many separate curves the cutter traces on the mould.</param>
/// <param name="ClosedCount">How many of them come back to where they started.</param>
public readonly record struct CutContourReport(int ContourCount, int ClosedCount)
{
    /// <summary>True when every contour closes, i.e. the cutter crosses the mould all the way round.</summary>
    public bool CanCut => ContourCount > 0 && ClosedCount == ContourCount;

    /// <summary>Contours that run off the cutter instead of closing.</summary>
    public int OpenCount => ContourCount - ClosedCount;

    public string Describe() => ContourCount == 0
        ? "the cutter does not reach the mould at all"
        : $"{ClosedCount} of {ContourCount} contour(s) closed";
}
