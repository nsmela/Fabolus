using System.Numerics;

namespace Fabolus.Core.Geometry;

/// <summary>
/// The two creases bounding one rim wall, which between them say where the middle of that wall is.
///
/// <para>
/// Deliberately a pair rather than a rim. Deciding which contours belong to the same rim, and whether
/// that rim is a wall at all, is <c>PartingStrategy</c>'s job and must happen in exactly one place -
/// so this takes the answer rather than working it out again, and a caller that has not asked the
/// question cannot accidentally centre a line inside a knife edge.
/// </para>
/// </summary>
public sealed record PartingBand(RidgeContour First, RidgeContour Second)
{
    private float? _span;

    /// <summary>
    /// How wide the band runs, as the median distance from one crease to the other. The median rather
    /// than the mean because a rim that pinches somewhere along its length has a handful of samples
    /// near zero, and those move a mean enough to loosen every comparison made against it.
    /// </summary>
    public float Span => _span ??= MedianSpan();

    private float MedianSpan()
    {
        var points = First.Points;
        if (points.Count == 0 || Second.Points.Count == 0) return 0f;

        var spans = new float[points.Count];
        for (int i = 0; i < points.Count; i++) spans[i] = Closest(points[i], Second).Distance;

        Array.Sort(spans);
        return spans[spans.Length / 2];
    }

    internal static (Vector3 Point, float Distance) Closest(Vector3 from, RidgeContour contour)
    {
        var points = contour.Points;
        int spans = contour.IsClosed ? points.Count : points.Count - 1;

        var best = from;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < spans; i++)
        {
            var a = points[i];
            var ab = points[(i + 1) % points.Count] - a;

            float lengthSquared = ab.LengthSquared();
            float t = lengthSquared < 1e-12f
                ? 0f
                : Math.Clamp(Vector3.Dot(from - a, ab) / lengthSquared, 0f, 1f);

            var on = a + (ab * t);
            float distance = Vector3.Distance(from, on);
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = on;
        }

        return (best, bestDistance);
    }
}

/// <summary>Settings for <see cref="PartingLineCentring"/>.</summary>
public sealed record PartingLineCentringOptions
{
    /// <summary>
    /// How far off centre a point may sit, as a fraction of the way across the band, before it is
    /// moved at all. A point 0.5 of the way across is exactly centred, so 0.10 leaves everything
    /// between 0.40 and 0.60 alone.
    ///
    /// <para>
    /// The idle band is the point rather than an inefficiency. The traced seam is already near the
    /// middle on most of the sample set - one body sits inside 0.41 to 0.59 for its whole length -
    /// and a pass that moved every point would be rewriting a correct line to match two contours that
    /// carry their own sampling noise. What wants fixing is the excursions, and this is what
    /// distinguishes them.
    /// </para>
    /// </summary>
    public float DeadZone { get; init; } = 0.02f;

    /// <summary>
    /// How much further past <see cref="DeadZone"/> a point must sit before it is moved at full
    /// strength, with everything between eased in.
    ///
    /// <para>
    /// Without the ramp the correction is a step function, and a step function is what breaks the
    /// loop's spacing. A point just outside the idle band moves the whole way while its neighbour just
    /// inside does not move at all, and the gap between two adjacent samples opens to the width of the
    /// correction - measured at 3.4 times the median spacing on <c>standard</c>. The trace resamples to
    /// an even arc length precisely because the flange sweep does not terminate on a loop like that, so
    /// re-introducing the unevenness here would undo it for the sake of a threshold being crisp.
    /// </para>
    /// </summary>
    public float Ramp { get; init; } = 0.06f;

    /// <summary>
    /// The narrowest and widest a sample's span may read, as a multiple of the band's own median,
    /// before it is left where it is.
    ///
    /// <para>
    /// The floor is what keeps this away from a taper. Where a rim thins towards a knife edge its two
    /// creases converge, and the measured span on <c>larynx-large</c> falls to a third of the rim's
    /// median through those stretches. There is no middle of a band that has no width - the ratio
    /// that says where the middle is divides by that span - so a pinch is a place to leave the line
    /// alone rather than a place to move it confidently to a point that means nothing.
    /// </para>
    ///
    /// <para>
    /// The ceiling catches the other end: a span far wider than the band cannot be a reading across
    /// it. The two match the thresholds the band-width report already judges outliers by, so this
    /// introduces no new number to argue over.
    /// </para>
    /// </summary>
    public float NarrowestSpan { get; init; } = 0.6f;

    /// <inheritdoc cref="NarrowestSpan"/>
    public float WidestSpan { get; init; } = 1.6f;

    /// <summary>
    /// How much of the way to the middle a point moves per pass. Short of the whole way, and repeated,
    /// because each move is followed by a projection back onto the body: a point allowed to jump the
    /// full distance in one go can cross the wall and come back projected onto the far sheet, where
    /// one edged across in steps stays on the surface it started on. This is the same reasoning that
    /// puts the projection inside <see cref="ThicknessParting"/>'s relaxation loop rather than after it.
    /// </summary>
    public float Strength { get; init; } = 0.5f;

    /// <summary>
    /// How many move-smooth-project rounds to run, as a ceiling - the loop stops early once nothing is
    /// left off centre.
    ///
    /// <para>
    /// Not to be raised casually to chase the last of the correction. Each round smooths and reprojects
    /// the stretches it touched, and past this the accumulated tidying starts to matter more than the
    /// centring does: at 24 the bias statistics barely improve while the solid swept along the line
    /// picks up self-intersections on two more bodies. Eight is where the correction has arrived and
    /// the line is still the shape the trace made it.
    /// </para>
    /// </summary>
    public int Passes { get; init; } = 8;

    /// <summary>
    /// Taubin passes applied to the loop after each move. Gentle on purpose: the midpoints being
    /// aimed at come from two evenly resampled contours and are already smooth, so this is here to
    /// take the edge off the joins where a run of moved points meets a run of untouched ones, not to
    /// reshape the line.
    /// </summary>
    public int SmoothingPasses { get; init; } = 1;

    /// <summary>
    /// How many times the per-point correction is averaged along the loop before any of it is applied.
    ///
    /// <para>
    /// This is what keeps the idle band from putting a step in the line. Every rule that declines to
    /// move a point - the band being too close to centre there, the span reading like a pinch - makes
    /// the correction a function that switches on and off along the loop, and applying it as it stands
    /// leaves a point moved 5 mm sitting next to one not moved at all. On <c>standard</c> that showed
    /// as a rectangular jog with a 160 degree hairpin at each end of it, which is a worse line than the
    /// uncentred one it replaced however well centred its points are.
    /// </para>
    ///
    /// <para>
    /// Smoothing the correction rather than the line is what makes this safe. A blur cannot invent a
    /// displacement out of nothing, so a stretch where no point qualified still receives none and stays
    /// exactly as traced; only the joins are ramped, over about this many samples.
    /// </para>
    /// </summary>
    public int BlendPasses { get; init; } = 3;

    /// <summary>
    /// Turn angle, in degrees over one sample, at or beyond which a point is treated as a kink rather
    /// than as the line following the rim round.
    ///
    /// <para>
    /// Set against what the lines actually do: across the sample set the median turn is 3 to 12 degrees
    /// and the 95th percentile 11 to 40, so 45 sits above anything a smooth stretch produces while
    /// still catching every spike measured - 160 degrees on <c>standard</c> at its worst, 118 on
    /// <c>larynx-large</c>.
    /// </para>
    /// </summary>
    public float OutlierTurnDegrees { get; init; } = 30f;

    /// <summary>
    /// The same threshold expressed against the loop's own median turn, whichever is the larger. A line
    /// sampled finely enough never reaches the absolute figure however badly it kinks, because the turn
    /// at one sample falls as the samples get closer together.
    /// </summary>
    public float OutlierTurnRatio { get; init; } = 6f;

    /// <summary>How far towards the average of its neighbours a kinked point moves per round.</summary>
    public float OutlierStrength { get; init; } = 0.5f;

    /// <summary>
    /// How many rounds of kink smoothing to run, as a ceiling - it stops as soon as no sample is over
    /// the threshold.
    /// </summary>
    public int OutlierPasses { get; init; } = 24;


    public static PartingLineCentringOptions Default { get; } = new();
}

/// <summary>
/// Moves a traced parting line to the middle of the rim wall it runs along.
///
/// <para>
/// <see cref="ThicknessParting"/> defines the line as the boundary between the two surfaces'
/// territories, and that boundary is close to the middle of the band for the same reason a watershed
/// is: a face goes to whichever side it reaches more cheaply, so the tie falls where the two are
/// equidistant. Close to, but not on it. The territories are spread across the face graph, so the
/// frontier can only fall on an edge between two faces and takes whatever position the tessellation
/// offers; and the corridor it is spreading through is the set of faces whose measured thickness is
/// off the median, whose two edges do not sit symmetrically about the wall wherever the body curves
/// into the rim.
/// </para>
///
/// <para>
/// Measured across the sample set the error is not a drift - every rim's median bias lands between
/// 0.40 and 0.55 of the way across - but a scatter, with excursions to 0.10 and 0.86 and single
/// points 5 mm off centre in a 13 mm band. So this corrects excursions and deliberately does nothing
/// elsewhere: what it is fixing is local, and a pass that rewrote the whole line would be trading a
/// measured fault for an unmeasured one.
/// </para>
///
/// <para>
/// Nothing here recomputes the line. The loops that come out are the loops that went in, still closed,
/// still one per rim, with some of their points moved - so every guarantee the trace makes about
/// topology survives untouched, and this can be left out entirely without changing what the line is.
/// </para>
/// </summary>
public static class PartingLineCentring
{
    /// <param name="bands">
    /// The rim walls to centre within, one per wall rim. A rim that is a single ridge or whose contours
    /// could not be told apart has no band and must not be passed: there is nothing between for the
    /// line to run down, and the contour is the line already.
    /// </param>
    /// <param name="projector">
    /// Closest-point projection onto the body, applied after every pass so the line stays on the
    /// surface. Optional for the same reason <see cref="ThicknessParting.Trace"/>'s is - this is pure
    /// geometry and cannot build one - and wanted for the same reason: the midpoint of two points on
    /// a curved rim is a chord, and lies inside the body.
    /// </param>
    public static PartingLine Centre(
        PartingLine line, IReadOnlyList<PartingBand> bands,
        PartingLineCentringOptions? options = null, ISurfaceProjector? projector = null)
    {
        if (line is null) return line!;

        options ??= PartingLineCentringOptions.Default;

        var usable = bands?.Where(b => b.Span > 1e-4f).ToList() ?? new List<PartingBand>();

        var centred = new List<Vector3[]>(line.Loops.Count);
        foreach (var loop in line.Loops)
        {
            var points = usable.Count > 0
                ? CentreLoop(loop, usable, options, projector)
                : loop.ToArray();

            // Kinks are taken out whether or not there was a band to centre in. A rim that tapered to a
            // single ridge, or one whose contours could not be told apart, has no middle to aim at -
            // but a flange is swept along its line just the same, and a spike in it is as bad there as
            // anywhere. Run after the centring rather than before, because the centring can put one in:
            // it moves some points and not others, and the joins are where a kink appears.
            SmoothOutliers(points, options, projector);
            centred.Add(points);
        }

        return new PartingLine(centred);
    }

    /// <summary>
    /// Pulls back the points where the line kinks, and leaves the rest of it alone.
    ///
    /// <para>
    /// Aimed at the turn angle rather than at position, because that is what a kink is and what makes
    /// it matter: the flange leaves the line along the surface normal, and where the line doubles back
    /// on itself over one sample the sweep has to fan through the reversal in a single step. The rest
    /// of the loop is already as smooth as the trace's twenty relaxation passes left it, so a global
    /// smoothing here would mostly be re-smoothing what is smooth - and moving more of the line is the
    /// thing that has repeatedly broken the solid swept along it.
    /// </para>
    ///
    /// <para>
    /// The threshold adapts to the loop as well as being absolute. A rim that genuinely turns sharply -
    /// the fin on <c>chin</c> is a real feature of the body, not a defect - turns sharply over several
    /// consecutive samples, so its neighbours turn too and the correction they receive is nearly equal;
    /// blending the correction along the loop then slides the whole corner intact rather than clipping
    /// its tip. A lone spike has no such support and is pulled back on its own.
    /// </para>
    /// </summary>
    private static void SmoothOutliers(
        Vector3[] points, PartingLineCentringOptions options, ISurfaceProjector? projector)
    {
        int count = points.Length;
        if (count < 8 || options.OutlierPasses <= 0) return;

        var correction = new Vector3[count];
        var turn = new float[count];

        for (int i = 0; i < count; i++) turn[i] = TurnDegrees(points, i);

        // Against the loop's own median as well as an absolute floor, so a finely tessellated line that
        // never exceeds the absolute threshold still has its worst samples eased, and a coarsely
        // sampled one that turns constantly is not declared to be all outlier.
        float threshold = MathF.Max(
            options.OutlierTurnDegrees, MedianOf(turn) * options.OutlierTurnRatio);

        // Chosen once, from the line as it arrives, and then held. Re-deciding each pass sets off a
        // cascade: easing a spike changes the turn at its neighbours, that tips one of them over the
        // threshold, easing it tips the next, and the correction walks away along the loop rewriting
        // stretches that were never kinked. Measured on chin, re-deciding took the 95th percentile turn
        // from 27 degrees to 39 while fixing nothing - the samples it went on to "correct" were the
        // line following the rim.
        var kinked = new bool[count];
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            if (turn[i] < threshold) continue;
            kinked[i] = true;
            found = true;
        }

        if (!found) return;

        for (int pass = 0; pass < options.OutlierPasses; pass++)
        {
            Array.Clear(correction);

            bool any = false;
            for (int i = 0; i < count; i++)
            {
                if (!kinked[i]) continue;

                // Stop as soon as this sample is back in line with its neighbours, so a spike that
                // needed one pass does not keep being pulled for eleven more.
                if (TurnDegrees(points, i) < threshold) continue;

                var midpoint = (points[(i - 1 + count) % count] + points[(i + 1) % count]) * 0.5f;
                correction[i] = (midpoint - points[i]) * options.OutlierStrength;
                any = true;
            }

            if (!any) break;

            // Deliberately not blended, unlike the centring correction. The two are shaped differently:
            // a centring move pushes a stretch of line sideways, so stopping it abruptly leaves a step
            // and the blend is what removes it - whereas this move pulls a single sample back onto the
            // line its own neighbours already define, which lowers the turn at those neighbours too and
            // can leave no step by construction. Blending it does the opposite of what it does there:
            // it drags unkinked neighbours off a smooth line to share a correction they had no need of,
            // and every one of them becomes a smaller kink of its own. Measured, that turned chin from
            // no samples over 60 degrees into 3.4% of them, and its worst turn from 59 to 91.
            for (int i = 0; i < count; i++)
            {
                if (correction[i].LengthSquared() < 1e-12f) continue;

                points[i] += correction[i];
                if (projector is not null) points[i] = projector.Project(points[i]);
            }
        }
    }

    /// <summary>How sharply the line turns at one sample, in degrees; zero where it cannot be measured.</summary>
    private static float TurnDegrees(Vector3[] points, int index)
    {
        int count = points.Length;
        var incoming = points[index] - points[(index - 1 + count) % count];
        var outgoing = points[(index + 1) % count] - points[index];

        if (incoming.LengthSquared() < 1e-12f || outgoing.LengthSquared() < 1e-12f) return 0f;

        return MathF.Acos(Math.Clamp(
            Vector3.Dot(Vector3.Normalize(incoming), Vector3.Normalize(outgoing)), -1f, 1f))
            * 180f / MathF.PI;
    }

    private static float MedianOf(float[] values)
    {
        var sorted = (float[])values.Clone();
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }

    private static Vector3[] CentreLoop(
        IReadOnlyList<Vector3> loop, IReadOnlyList<PartingBand> bands,
        PartingLineCentringOptions options, ISurfaceProjector? projector)
    {
        int count = loop.Count;
        var points = loop.ToArray();
        if (count < 4) return points;

        // Which band each point runs along, fixed once from the traced line rather than re-asked as
        // the points move. A point near where two rims converge can be nearer the other rim by a hair,
        // and letting the answer change mid-iteration lets a point walk from one band to the other and
        // drag the loop across the gap between them.
        var owner = new int[count];
        for (int i = 0; i < count; i++) owner[i] = Nearest(points[i], bands);

        var scratch = new Vector3[count];
        var moved = new bool[count];
        var correction = new Vector3[count];

        for (int pass = 0; pass < options.Passes; pass++)
        {
            Array.Clear(moved);
            Array.Clear(correction);

            for (int i = 0; i < count; i++)
            {
                var band = bands[owner[i]];
                var (first, toFirst) = PartingBand.Closest(points[i], band.First);
                var (second, toSecond) = PartingBand.Closest(points[i], band.Second);

                float span = toFirst + toSecond;
                if (span < 1e-4f) continue;

                float ratio = span / band.Span;
                if (ratio < options.NarrowestSpan || ratio > options.WidestSpan) continue;

                float strength = Eased(MathF.Abs((toFirst / span) - 0.5f), options);
                if (strength <= 0f) continue;

                // Only the part of the move that crosses the band is kept. The midpoint of the two
                // nearest crease points also sits a little way along the rim from where the point is,
                // and following that component slides the point along the line rather than across it.
                //
                // That was worth measuring rather than assuming, because stripping it does cost
                // accuracy: where the line meets the band at an angle the crossing component is small,
                // so the correction there is weaker and takes more passes to arrive. Taking the whole
                // step instead centres those stretches in one pass and reads better on every bias
                // statistic - and makes the solid swept along the line self-intersect on scalp, which
                // is the one thing the line must never do, because a self-intersecting cutter is one
                // the mould boolean refuses. Accuracy that costs the cutter is not accuracy worth
                // having; the passes are cheap and buy it back.
                var step = ((first + second) * 0.5f) - points[i];
                var along = points[(i + 1) % count] - points[(i - 1 + count) % count];
                if (along.LengthSquared() > 1e-12f)
                {
                    along = Vector3.Normalize(along);
                    step -= along * Vector3.Dot(step, along);
                }

                correction[i] = step * options.Strength * strength;
            }

            // Nothing left off centre: stop rather than run the remaining passes over a line that is
            // already where it should be.
            if (Array.TrueForAll(correction, c => c.LengthSquared() < 1e-12f)) break;

            // The correction is blended along the loop before any of it lands, so the stretches that
            // qualified and the stretches that did not are joined by a ramp instead of a step.
            Blend(correction, scratch, options.BlendPasses);

            for (int i = 0; i < count; i++)
            {
                if (correction[i].LengthSquared() < 1e-12f) continue;

                points[i] += correction[i];
                moved[i] = true;
            }

            // Everything that follows is confined to the stretches that actually moved. A pass that
            // tidied the whole loop would rewrite the parts of the line that were already correct -
            // and it did: before this, a body needing no correction at all still came back shifted a
            // tenth of a millimetre by the smoothing alone, which was enough to make the solid swept
            // along it self-intersect on one body. Leaving correct stretches untouched is the same
            // principle as the idle band, applied to the tidying rather than to the correction.

            // Spacing evened out along the curve, without moving the curve. Even with the moves eased
            // in, a sample whose band reading differs sharply from its neighbours' is pulled further
            // than they are, and the loop the sweep is handed has to stay evenly sampled whatever the
            // correction did to get there. Resampling it would also work and is what the trace does,
            // but it interpolates along chords, which on a curved rim pulls the line inward and undoes
            // the centring a little more every pass - measured at a third of the correction lost.
            Redistribute(points, scratch, moved);
            Smooth(points, scratch, moved, options.SmoothingPasses);

            if (projector is not null)
                for (int i = 0; i < count; i++)
                    if (moved[i]) points[i] = projector.Project(points[i]);
        }

        return points;
    }

    /// <summary>
    /// Averages the correction with its neighbours along the loop, repeatedly - a binomial blur, which
    /// spreads each switch-on over roughly <paramref name="passes"/> samples and cannot overshoot.
    ///
    /// <para>
    /// Note what this does at the ends of a corrected run: the zero either side is averaged in, so the
    /// last moved point moves less than its neighbour and the first unmoved point moves a little rather
    /// than not at all. That is the ramp. Where nothing qualified for a whole stretch there is nothing
    /// to spread and the stretch keeps its traced position exactly.
    /// </para>
    /// </summary>
    private static void Blend(Vector3[] correction, Vector3[] scratch, int passes)
    {
        int count = correction.Length;

        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = 0; i < count; i++)
                scratch[i] = (correction[(i - 1 + count) % count]
                    + (correction[i] * 2f)
                    + correction[(i + 1) % count]) * 0.25f;

            Array.Copy(scratch, correction, count);
        }
    }

    /// <summary>
    /// Slides each sample along the loop towards the midpoint of its neighbours, taking only the
    /// component that runs <em>along</em> the curve.
    ///
    /// <para>
    /// This is Laplacian smoothing with the part that would reshape the loop removed. The full move
    /// towards a neighbour midpoint has a component across the curve, which is what shrinks a loop
    /// towards its own centre and would walk the line straight back out of the band; the component
    /// along it only changes where the samples sit, not where the curve goes. So spacing can be
    /// evened out as often as wanted and it costs the centring nothing.
    /// </para>
    /// </summary>
    private static void Redistribute(
        Vector3[] points, Vector3[] scratch, bool[] mask, float factor = 0.5f)
    {
        int count = points.Length;

        for (int i = 0; i < count; i++)
        {
            if (!mask[i])
            {
                scratch[i] = points[i];
                continue;
            }

            var previous = points[(i - 1 + count) % count];
            var next = points[(i + 1) % count];

            var tangent = next - previous;
            if (tangent.LengthSquared() < 1e-12f)
            {
                scratch[i] = points[i];
                continue;
            }

            tangent = Vector3.Normalize(tangent);
            var toMidpoint = ((previous + next) * 0.5f) - points[i];
            scratch[i] = points[i] + (tangent * Vector3.Dot(toMidpoint, tangent) * factor);
        }

        Array.Copy(scratch, points, count);
    }

    /// <summary>
    /// How much of the correction a point at this distance from the middle receives: none inside the
    /// idle band, all of it beyond the ramp, and smoothly between - so neighbouring samples either side
    /// of the threshold move by nearly the same amount rather than by all or nothing.
    /// </summary>
    private static float Eased(float offCentre, PartingLineCentringOptions options)
    {
        if (offCentre <= options.DeadZone) return 0f;
        if (options.Ramp <= 1e-6f) return 1f;

        float t = MathF.Min((offCentre - options.DeadZone) / options.Ramp, 1f);
        return t * t * (3f - (2f * t));
    }

    /// <summary>The band this point runs along, as the one whose nearer crease is nearest.</summary>
    private static int Nearest(Vector3 point, IReadOnlyList<PartingBand> bands)
    {
        int best = 0;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < bands.Count; i++)
        {
            float distance = MathF.Min(
                PartingBand.Closest(point, bands[i].First).Distance,
                PartingBand.Closest(point, bands[i].Second).Distance);

            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = i;
        }

        return best;
    }

    /// <summary>
    /// Taubin relaxation in place. Alternating a shrinking pass with an inflating one, for the same
    /// reason the trace does: a plain Laplacian would pull the loop towards its own centre and off the
    /// band this has just finished putting it in the middle of.
    /// </summary>
    private static void Smooth(Vector3[] points, Vector3[] scratch, bool[] mask, int passes)
    {
        const float Lambda = 0.55f;
        const float Mu = -0.58f;

        for (int pass = 0; pass < passes; pass++)
        {
            Sweep(points, scratch, mask, Lambda);
            Sweep(scratch, points, mask, Mu);
        }

        static void Sweep(Vector3[] source, Vector3[] destination, bool[] mask, float factor)
        {
            int count = source.Length;
            for (int i = 0; i < count; i++)
            {
                if (!mask[i])
                {
                    destination[i] = source[i];
                    continue;
                }

                var midpoint = (source[(i - 1 + count) % count] + source[(i + 1) % count]) * 0.5f;
                destination[i] = source[i] + (factor * (midpoint - source[i]));
            }
        }
    }
}
