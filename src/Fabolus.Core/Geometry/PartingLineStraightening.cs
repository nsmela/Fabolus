using System.Numerics;

namespace Fabolus.Core.Geometry;

/// <summary>Settings for <see cref="PartingLineStraightening"/>.</summary>
public sealed record PartingLineStraighteningOptions
{
    /// <summary>
    /// How close to a crease the line may come, as a fraction of the way across the wall. The
    /// smoothing is free to put the line anywhere between this and its mirror.
    ///
    /// <para>
    /// Not as loose as "stay on the wall" would allow, because staying on the wall turns out not to be
    /// the real constraint. At 0.15 the line straightens beautifully and drifts to two thirds of the
    /// way across on the coarser bodies - still inside the wall, no point past a crease - and the
    /// flange rim swept from it then fails to seal against the body on three of them. The seal is what
    /// actually bounds how far off centre the line may sit, so the margin is set to keep it in the
    /// middle third rather than merely inside the creases.
    /// </para>
    /// </summary>
    public float Margin { get; init; } = 0.35f;

    /// <summary>Rounds of smoothing: a Laplacian step, a push back off the creases, then a projection.</summary>
    public int Passes { get; init; } = 60;

    /// <summary>How far towards the average of its neighbours a point moves per round.</summary>
    public float Strength { get; init; } = 0.5f;

    public static PartingLineStraighteningOptions Default { get; } = new();
}

/// <summary>
/// Makes the parting line as straight as the rim wall will allow, and stops asking it to be central.
///
/// <para>
/// A change of objective, and a better-aimed one than everything before it. A parting line is not for
/// bisecting a wall - it is for sweeping a flange along, and a flange cares how sharply the line turns,
/// not whether it sits at 0.50 or 0.62 of the way across. Dropping the requirement to be central frees
/// the smoothing to take out the wander that being central was forcing the line to follow, and leaves
/// only one thing to defend: that it stays on the wall.
/// </para>
///
/// <para>
/// Measured across the sample set this lowers the 95th percentile turn and raises the clearance the
/// line keeps from itself - the two quantities a sweep actually fails on - on every body, and no body
/// ends with a single point past a crease. <c>larynx-large</c> had 4.4% of its line outside the wall
/// before and has none after.
/// </para>
///
/// <para>
/// Not wired into the pipeline, and the reason is the interesting part. Applied to every body it costs
/// three downstream tests: the flange rim stops sealing against the body on scalp_bolus, the normals
/// along the line stop turning smoothly on chin_bolus, and the thin cutter fails to break the mould.
/// Tightening the margin from 0.15 to 0.35 - keeping the line inside the middle third rather than
/// merely inside the creases - recovers two of them and not the third. So the premise this was built
/// on is wrong: being central is not a preference that can be traded away for smoothness. The flange
/// rim is offset from the line and has to seat inside the body, and it stops sealing well before the
/// line gets anywhere near a crease. Kept because the measurements are worth having and because a
/// caller that wants a smoother line and can live with the seal may still ask for it.
/// </para>
/// </summary>
public static class PartingLineStraightening
{
    /// <param name="bands">
    /// The walls the line must stay inside, one per wall rim. With none supplied the line comes back
    /// untouched - there would be nothing holding it, and smoothing an unconstrained loop walks it off
    /// the rim entirely.
    /// </param>
    public static PartingLine Straighten(
        PartingLine line, IReadOnlyList<PartingBand> bands,
        PartingLineStraighteningOptions? options = null, ISurfaceProjector? projector = null)
    {
        if (line is null || bands is null || bands.Count == 0) return line!;

        options ??= PartingLineStraighteningOptions.Default;

        var usable = bands.Where(b => b.Span > 1e-4f).ToList();
        if (usable.Count == 0) return line;

        var straightened = new List<Vector3[]>(line.Loops.Count);
        foreach (var loop in line.Loops)
            straightened.Add(StraightenLoop(loop, Nearest(loop, usable), options, projector));

        return new PartingLine(straightened);
    }

    /// <summary>The wall a loop runs along, as the one its points sit closest to overall.</summary>
    private static PartingBand Nearest(IReadOnlyList<Vector3> loop, IReadOnlyList<PartingBand> bands)
    {
        var best = bands[0];
        float bestDistance = float.MaxValue;

        foreach (var band in bands)
        {
            float total = 0f;
            foreach (var point in loop)
                total += MathF.Min(
                    PartingBand.Closest(point, band.First).Distance,
                    PartingBand.Closest(point, band.Second).Distance);

            if (total >= bestDistance) continue;
            bestDistance = total;
            best = band;
        }

        return best;
    }

    private static Vector3[] StraightenLoop(
        IReadOnlyList<Vector3> loop, PartingBand band,
        PartingLineStraighteningOptions options, ISurfaceProjector? projector)
    {
        int count = loop.Count;
        var points = loop.ToArray();
        if (count < 8) return points;

        var scratch = new Vector3[count];

        for (int pass = 0; pass < options.Passes; pass++)
        {
            // Plain Laplacian, not Taubin. Taubin alternates a shrinking pass with an inflating one so
            // that a loop keeps its size, which is right where the loop's position is the answer and
            // wrong here: the shrinkage is the straightening. What stops it collapsing is the wall.
            for (int i = 0; i < count; i++)
            {
                var midpoint = (points[(i - 1 + count) % count] + points[(i + 1) % count]) * 0.5f;
                scratch[i] = points[i] + ((midpoint - points[i]) * options.Strength);
            }

            // The wall pushes back, rather than the line being clipped to it. A clamp is a step
            // function - nothing at all until the point crosses the margin, then a bodily move - so it
            // puts a corner exactly where it engages: with one, nose went from a worst turn of 36
            // degrees to 63 and chin_bolus from 65 to 73, while every stretch away from a crease
            // improved. A force that grows from zero as the margin is approached leaves the smoothing
            // and the constraint to resolve against each other instead of taking turns.
            for (int i = 0; i < count; i++)
            {
                var a = PartingBand.Closest(scratch[i], band.First).Point;
                var b = PartingBand.Closest(scratch[i], band.Second).Point;

                var across = b - a;
                float span = across.LengthSquared();
                if (span < 1e-9f) continue;

                float t = Vector3.Dot(scratch[i] - a, across) / span;
                float over =
                    t < options.Margin ? options.Margin - t
                    : t > 1f - options.Margin ? t - (1f - options.Margin)
                    : 0f;

                if (over <= 0f) continue;

                float target = t < options.Margin ? options.Margin : 1f - options.Margin;
                scratch[i] += across * ((target - t) * MathF.Min(over / options.Margin, 1f));
            }

            Array.Copy(scratch, points, count);

            if (projector is not null)
                for (int i = 0; i < count; i++) points[i] = projector.Project(points[i]);
        }

        return points;
    }
}
