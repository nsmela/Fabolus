using System.Numerics;

namespace Fabolus.Core.Geometry;

/// <summary>
/// The band of surface that is draft-neutral for a given pull direction: everything whose normal sits
/// within a tolerance of perpendicular to that direction, i.e. a near-vertical wall that faces neither
/// half of the mould. The parting view already shades this band on the model from its range slider;
/// this is the same band expressed so the geometry can use it.
///
/// <para>
/// Measured as the dot product of the surface normal with the pull direction, which is the sine of the
/// angle above (positive) or below (negative) perpendicular. So a +/-5 degree band is
/// [-0.087, +0.087]: above <see cref="Upper"/> the surface belongs to the far half, below
/// <see cref="Lower"/> to the near half, and in between it could go either way.
/// </para>
///
/// <para>
/// Two things use it. The isoline is traced at the band's <see cref="Midpoint"/> rather than at
/// exactly zero, so a band the user has deliberately made asymmetric biases the parting line toward
/// the side they favoured. And the surface-constrained smoother is confined to the band: it may slide
/// a point anywhere across the neutral wall, because that cannot create an undercut, but it may not
/// slide it out onto drafted surface, which can. Without that, smoothing is free to walk the line off
/// the silhouette entirely - the loop stays on the mesh but stops being a valid parting line.
/// </para>
/// </summary>
public readonly record struct PartingNeutralBand
{
    /// <summary>Default half-width of the band, in degrees, matching the parting view's slider default.</summary>
    public const float DefaultHalfAngleDegrees = 5f;

    /// <summary>Lower edge, as normal-dot-pull. Negative tilts below perpendicular.</summary>
    public float Lower { get; init; }

    /// <summary>Upper edge, as normal-dot-pull.</summary>
    public float Upper { get; init; }

    public PartingNeutralBand(float lower, float upper)
    {
        // Tolerate the edges arriving the wrong way round rather than silently producing an empty band.
        Lower = MathF.Min(lower, upper);
        Upper = MathF.Max(lower, upper);
    }

    /// <summary>Builds a band from angles either side of perpendicular, in degrees.</summary>
    public static PartingNeutralBand FromDegrees(float lowerDegrees, float upperDegrees) =>
        new(MathF.Sin(lowerDegrees * MathF.PI / 180f), MathF.Sin(upperDegrees * MathF.PI / 180f));

    public static PartingNeutralBand Default { get; } =
        FromDegrees(-DefaultHalfAngleDegrees, DefaultHalfAngleDegrees);

    /// <summary>A band of zero width at exactly perpendicular - the plain silhouette, no tolerance.</summary>
    public static PartingNeutralBand None { get; } = new(0f, 0f);

    /// <summary>The isovalue the parting line is traced at.</summary>
    public float Midpoint => (Lower + Upper) * 0.5f;

    public float Width => Upper - Lower;

    /// <summary>True when <paramref name="normalDotPull"/> falls inside the band.</summary>
    public bool Contains(float normalDotPull) => normalDotPull >= Lower && normalDotPull <= Upper;

    /// <summary>
    /// True when a surface with normal <paramref name="normal"/> is draft-neutral for
    /// <paramref name="pullDirection"/>. Both are expected normalized.
    /// </summary>
    public bool Contains(Vector3 normal, Vector3 pullDirection) =>
        Contains(Vector3.Dot(normal, pullDirection));
}
