using System.Numerics;

namespace Fabolus.Core.Geometry;

/// <summary>
/// What is wrong with one stretch of a parting line, which is what decides how to put it right.
///
/// <para>
/// The distinction that earns this its existence is between <see cref="Detour"/> and
/// <see cref="Adrift"/>. Both read as the line coming too near a crease and they want opposite
/// treatments: a detour is a stretch that went somewhere and came back, so the repair is to rebuild its
/// shape and the position follows; a stretch that is adrift never changed shape at all - the band
/// widened underneath it and left it behind - so the repair is to move it across and the shape must not
/// be touched. Treating either as the other is worse than treating neither, because rebuilding a
/// straight stretch puts a curve into it and shifting a detour sideways moves the detour rather than
/// removing it.
/// </para>
/// </summary>
public enum PartingLineCondition
{
    /// <summary>Centred, clear of both creases, and turning no harder than the rim does.</summary>
    Sound,

    /// <summary>
    /// Near a crease, and carrying much more arc than the straight line across it: the line went round
    /// something. Rebuild the stretch.
    /// </summary>
    Detour,

    /// <summary>
    /// Near a crease with no extra arc to explain it: the line is running parallel to the creases but
    /// not down the middle. Move it across, and leave its shape alone.
    ///
    /// <para>
    /// Deliberately named for what is measured rather than for a cause. The obvious story - that the
    /// wall widened and left a line that never moved behind - is not what the numbers say on
    /// <c>standard</c>, where the one adrift stretch reads 0.94 to 1.03 times the median width. The
    /// wall is its normal width and the line is simply not in the middle of it, which is a fault worth
    /// naming even while the reason for it is still open.
    /// </para>
    /// </summary>
    Adrift,

    /// <summary>
    /// The wall has narrowed until it has no middle worth aiming at, and the line is still inside it.
    /// Nothing here is a defect and nothing should be moved - the line is as central as the geometry
    /// allows.
    /// </summary>
    Necked,

    /// <summary>
    /// Clear of the creases and centred, but turning far harder than its neighbours: a spike at the
    /// scale of one sample rather than a feature of the rim. Ease it in place.
    /// </summary>
    Kinked,
}

/// <summary>Everything measured at one sample of the line.</summary>
/// <param name="Across">0 on the first crease, 1 on the other. Outside that range it has left the wall.</param>
/// <param name="Clearance">How far from the nearer crease, as a share of the way across.</param>
/// <param name="Width">The wall's width here as a multiple of its own median.</param>
/// <param name="Bulge">
/// Arc over chord across a window of a couple of wall widths. One is straight; a stretch that goes
/// somewhere and returns runs well above it.
/// </param>
/// <param name="Turn">Degrees turned at this sample.</param>
/// <param name="Step">Distance to the next sample, as a multiple of the median step.</param>
public sealed record PartingLineSample(
    float Across, float Clearance, float Width, float Bulge, float Turn, float Step);

/// <summary>A run of consecutive samples sharing one condition.</summary>
/// <param name="Start">First sample of the run. May exceed the end - the loop wraps.</param>
/// <param name="Worst">
/// The most extreme reading behind the classification: the least clearance for
/// <see cref="PartingLineCondition.Detour"/> and <see cref="PartingLineCondition.Adrift"/>, the
/// narrowest width for <see cref="PartingLineCondition.Necked"/>, the sharpest turn for
/// <see cref="PartingLineCondition.Kinked"/>.
/// </param>
public sealed record PartingLineSection(
    PartingLineCondition Condition, int Start, int Count, float Worst);

/// <summary>The line, sample by sample and stretch by stretch.</summary>
public sealed record PartingLineReport(
    IReadOnlyList<PartingLineSample> Samples, IReadOnlyList<PartingLineSection> Sections)
{
    public static PartingLineReport Empty { get; } =
        new(Array.Empty<PartingLineSample>(), Array.Empty<PartingLineSection>());

    public int SamplesIn(PartingLineCondition condition) =>
        Sections.Where(s => s.Condition == condition).Sum(s => s.Count);

    public float ShareIn(PartingLineCondition condition) =>
        Samples.Count == 0 ? 0f : (float)SamplesIn(condition) / Samples.Count;

    /// <summary>The least clearance anywhere on the line, which is the figure a flange seal turns on.</summary>
    public float Nearest => Samples.Count == 0 ? 0f : Samples.Min(s => s.Clearance);

    public bool IsSound => Sections.All(s => s.Condition is
        PartingLineCondition.Sound or PartingLineCondition.Necked);
}

/// <summary>Settings for <see cref="PartingLineSections"/>.</summary>
public sealed record PartingLineSectionOptions
{
    /// <summary>
    /// How near a crease the line may come, as a share of the way across the wall, before the stretch
    /// is called faulty. Not an aesthetic threshold: the flange rim is offset from the line and stops
    /// sealing well before the line reaches a crease.
    /// </summary>
    public float ClearanceFloor { get; init; } = 0.30f;

    /// <summary>
    /// How much arc over chord separates a stretch that went round something from one that merely sits
    /// off centre. Used only to tell two faults apart, never to find one on its own - as a detector in
    /// its own right it cannot distinguish a step in the rim from the rim's own curvature, and flagged
    /// six times too much when it was tried that way.
    /// </summary>
    public float BulgeRatio { get; init; } = 1.15f;

    /// <summary>How many wall widths the bulge is measured over.</summary>
    public float BulgeWindowWidths { get; init; } = 2f;

    /// <summary>
    /// How narrow the wall may read, against its own median, before its middle stops meaning anything.
    /// Below this the ratio that locates the middle is dividing by nearly nothing.
    /// </summary>
    public float NeckedWidth { get; init; } = 0.6f;

    /// <summary>How hard a sample may turn, in degrees, before it is a spike rather than a bend.</summary>
    public float KinkDegrees { get; init; } = 45f;

    public static PartingLineSectionOptions Default { get; } = new();
}

/// <summary>
/// Reads a parting line against the wall it runs in and sorts it into stretches by what, if anything,
/// is wrong with each.
///
/// <para>
/// This exists because every repair written before it began by choosing a threshold and treating
/// everything past it the same way, and the measurements kept saying that was the wrong shape for the
/// problem. One stretch of <c>standard</c> is near a crease because the rim steps and the line followed
/// the step; another is near a crease because the wall widens there and a line that never turned got
/// left behind. The same number describes both and no single repair suits both. Diagnosing first and
/// treating second is the only way to apply the right one, and it also makes it possible to say a
/// stretch is <em>fine</em> - which no threshold-and-treat pass can, because it has no way to tell a
/// bend the rim genuinely takes from a defect.
/// </para>
/// </summary>
public static class PartingLineSections
{
    /// <summary>Measures and classifies one loop against the wall it runs in.</summary>
    public static PartingLineReport Analyse(
        IReadOnlyList<Vector3> loop, PartingBand band, PartingLineSectionOptions? options = null)
    {
        options ??= PartingLineSectionOptions.Default;

        int n = loop?.Count ?? 0;
        if (n < 8 || band?.First is null || band.Second is null) return PartingLineReport.Empty;

        var across = new float[n];
        var width = new float[n];
        var step = new float[n];
        var turn = new float[n];

        for (int i = 0; i < n; i++)
        {
            var first = PartingBand.Closest(loop[i], band.First).Point;
            var second = PartingBand.Closest(loop[i], band.Second).Point;

            var axis = second - first;
            float span = axis.LengthSquared();

            across[i] = span < 1e-9f ? 0.5f : Vector3.Dot(loop[i] - first, axis) / span;
            width[i] = MathF.Sqrt(span);
            step[i] = Vector3.Distance(loop[i], loop[(i + 1) % n]);

            var incoming = loop[i] - loop[(((i - 1) % n) + n) % n];
            var outgoing = loop[(i + 1) % n] - loop[i];
            turn[i] = incoming.LengthSquared() < 1e-12f || outgoing.LengthSquared() < 1e-12f
                ? 0f
                : MathF.Acos(Math.Clamp(
                    Vector3.Dot(Vector3.Normalize(incoming), Vector3.Normalize(outgoing)), -1f, 1f))
                    * 180f / MathF.PI;
        }

        float medianWidth = Median(width);
        float medianStep = Median(step);
        if (medianWidth < 1e-5f || medianStep < 1e-5f) return PartingLineReport.Empty;

        var bulge = Bulge(loop, step, medianWidth * options.BulgeWindowWidths);

        var samples = new PartingLineSample[n];
        var condition = new PartingLineCondition[n];

        for (int i = 0; i < n; i++)
        {
            float clearance = MathF.Min(across[i], 1f - across[i]);
            float relative = width[i] / medianWidth;

            samples[i] = new PartingLineSample(
                across[i], clearance, relative, bulge[i], turn[i], step[i] / medianStep);

            // Necked first, because where the wall has no width the clearance reading is a ratio
            // whose denominator has gone, and everything below would be judging the line against a
            // middle that is not there.
            condition[i] =
                relative < options.NeckedWidth ? PartingLineCondition.Necked
                : clearance >= options.ClearanceFloor
                    ? turn[i] > options.KinkDegrees ? PartingLineCondition.Kinked
                    : PartingLineCondition.Sound
                : bulge[i] >= options.BulgeRatio ? PartingLineCondition.Detour
                : PartingLineCondition.Adrift;
        }

        return new PartingLineReport(samples, Runs(condition, samples));
    }

    /// <summary>
    /// Arc over chord across a window centred on each sample. Centred rather than run forward, so the
    /// reading belongs to the middle of the stretch it describes - measured forward, a detour's high
    /// reading lands on the samples approaching it rather than on the detour itself.
    /// </summary>
    private static float[] Bulge(IReadOnlyList<Vector3> loop, float[] step, float window)
    {
        int n = loop.Count;
        var bulge = new float[n];

        for (int i = 0; i < n; i++)
        {
            float arc = 0f;
            int back = 0, forward = 0;

            while (arc < window * 0.5f && back < n / 6)
            {
                arc += step[(((i - back - 1) % n) + n) % n];
                back++;
            }

            while (arc < window && forward < n / 6)
            {
                arc += step[(i + forward) % n];
                forward++;
            }

            if (back + forward < 2) { bulge[i] = 1f; continue; }

            float chord = Vector3.Distance(
                loop[(((i - back) % n) + n) % n], loop[(i + forward) % n]);

            bulge[i] = chord < 1e-5f ? float.MaxValue : arc / chord;
        }

        return bulge;
    }

    private static IReadOnlyList<PartingLineSection> Runs(
        PartingLineCondition[] condition, PartingLineSample[] samples)
    {
        int n = condition.Length;

        // Walked from a sound sample where there is one, so a run straddling index zero comes back as
        // one run rather than as two facing each other.
        int origin = Array.FindIndex(condition, c => c == PartingLineCondition.Sound);
        if (origin < 0) origin = 0;

        var sections = new List<PartingLineSection>();
        int at = 0;

        while (at < n)
        {
            int start = (origin + at) % n;
            var kind = condition[start];

            int count = 0;
            while (count < n - at && condition[(origin + at + count) % n] == kind) count++;

            sections.Add(new PartingLineSection(kind, start, count, Worst(kind, samples, start, count)));
            at += count;
        }

        return sections;
    }

    private static float Worst(
        PartingLineCondition kind, PartingLineSample[] samples, int start, int count)
    {
        int n = samples.Length;
        float worst = kind == PartingLineCondition.Kinked ? float.MinValue : float.MaxValue;

        for (int k = 0; k < count; k++)
        {
            var sample = samples[(start + k) % n];
            worst = kind switch
            {
                PartingLineCondition.Kinked => MathF.Max(worst, sample.Turn),
                PartingLineCondition.Necked => MathF.Min(worst, sample.Width),
                _ => MathF.Min(worst, sample.Clearance),
            };
        }

        return worst;
    }

    private static float Median(float[] values)
    {
        var sorted = (float[])values.Clone();
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }
}
