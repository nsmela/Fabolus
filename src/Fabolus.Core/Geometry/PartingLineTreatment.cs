using System.Numerics;

namespace Fabolus.Core.Geometry;

/// <summary>Settings for <see cref="PartingLineTreatment"/>.</summary>
public sealed record PartingLineTreatmentOptions
{
    /// <summary>How the line is read before anything is done to it.</summary>
    public PartingLineSectionOptions Sections { get; init; } = PartingLineSectionOptions.Default;

    /// <summary>
    /// How far the clearance must recover before a faulty run is called finished, so the repair is
    /// anchored on samples the line was actually happy at rather than on the last one that scraped past
    /// the floor.
    /// </summary>
    public float Recovered { get; init; } = 0.40f;

    /// <summary>
    /// How much of the loop one run may cover before it is left alone, as a fraction. A diagnosis
    /// covering a quarter of the rim is not a defect in the rim, and rebuilding that far replaces the
    /// parting line with a chord across the body.
    /// </summary>
    public float LongestRun { get; init; } = 0.25f;

    /// <summary>
    /// Passes of circular averaging applied to the sideways correction before it is applied, which is
    /// what blends each shift into the samples either side of it instead of stepping.
    /// </summary>
    public int BlendPasses { get; init; } = 6;

    /// <summary>How many sub-steps a sideways shift is walked in, each one put back on the surface.</summary>
    public int ShiftSteps { get; init; } = 3;

    /// <summary>Passes of local easing applied to a kinked sample and its immediate neighbours.</summary>
    public int EasePasses { get; init; } = 12;

    /// <summary>
    /// Passes of the finishing flow, which eases the line along the rim and holds it across the rim
    /// once the aimed treatments are done.
    ///
    /// <para>
    /// Needed because a diagnosis is a floor test, not a quality test:
    /// <see cref="PartingLineCondition.Sound"/> means clear of both creases and not spiking, which most
    /// of a line is while still having room to be better. Measured on <c>standard</c>, treating only
    /// what was diagnosed left the line at 0.33 clearance against 0.44 with this behind it - the
    /// diagnosed stretches were 8% of the loop and the other 92% was where the rest of the gain was.
    /// </para>
    /// </summary>
    public int PolishPasses { get; init; } = 60;

    /// <summary>The clearance the finishing flow will not take the line below.</summary>
    public float PolishFloor { get; init; } = 0.40f;

    /// <summary>
    /// How many times the whole diagnose-and-treat cycle may run. More than one because a repair can
    /// leave a different condition behind - bridging a detour can put a mild kink at each end - and the
    /// second pass is what catches that. It stops early once nothing is left to treat.
    /// </summary>
    public int Rounds { get; init; } = 3;

    public static PartingLineTreatmentOptions Default { get; } = new();
}

/// <summary>What was treated, and what the line looked like before and after.</summary>
public sealed record PartingLineTreatmentReport(
    PartingLineReport Before, PartingLineReport After, int Rounds,
    int Bridged, int Shifted, int Eased, int Refused);

/// <summary>
/// Applies one repair per diagnosis, and nothing at all to a stretch with no diagnosis.
///
/// <para>
/// The point of the split is that the three faults want three different things done. A
/// <see cref="PartingLineCondition.Detour"/> is a shape problem - the line went round something - so
/// its stretch is rebuilt from the samples either side and its position follows from that.
/// <see cref="PartingLineCondition.Adrift"/> is a position problem with no shape problem attached, so
/// it is moved sideways and its shape is left exactly as it was. A
/// <see cref="PartingLineCondition.Kinked"/> sample is neither: it is centred and clear, and only needs
/// easing where it stands. <see cref="PartingLineCondition.Necked"/> gets nothing done to it at all,
/// because a wall with no middle has no better place to put the line than where it already is.
/// </para>
///
/// <para>
/// What this replaces treated all of them alike - anything below a clearance threshold was rebuilt, and
/// then a shortening flow was run over the whole loop whether it needed it or not. That worked, and it
/// worked by doing two blunt things whose side effects happened to cancel. The cost showed up as a
/// global pass that could not tell a bend the rim genuinely takes from a defect, and so had to be held
/// back by a limit rather than aimed.
/// </para>
/// </summary>
public static class PartingLineTreatment
{
    /// <summary>Treats every loop of a line against the rim wall each one runs in.</summary>
    public static PartingLine Apply(
        PartingLine line, IReadOnlyList<PartingBand> bands,
        PartingLineTreatmentOptions? options = null, ISurfaceProjector? projector = null)
    {
        if (line is null || bands is null || bands.Count == 0) return line!;

        var treated = new List<IReadOnlyList<Vector3>>(line.Loops.Count);
        foreach (var loop in line.Loops)
            treated.Add(Apply(loop.ToArray(), Nearest(loop, bands), out _, options, projector));

        return new PartingLine(treated);
    }

    /// <summary>Treats one loop against the wall it runs in.</summary>
    public static Vector3[] Apply(
        Vector3[] loop, PartingBand band, out PartingLineTreatmentReport report,
        PartingLineTreatmentOptions? options = null, ISurfaceProjector? projector = null)
    {
        options ??= PartingLineTreatmentOptions.Default;

        var before = PartingLineSections.Analyse(loop, band, options.Sections);
        report = new PartingLineTreatmentReport(before, before, 0, 0, 0, 0, 0);

        if (loop is null || loop.Length < 8 || before.Samples.Count == 0) return loop!;

        var work = (Vector3[])loop.Clone();
        int bridged = 0, shifted = 0, eased = 0, refused = 0, rounds = 0;

        for (int round = 0; round < options.Rounds; round++)
        {
            var read = round == 0 ? before : PartingLineSections.Analyse(work, band, options.Sections);
            if (read.IsSound) break;

            rounds++;

            // Grown before anything is treated, so a run's anchors are samples that were sound. Done
            // here rather than in the analysis because it is a property of the repair - the diagnosis
            // should say where the fault is, not where a fix would like to begin.
            var runs = Grow(read, options.Recovered);

            foreach (var (condition, start, count) in runs)
            {
                if (count > work.Length * options.LongestRun || count >= work.Length - 4)
                {
                    refused++;
                    continue;
                }

                switch (condition)
                {
                    case PartingLineCondition.Detour:
                        Bridge(work, start, count, projector);
                        bridged++;
                        break;

                    case PartingLineCondition.Adrift:
                        Shift(work, band, start, count, options, projector);
                        shifted++;
                        break;

                    case PartingLineCondition.Kinked:
                        Ease(work, start, count, options.EasePasses, projector);
                        eased++;
                        break;
                }
            }
        }

        Polish(work, band, options, projector);

        report = new PartingLineTreatmentReport(
            before, PartingLineSections.Analyse(work, band, options.Sections),
            rounds, bridged, shifted, eased, refused);

        return work;
    }

    // ---------------------------------------------------------------- finishing

    /// <summary>
    /// Eases the line along the rim while holding it across the rim.
    ///
    /// <para>
    /// Splitting the move into those two directions is the whole of this. A plain shortening flow with
    /// a clearance limit looks correct and is not: shortening a loop that runs round a rim means cutting
    /// to the inside of every bend, so the flow marches steadily across the band and stops only when it
    /// reaches whatever limit it was given - which it then sits against. Measured, that parked three of
    /// the four coarse bodies at 0.60 of the way across, passing every clearance check while hugging one
    /// crease along their whole length. A limit says where the line may not go; it does not say where it
    /// should be. So the across-the-band part of each move is nearly all removed, leaving the along-the-
    /// band part that does the smoothing, and a gentle pull towards the middle is added in its place.
    /// </para>
    ///
    /// <para>
    /// Unlike the treatments above this is applied to the whole loop, and that is deliberate rather than
    /// an exception to the rule. It cannot rebuild anything and it cannot move a sample nearer a crease
    /// than the floor allows, so it has no way to damage a stretch that was already right - which is
    /// what let the blunt pass it replaces be run everywhere too. What that pass could not do, and this
    /// keeps clear of, is decide <em>where</em> to rebuild.
    /// </para>
    /// </summary>
    private static void Polish(
        Vector3[] loop, PartingBand band, PartingLineTreatmentOptions options,
        ISurfaceProjector? projector)
    {
        int n = loop.Length;
        if (n < 8 || options.PolishPasses <= 0) return;

        // How much of a move's across-the-band component survives. Not zero: the band twists, so the
        // across direction measured at a sample is never exactly square to the line, and forbidding the
        // component outright would fight the smoothing rather than only the drift.
        const float AcrossFreedom = 0.2f;

        // How hard the line is pulled back to the middle each pass. Gentle, because it is applied every
        // pass and over sixty of them a large one would overwhelm the smoothing entirely.
        const float Centring = 0.15f;

        for (int pass = 0; pass < options.PolishPasses; pass++)
        {
            bool moved = false;

            for (int i = 0; i < n; i++)
            {
                var first = PartingBand.Closest(loop[i], band.First).Point;
                var second = PartingBand.Closest(loop[i], band.Second).Point;

                var axis = second - first;
                float span = axis.Length();
                var across = span < 1e-6f ? Vector3.Zero : axis / span;

                var midpoint = (loop[(((i - 1) % n) + n) % n] + loop[(i + 1) % n]) * 0.5f;
                var move = (midpoint - loop[i]) * 0.5f;

                move -= across * Vector3.Dot(move, across) * (1f - AcrossFreedom);

                float at = span < 1e-6f ? 0.5f : Vector3.Dot(loop[i] - first, axis) / (span * span);
                move += across * ((0.5f - at) * span * Centring);

                var proposed = loop[i] + move;
                if (projector is not null) proposed = projector.Project(proposed);

                float was = MathF.Min(at, 1f - at);
                float now = Clearance(proposed, band);

                // Allowed if it ends up inside, or if it was already outside and the move is an
                // improvement. The second half matters: without it a sample that starts too near a
                // crease is frozen exactly where it is worst, which is the one place something has to
                // happen.
                if (now < options.PolishFloor && now <= was) continue;

                loop[i] = proposed;
                moved = true;
            }

            if (!moved) break;
        }
    }

    private static float Clearance(Vector3 point, PartingBand band)
    {
        var first = PartingBand.Closest(point, band.First).Point;
        var second = PartingBand.Closest(point, band.Second).Point;

        var axis = second - first;
        float span = axis.LengthSquared();
        float at = span < 1e-9f ? 0.5f : Vector3.Dot(point - first, axis) / span;

        return MathF.Min(at, 1f - at);
    }

    // ---------------------------------------------------------------- runs

    /// <summary>
    /// The faulty sections, each grown outward until the clearance has properly recovered. A run that
    /// grows into another keeps the condition of whichever is the more serious, because the treatments
    /// are ordered: rebuilding a stretch fixes its position too, but shifting one sideways does not fix
    /// its shape.
    /// </summary>
    private static List<(PartingLineCondition Condition, int Start, int Count)> Grow(
        PartingLineReport read, float recovered)
    {
        int n = read.Samples.Count;
        var condition = new PartingLineCondition[n];
        Array.Fill(condition, PartingLineCondition.Sound);

        foreach (var section in read.Sections)
            for (int k = 0; k < section.Count; k++)
                condition[(section.Start + k) % n] = section.Condition;

        var claimed = new PartingLineCondition?[n];

        foreach (var section in read.Sections)
        {
            if (section.Condition is PartingLineCondition.Sound or PartingLineCondition.Necked) continue;

            for (int k = 0; k < section.Count; k++) Claim(claimed, (section.Start + k) % n, section.Condition);

            // Kinked samples are clear of the creases by definition, so growing them on a clearance
            // test would grow them without limit. They are treated where they stand.
            if (section.Condition == PartingLineCondition.Kinked) continue;

            for (int direction = -1; direction <= 1; direction += 2)
            {
                int from = direction < 0 ? section.Start : (section.Start + section.Count - 1) % n;

                for (int step = 1; step < n / 2; step++)
                {
                    int at = (((from + (direction * step)) % n) + n) % n;
                    if (read.Samples[at].Clearance >= recovered) break;
                    if (condition[at] == PartingLineCondition.Necked) break;

                    Claim(claimed, at, section.Condition);
                }
            }
        }

        // Back into runs, so adjacent claims of the same kind are treated as one stretch.
        var runs = new List<(PartingLineCondition, int, int)>();
        int origin = Array.FindIndex(claimed, c => c is null);
        if (origin < 0) return runs;

        int index = 0;
        while (index < n)
        {
            int at = (origin + index) % n;
            if (claimed[at] is not { } kind) { index++; continue; }

            int count = 0;
            while (count < n - index && claimed[(origin + index + count) % n] == kind) count++;

            runs.Add((kind, at, count));
            index += count;
        }

        return runs;
    }

    /// <summary>Detour outranks adrift, which outranks kinked - see <see cref="Grow"/>.</summary>
    private static void Claim(PartingLineCondition?[] claimed, int at, PartingLineCondition condition)
    {
        int Rank(PartingLineCondition c) => c switch
        {
            PartingLineCondition.Detour => 3,
            PartingLineCondition.Adrift => 2,
            PartingLineCondition.Kinked => 1,
            _ => 0,
        };

        if (claimed[at] is { } held && Rank(held) >= Rank(condition)) return;
        claimed[at] = condition;
    }

    // ---------------------------------------------------------------- treatments

    /// <summary>
    /// Rebuilds a stretch from the samples either side of it, tangents included, so the replacement
    /// meets the line smoothly at both ends rather than as a chord with a corner at each.
    /// </summary>
    private static void Bridge(Vector3[] loop, int start, int count, ISurfaceProjector? projector)
    {
        int n = loop.Length;

        int before = (((start - 1) % n) + n) % n;
        int after = (start + count) % n;
        var beforeTangent = loop[(((before - 1) % n) + n) % n];
        var afterTangent = loop[(after + 1) % n];

        for (int k = 0; k < count; k++)
        {
            float t = (k + 1f) / (count + 1f);
            var point = CatmullRom(beforeTangent, loop[before], loop[after], afterTangent, t);
            loop[(start + k) % n] = projector is null ? point : projector.Project(point);
        }
    }

    /// <summary>
    /// Moves a stretch across the wall to the middle without altering its shape along the wall.
    ///
    /// <para>
    /// The displacement is worked out for every sample first, blended into the samples either side, and
    /// only then applied - so the stretch arrives at the middle and rejoins the rest of the line
    /// without a step at either end. Walked across in a few sub-steps with a projection after each,
    /// because half a wall is far enough that a single straight move leaves a curved rim.
    /// </para>
    /// </summary>
    private static void Shift(
        Vector3[] loop, PartingBand band, int start, int count,
        PartingLineTreatmentOptions options, ISurfaceProjector? projector)
    {
        int n = loop.Length;
        var wanted = new float[n];

        for (int k = 0; k < count; k++)
        {
            int at = (start + k) % n;
            var first = PartingBand.Closest(loop[at], band.First).Point;
            var second = PartingBand.Closest(loop[at], band.Second).Point;

            var axis = second - first;
            float span = axis.LengthSquared();
            if (span < 1e-9f) continue;

            wanted[at] = 0.5f - (Vector3.Dot(loop[at] - first, axis) / span);
        }

        Blend(wanted, options.BlendPasses);

        for (int i = 0; i < n; i++)
        {
            if (MathF.Abs(wanted[i]) < 1e-4f) continue;

            for (int step = 0; step < options.ShiftSteps; step++)
            {
                var first = PartingBand.Closest(loop[i], band.First).Point;
                var second = PartingBand.Closest(loop[i], band.Second).Point;

                var axis = second - first;
                if (axis.LengthSquared() < 1e-9f) break;

                loop[i] += axis * (wanted[i] / options.ShiftSteps);
                if (projector is not null) loop[i] = projector.Project(loop[i]);
            }
        }
    }

    /// <summary>Eases a spike towards the midpoint of its neighbours, and leaves the rest alone.</summary>
    private static void Ease(
        Vector3[] loop, int start, int count, int passes, ISurfaceProjector? projector)
    {
        int n = loop.Length;

        for (int pass = 0; pass < passes; pass++)
            for (int k = 0; k < count; k++)
            {
                int at = (start + k) % n;
                var midpoint = (loop[(((at - 1) % n) + n) % n] + loop[(at + 1) % n]) * 0.5f;

                loop[at] += (midpoint - loop[at]) * 0.5f;
                if (projector is not null) loop[at] = projector.Project(loop[at]);
            }
    }

    // ---------------------------------------------------------------- helpers

    private static void Blend(float[] values, int passes)
    {
        int n = values.Length;
        if (n < 3 || passes <= 0) return;

        var scratch = new float[n];
        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = 0; i < n; i++)
                scratch[i] = (values[(((i - 1) % n) + n) % n] + (values[i] * 2f) + values[(i + 1) % n])
                    * 0.25f;
            Array.Copy(scratch, values, n);
        }
    }

    private static Vector3 CatmullRom(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * b) +
            ((c - a) * t) +
            ((((2f * a) - (5f * b)) + (4f * c) - d) * t2) +
            ((-a + (3f * b) - (3f * c) + d) * t3));
    }

    private static PartingBand Nearest(IReadOnlyList<Vector3> loop, IReadOnlyList<PartingBand> bands)
    {
        var best = bands[0];
        float bestDistance = float.MaxValue;

        foreach (var band in bands)
        {
            float total = 0f;
            int taken = 0;

            for (int i = 0; i < loop.Count; i += Math.Max(loop.Count / 16, 1))
            {
                total += MathF.Min(
                    PartingBand.Closest(loop[i], band.First).Distance,
                    PartingBand.Closest(loop[i], band.Second).Distance);
                taken++;
            }

            float mean = taken == 0 ? float.MaxValue : total / taken;
            if (mean >= bestDistance) continue;

            bestDistance = mean;
            best = band;
        }

        return best;
    }
}
