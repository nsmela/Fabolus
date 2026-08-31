using System.Numerics;

namespace Fabolus.Core.Geometry;

/// <summary>
/// The distribution of a <see cref="WallThickness"/> measurement, over the faces that could be
/// measured. Faces whose probe never came out the far side are excluded from every figure here -
/// they contribute to <see cref="UnmeasuredFraction"/> instead - because a face looking lengthwise
/// down a shell is not reporting a thickness at all, and letting those into the statistics drags
/// every one of them upward.
/// </summary>
public sealed record WallThicknessStatistics
{
    /// <summary>
    /// The shell's wall thickness. The median rather than the mean: even after excluding the faces
    /// that never exited, the ones near a rim read long, and there is no upper bound on how long.
    /// </summary>
    public required float Median { get; init; }

    public required float Mean { get; init; }
    public required float Minimum { get; init; }
    public required float Maximum { get; init; }

    /// <summary>
    /// Spread about the <see cref="Mean"/>. Small next to the median means a shell of even
    /// thickness; large means the two surfaces are not parallel, which is worth knowing before
    /// trusting anything that assumes a constant offset.
    /// </summary>
    public required float StandardDeviation { get; init; }

    /// <summary>
    /// Fifth and ninety-fifth percentiles - the working range of the wall, ignoring the tails. Useful
    /// for choosing a band around the median without having to sort the per-face values again.
    /// </summary>
    public required float FifthPercentile { get; init; }
    public required float NinetyFifthPercentile { get; init; }

    /// <summary>How many faces returned a thickness, and how many there were in total.</summary>
    public required int MeasuredFaces { get; init; }
    public required int TotalFaces { get; init; }

    /// <summary>
    /// Share of faces whose probe never exited, in [0, 1]. On a shell this is roughly the rim: the
    /// faces of the wall swept between the two surfaces, whose normals run along the shell rather
    /// than across it.
    /// </summary>
    public float UnmeasuredFraction =>
        TotalFaces > 0 ? 1f - ((float)MeasuredFaces / TotalFaces) : 0f;

    public static WallThicknessStatistics Empty { get; } = new()
    {
        Median = 0f,
        Mean = 0f,
        Minimum = 0f,
        Maximum = 0f,
        StandardDeviation = 0f,
        FifthPercentile = 0f,
        NinetyFifthPercentile = 0f,
        MeasuredFaces = 0,
        TotalFaces = 0,
    };
}

/// <summary>
/// How thick a solid is, measured face by face, with the distribution of that measurement alongside.
///
/// <para>
/// The bodies a mould is built around are a surface given thickness: an anatomy surface, an offset
/// copy of it, and the wall swept between their boundaries. The thickness of that shell is the one
/// dimension that describes the whole piece, and until now nothing recorded it - it was known only
/// as "5 to 10mm, whatever the operator chose".
/// </para>
///
/// <para>
/// The measurement also says which part of the shell a face belongs to, which is what makes the
/// per-face array worth keeping rather than just the summary. A face on either surface looks
/// straight across the wall and reads one thickness; a face on the rim looks along the shell and
/// reads far more, or nothing at all. That is a statement about the shape, so unlike any measure of
/// curvature it reads the same however coarsely the mesh is tessellated.
/// </para>
/// </summary>
public sealed record WallThickness
{
    /// <summary>
    /// Distance through the solid along each face's own inward normal, indexed by triangle - the
    /// same order as <see cref="IMesh.Triangles"/> in groups of three.
    /// <see cref="float.PositiveInfinity"/> where the probe never came out the far side within
    /// <see cref="WallThicknessOptions.MaxThicknessMm"/>.
    /// </summary>
    public required IReadOnlyList<float> PerFace { get; init; }

    /// <summary>
    /// The same measurement carried to the vertices, indexed to match <see cref="IMesh.Vertices"/>,
    /// as the area-weighted mean of the measured faces around each one. Area-weighted so a fan of
    /// slivers cannot outvote the one broad face beside it.
    /// <see cref="float.PositiveInfinity"/> where no face around the vertex could be measured.
    ///
    /// <para>
    /// Indexed against the mesh exactly as given: display geometry arrives un-welded, so a corner
    /// shared by several faces appears several times and each copy carries only its own face.
    /// </para>
    /// </summary>
    public required IReadOnlyList<float> PerVertex { get; init; }

    /// <summary>
    /// The face each probe came out through, indexed by triangle; -1 where nothing was measured.
    ///
    /// <para>
    /// This is the offset correspondence made explicit. On a surface given thickness, a face and the
    /// face its probe exits through are the same place on opposite sides of the shell - that is what
    /// "offset" means. Knowing the pairing is worth more than the distance alone: the distance says
    /// how thick the shell is there, the pairing says which two patches of surface are the two sides
    /// of it, and that holds across a gap in either patch.
    /// </para>
    /// </summary>
    public required IReadOnlyList<int> PartnerFace { get; init; }

    public required WallThicknessStatistics Statistics { get; init; }

    /// <summary>
    /// The settings that produced this, so the result carries its own caveats - what counts as
    /// unmeasured depends entirely on how far the search was allowed to look.
    /// </summary>
    public required WallThicknessOptions Options { get; init; }

    /// <summary>Shorthand for <see cref="WallThicknessStatistics.Median"/>, the wall thickness.</summary>
    public float Median => Statistics.Median;
}

/// <summary>Search settings for <see cref="IGeometryEvaluators.MeasureWallThickness"/>.</summary>
public sealed record WallThicknessOptions
{
    /// <summary>
    /// How far to look through the solid before giving up, in mm. Faces that reach this are reported
    /// as unmeasured rather than as very thick, because past this depth the probe is no longer
    /// crossing a wall - it is running the length of the body.
    /// </summary>
    public float MaxThicknessMm { get; init; } = 25f;

    /// <summary>
    /// How precisely to locate the far surface, in mm. The search brackets the crossing coarsely and
    /// then bisects, so halving this costs one extra probe per face rather than doubling the work.
    /// </summary>
    public float ToleranceMm { get; init; } = 0.1f;

    /// <summary>
    /// Probes taken on the first sweep. The bracket has to be found before it can be bisected, so
    /// this sets the coarsest wall the search can still see: a wall thinner than
    /// <see cref="MaxThicknessMm"/> over this many steps is stepped straight over.
    /// </summary>
    public int CoarseSteps { get; init; } = 24;

    public static WallThicknessOptions Default { get; } = new();
}
