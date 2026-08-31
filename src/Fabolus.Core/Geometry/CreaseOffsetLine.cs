using System.Numerics;

namespace Fabolus.Core.Geometry;

/// <summary>Settings for <see cref="CreaseOffsetLine"/>.</summary>
public sealed record CreaseOffsetOptions
{
    /// <summary>
    /// How far across the wall the line is laid, as a fraction of the measured width. Half puts it down
    /// the middle, which is what a parting line wants; it is a setting rather than a constant only so a
    /// caller deliberately biasing the line towards one shell can say so.
    /// </summary>
    public float Fraction { get; init; } = 0.5f;

    /// <summary>
    /// How long a step the walk across the wall takes, as a multiple of the mesh's mean edge.
    ///
    /// <para>
    /// The walk is what makes the offset an offset rather than a chord, and the step length is what
    /// makes the walk follow the surface. A rim wall is rounded over, so the straight line from one
    /// crease to the other passes through the solid - on <c>standard</c> it dives five millimetres
    /// under - and a single move that long cannot be projected back onto anything meaningful. Short
    /// steps, each re-aimed and put back on the body, trace the surface instead of cutting across it.
    /// </para>
    /// </summary>
    public float StepFraction { get; init; } = 0.35f;

    /// <summary>How many steps the walk may take before it is abandoned as not arriving.</summary>
    public int MaxSteps { get; init; } = 400;

    /// <summary>
    /// Passes of circular averaging applied to the offset distance along the base crease.
    ///
    /// <para>
    /// Only does anything where the wall is narrower than its own median and the offset has been pulled
    /// in. Everywhere else the distance is one number for the whole loop, which is the point of the
    /// method: a constant offset cannot carry a local measurement's noise into the line, and every
    /// method that placed each point by a local measurement did.
    /// </para>
    /// </summary>
    public int DistanceSmoothingPasses { get; init; } = 12;

    /// <summary>
    /// How much the offset holds to one width for the whole rim rather than following the rim's own.
    /// One is a single number for the loop; zero follows the smoothed profile of the measured crossing.
    ///
    /// <para>
    /// Zero by default, which is not the obvious reading of "offset by half the thickness" and is what
    /// the measurements say. A constant offset is perfectly smooth and lands off centre wherever the
    /// wall is not its median width, and these walls run to one and a half times theirs - measured, it
    /// put 12% of the widest body's points outside the middle third against 3% for the line it would
    /// replace. The profile is smoothed hard enough (see <see cref="WidthSmoothingPasses"/>) that
    /// following it costs almost none of the smoothness the constant bought.
    /// </para>
    /// </summary>
    public float Constancy { get; init; } = 0f;

    /// <summary>
    /// Passes of circular averaging applied to the measured crossing before the offset follows it.
    ///
    /// <para>
    /// Deliberately large. This is the dial that decides how much of the wall's own shape the line is
    /// allowed to see: too few passes and every wobble in the crossing measurement becomes a wobble in
    /// the line, which is the failure every previous method had; too many and it is a constant again,
    /// which lands off centre wherever the rim widens.
    /// </para>
    /// </summary>
    public int WidthSmoothingPasses { get; init; } = 50;

    /// <summary>
    /// How many walks must reach the far crease before the rim is one this can answer for, as a
    /// fraction of the samples. A rim most of whose walks never cross is not a wall along its length,
    /// and the median crossing taken off the few that did describes somewhere else on the body.
    /// </summary>
    public float FewestCrossings { get; init; } = 0.25f;

    /// <summary>Taubin passes applied to the finished curve, with a projection after each.</summary>
    public int SmoothingPasses { get; init; } = 4;

    public static CreaseOffsetOptions Default { get; } = new();
}

/// <summary>
/// How steady one crease runs, in the two ways an unsteady one goes wrong.
/// </summary>
/// <param name="TurnP95">
/// The 95th percentile turn between consecutive samples, in degrees, measured after resampling to an
/// even spacing so a densely sampled stretch does not read as sharper than a sparse one. This is the
/// crease's own noise: a crease the detector followed confidently turns smoothly, and one it guessed
/// at zigzags.
/// </param>
/// <param name="WidthVariation">
/// The spread of this crease's distance to the other, as (p90 - p10) over the median. Distance to a
/// curve is a minimum, so it is far more sensitive to wobble in the crease the samples are taken
/// <em>from</em> than to wobble in the one they are taken <em>to</em> - which is what makes it tell the
/// two creases apart rather than scoring them alike.
/// </param>
/// <param name="Score">
/// The two together, each as its share of the pair's total, so neither unit has to be converted into
/// the other and neither can dominate by being the larger number. Lower is steadier.
/// </param>
public sealed record CreaseSteadiness(float TurnP95, float WidthVariation, float Score);

/// <summary>What the offset was built from, so a caller can see which crease it trusted and why.</summary>
/// <param name="Base">Which of the band's contours the line was offset from: 0 for first, 1 for second.</param>
/// <param name="Crossing">
/// The wall's width <em>along the surface</em> - the median arc length of the walk from the base crease
/// to the other. Longer than the straight-line width wherever the rim is rounded over, and it is this
/// one the offset is half of, because half the straight-line width lands nowhere near half way round.
/// </param>
/// <param name="Offset">How far the line was laid off the base crease, at the median.</param>
/// <param name="Reached">
/// How many samples' walks arrived at the far crease. A walk that does not arrive has no width to
/// contribute; where most of them do not, the wall is not a wall along that stretch.
/// </param>
/// <param name="Clamped">
/// How many samples were pulled in short of the full offset because the wall was narrower there.
/// Zero on an even rim; a large share means the rim tapers, and that the single width this is built on
/// describes only part of it.
/// </param>
/// <param name="Narrowest">The 5th percentile crossing, and <paramref name="Widest"/> the 95th.</param>
/// <param name="Widest">
/// How much wider the rim gets than its median is the whole case against one thickness for the loop: a
/// constant offset is half the median, so where the rim crosses this much further the line lands
/// <c>Crossing / Widest</c> of the way over instead of half.
/// </param>
public sealed record CreaseOffsetReport(
    int Base, float Crossing, float Narrowest, float Widest, float Offset,
    int Samples, int Reached, int Clamped, int Short,
    IReadOnlyDictionary<WalkStop, int> Crossings, IReadOnlyDictionary<WalkStop, int> Offsets,
    CreaseSteadiness First, CreaseSteadiness Second);

/// <summary>Why a walk across the wall stopped, which is the only thing that says whether to trust it.</summary>
public enum WalkStop
{
    /// <summary>It went the distance it was asked for, or reached the far crease.</summary>
    Arrived,

    /// <summary>No direction across the surface could be found - the normal and the wall's run agreed.</summary>
    NoHeading,

    /// <summary>A step was undone in full by the projection, so the walk was standing still.</summary>
    Stalled,

    /// <summary>It stopped getting nearer the far crease, so it was running along the band, not across.</summary>
    LeftTheWall,

    /// <summary>It ran out of steps.</summary>
    Budget,
}

/// <summary>
/// The parting line as one crease of the rim wall walked across it by half the wall's width.
///
/// <para>
/// The reasoning is that a crease is the one curve on the body that is genuinely there. It is where two
/// surfaces meet at an angle, the detector follows it directly, and finding it needs nothing solved
/// over the band. The middle of the wall, by contrast, has been reached three ways so far - correcting
/// a traced seam, a level set through a scalar field, a geodesic medial - and each needs the band to be
/// wide enough, evenly enough meshed, or unpinched enough to carry what is solved over it. Offsetting
/// inherits none of that: the shape of the line is the shape of the crease, and the wall's width enters
/// as a single number.
/// </para>
///
/// <para>
/// That single number is what makes it smooth. Every method that placed each point by a local
/// measurement carried that measurement's noise into the curve and then had to smooth it back out,
/// which is a second choice with its own threshold made after the damage. Here there is no per-point
/// measurement to be noisy: one width for the loop, one crease for its shape.
/// </para>
///
/// <para>
/// The width is measured along the surface rather than straight through it, and that is not a detail.
/// A rim wall is rounded over, so the straight-line distance between its two creases is a chord under
/// the surface - on a semicircular rim it is the diameter against the arc, short by more than a third.
/// Half of that lands the line a third of the way round rather than half, which is what the first
/// version of this did on every body in the set.
/// </para>
///
/// <para>
/// The cost is that it is only as good as the crease it picks, so it picks deliberately - see
/// <see cref="CreaseSteadiness"/> - and it cannot do anything sensible where the wall has no width. A
/// stretch that tapers towards a knife edge has its offset pulled in to what is actually there, which
/// keeps the line inside the wall at the price of it no longer being half a width from anything.
/// </para>
/// </summary>
public static class CreaseOffsetLine
{
    /// <summary>
    /// Traces one rim's parting line, or null when neither crease yields a walk across the wall.
    /// </summary>
    /// <param name="mesh">
    /// The body, for its surface normals. The walk needs the direction across the wall to lie
    /// <em>in</em> the surface, and the only thing that says where the surface faces is the surface.
    /// </param>
    /// <param name="projector">
    /// Closest-point projection onto <paramref name="mesh"/>. Wanted rather than required, for the same
    /// reason <see cref="ThicknessParting.Trace"/>'s is - without it each step is taken in the tangent
    /// plane and never corrected, so the walk drifts off a curved rim over its length.
    /// </param>
    public static IReadOnlyList<Vector3>? Trace(
        IMesh mesh, PartingBand band, ISurfaceProjector? projector = null,
        CreaseOffsetOptions? options = null) => Trace(mesh, band, out _, projector, options);

    /// <inheritdoc cref="Trace(IMesh, PartingBand, ISurfaceProjector, CreaseOffsetOptions)"/>
    public static IReadOnlyList<Vector3>? Trace(
        IMesh mesh, PartingBand band, out CreaseOffsetReport? report,
        ISurfaceProjector? projector = null, CreaseOffsetOptions? options = null)
    {
        report = null;
        options ??= CreaseOffsetOptions.Default;

        if (mesh is null || band?.First is null || band.Second is null) return null;
        if (band.First.Points.Count < 8 || band.Second.Points.Count < 8) return null;

        var normals = SurfaceNormals.Build(mesh);
        if (normals is null) return null;

        // Both creases are resampled before anything is measured off them. The detector walks mesh
        // edges, so its steps run several to one longest-to-shortest, and a turn angle read off that
        // says as much about the tessellation as about the crease.
        var first = Resample(band.First.Points, MedianStep(band.First.Points));
        var second = Resample(band.Second.Points, MedianStep(band.Second.Points));
        if (first.Length < 8 || second.Length < 8) return null;

        var (steadyFirst, steadySecond) = Steadiness(first, second);

        bool useFirst = steadyFirst.Score <= steadySecond.Score;
        var baseLine = useFirst ? first : second;
        var far = useFirst ? band.Second : band.First;

        float step = normals.MeanEdge * options.StepFraction;

        // Walked all the way across first, to find out how wide the wall is along the surface. Nothing
        // else measures that: the straight-line width is a chord under a rounded rim, and the offset
        // has to be half of what the line will actually travel.
        var crossing = new float[baseLine.Length];
        var crossingStops = new Dictionary<WalkStop, int>();
        for (int i = 0; i < baseLine.Length; i++)
        {
            crossing[i] = Walk(
                baseLine[i], Along(baseLine, i), far, float.PositiveInfinity,
                step, options.MaxSteps, normals, projector, out var why).Travelled;
            crossingStops[why] = crossingStops.GetValueOrDefault(why) + 1;
        }

        var reached = crossing.Where(float.IsFinite).ToArray();
        if (reached.Length == 0) return null;

        Array.Sort(reached);
        float width = reached[reached.Length / 2];
        if (width < 1e-4f) return null;

        float offset = width * options.Fraction;

        // A sample whose walk did not cross takes the width of the walks either side of it rather than
        // the median of the whole rim. A stretch the walks could not cross is a stretch where something
        // local is going on, and the median is the one figure guaranteed to know nothing about it.
        var filled = Fill(crossing, width);

        // The profile the offset follows, smoothed hard along the rim. The choice between one width for
        // the loop and a width per sample is a real one and neither end of it is right: a constant
        // offset is perfectly smooth and lands off centre wherever the wall is not its median width,
        // and these walls run to one and a half times theirs. A width per sample follows them and
        // carries the measurement's noise straight into the curve. Smoothing hard keeps the first and
        // drops the second - what survives fifty passes is the rim genuinely widening over a span of
        // it, and what does not is sampling.
        var profile = (float[])filled.Clone();
        SmoothCircular(profile, options.WidthSmoothingPasses);

        var distance = new float[baseLine.Length];
        int clamped = 0;
        for (int i = 0; i < baseLine.Length; i++)
        {
            float following = (profile[i] * (1f - options.Constancy)) + (width * options.Constancy);

            // Never past the middle of what is actually there, whatever the profile says. Measured off
            // the raw crossing rather than the smoothed one, because a pinch is exactly the feature
            // smoothing takes out, and this is the guard that stops the line walking through the far
            // crease and out onto the far shell.
            distance[i] = MathF.Min(following, filled[i]) * options.Fraction;
            if (distance[i] < offset - 1e-4f) clamped++;
        }

        SmoothCircular(distance, options.DistanceSmoothingPasses);

        var laid = new Vector3[baseLine.Length];
        var offsetStops = new Dictionary<WalkStop, int>();
        int fellShort = 0;
        for (int i = 0; i < baseLine.Length; i++)
        {
            var (point, travelled) = Walk(
                baseLine[i], Along(baseLine, i), far, distance[i],
                step, options.MaxSteps, normals, projector, out var why);

            laid[i] = point;
            offsetStops[why] = offsetStops.GetValueOrDefault(why) + 1;
            if (travelled < distance[i] * 0.9f) fellShort++;
        }

        var line = Smooth(laid, MedianStep(laid), options.SmoothingPasses, projector);

        report = new CreaseOffsetReport(
            useFirst ? 0 : 1, width, Percentile(reached, 0.05f), Percentile(reached, 0.95f), offset,
            baseLine.Length, reached.Length, clamped, fellShort,
            crossingStops, offsetStops, steadyFirst, steadySecond);

        // Refused last rather than first, so a rim this cannot cross still says why. The walks are the
        // measurement and the diagnosis both, and discarding them before they are reported leaves a
        // caller with a null and nothing to look at.
        return reached.Length < baseLine.Length * options.FewestCrossings ? null : line;
    }

    // ---------------------------------------------------------------- choosing the base

    private static (CreaseSteadiness First, CreaseSteadiness Second) Steadiness(
        Vector3[] first, Vector3[] second)
    {
        var firstContour = new RidgeContour(first, true);
        var secondContour = new RidgeContour(second, true);

        float turnFirst = Percentile(Turns(first), 0.95f);
        float turnSecond = Percentile(Turns(second), 0.95f);
        float varFirst = Variation(first, secondContour);
        float varSecond = Variation(second, firstContour);

        // Each metric as its share of the pair's total. Degrees and a dimensionless ratio cannot be
        // added directly, and converting one into the other would need a constant nothing measures;
        // shares need none and cannot let the larger unit decide the answer on its own.
        float turnTotal = turnFirst + turnSecond;
        float varTotal = varFirst + varSecond;

        static float ShareOf(float value, float total) => total < 1e-6f ? 0.5f : value / total;

        return (
            new CreaseSteadiness(turnFirst, varFirst,
                ShareOf(turnFirst, turnTotal) + ShareOf(varFirst, varTotal)),
            new CreaseSteadiness(turnSecond, varSecond,
                ShareOf(turnSecond, turnTotal) + ShareOf(varSecond, varTotal)));
    }

    private static float[] Turns(Vector3[] points)
    {
        int n = points.Length;
        var turns = new float[n];

        for (int i = 0; i < n; i++)
        {
            var incoming = points[i] - points[(i - 1 + n) % n];
            var outgoing = points[(i + 1) % n] - points[i];
            if (incoming.LengthSquared() < 1e-12f || outgoing.LengthSquared() < 1e-12f) continue;

            turns[i] = MathF.Acos(Math.Clamp(
                Vector3.Dot(Vector3.Normalize(incoming), Vector3.Normalize(outgoing)), -1f, 1f))
                * 180f / MathF.PI;
        }

        return turns;
    }

    private static float Variation(Vector3[] points, RidgeContour other)
    {
        var distances = new float[points.Length];
        for (int i = 0; i < points.Length; i++)
            distances[i] = PartingBand.Closest(points[i], other).Distance;

        Array.Sort(distances);
        float median = distances[distances.Length / 2];
        if (median < 1e-6f) return float.MaxValue;

        return (Percentile(distances, 0.90f) - Percentile(distances, 0.10f)) / median;
    }

    // ---------------------------------------------------------------- the walk

    /// <summary>The direction the crease runs in at one of its samples.</summary>
    private static Vector3 Along(Vector3[] loop, int index)
    {
        int n = loop.Length;
        var run = loop[(index + 1) % n] - loop[(index - 1 + n) % n];
        return run.LengthSquared() < 1e-12f ? Vector3.UnitX : Vector3.Normalize(run);
    }

    /// <summary>
    /// Walks one point across the wall, either a set distance or all the way to the far crease.
    ///
    /// <para>
    /// Each step goes across the surface rather than towards the far crease, and the difference is the
    /// whole of why this works. The straight line to the far crease points almost directly into the
    /// solid where the rim is rounded - at the crease of a semicircular wall it is the radius, which is
    /// square to the surface - so a step taken along it is undone in full by the projection that
    /// follows, and the point never leaves the crease it started on.
    /// </para>
    ///
    /// <para>
    /// So the step is steered two ways, and which one is in charge changes as the walk goes on. Off the
    /// crease the chord to the far side is all but square to the surface and useless as a heading, so
    /// the direction comes from the surface normal crossed with the crease's own run - square to the
    /// crease and in the surface by construction. A few steps round the rim the chord has tilted into
    /// the surface far enough to be worth following, and it takes over. That hand-over is what keeps
    /// the walk honest over a long crossing: a heading carried forward step by step accumulates every
    /// small error in it, and on the widest wall in the set that drift was enough for neighbouring
    /// samples to cross and leave the finished curve doubling back on itself.
    /// </para>
    /// </summary>
    private static (Vector3 At, float Travelled) Walk(
        Vector3 from, Vector3 along, RidgeContour far, float distance,
        float step, int maxSteps, SurfaceNormals normals, ISurfaceProjector? projector,
        out WalkStop stopped)
    {
        stopped = WalkStop.Arrived;
        if (distance <= 1e-5f) return (from, 0f);

        // How much of the chord to the far crease must lie in the surface before it is followed
        // directly. Below this it is mostly a probe into the solid, and normalizing what little of it
        // is tangential amplifies whatever noise is in the normal rather than picking a heading.
        const float UsableTangent = 0.35f;

        var at = from;
        var run = along;
        float travelled = 0f;
        float span = PartingBand.Closest(from, far).Distance;
        float closest = span;
        int sinceCloser = 0;

        for (int taken = 0; taken < maxSteps; taken++)
        {
            var toward = PartingBand.Closest(at, far).Point - at;
            float remaining = toward.Length();

            // Arrival, for a walk that was asked to cross rather than to stop short. A whole step of
            // slack, because the far crease runs between mesh vertices while the walk runs on the
            // surface: a walk stepping onto it lands one side or the other and never reads zero, and
            // asked for more precision than its own step length it circles the crease instead of
            // arriving - measured, that was 401 of larynx-large's 442 walks spending the whole step
            // budget within a millimetre of where they were trying to get to.
            if (float.IsPositiveInfinity(distance) && remaining <= step)
                return (at, travelled + remaining);

            var normal = normals.At(at);

            var tangential = toward - (Vector3.Dot(toward, normal) * normal);
            var across = tangential.Length() >= remaining * UsableTangent
                ? Vector3.Normalize(tangential)
                : Square(normal, run, toward);

            if (across == Vector3.Zero) { stopped = WalkStop.NoHeading; break; }

            float hop = MathF.Min(step, MathF.Min(remaining, distance - travelled));
            if (hop <= 1e-6f) { stopped = WalkStop.NoHeading; break; }

            var moved = at + (across * hop);
            if (projector is not null) moved = projector.Project(moved);

            // Measured rather than assumed. Projection puts back whatever the step took off the
            // surface, so what the walk actually travelled is the distance between where it was and
            // where it ended up - and a width built on the requested figure would be long by however
            // much the rim curved away underneath it.
            float actual = Vector3.Distance(at, moved);

            // A step that goes nowhere is a walk that has stopped, whatever it was aiming at. Left to
            // run it spends the whole step budget in one place and reports a width of zero.
            // Judged against the step that was asked for rather than a full one. The last step of a
            // walk that has nearly arrived is a fraction of a step by design, and measured against a
            // full one every completed walk reports as having stalled.
            if (actual < hop * 0.05f) { stopped = WalkStop.Stalled; break; }

            // The run is carried forward only as the fallback heading's reference, so a stretch where
            // the wall twists away from the crease is still stepped square to the wall rather than
            // square to where the crease used to point.
            run = Vector3.Cross(moved - at, normals.At(moved));
            if (run.LengthSquared() < 1e-12f) run = along;
            else
            {
                run = Vector3.Normalize(run);
                if (Vector3.Dot(run, along) < 0f) run = -run;
            }

            at = moved;
            travelled += actual;

            float now = PartingBand.Closest(at, far).Distance;

            // A walk that has stopped getting nearer the far crease has left the wall - it is running
            // along the band rather than across it. Nothing beyond that point is a crossing, so the
            // width it would report is a length of the rim instead of a width of it.
            if (float.IsPositiveInfinity(distance) && now > closest + step)
            {
                stopped = WalkStop.LeftTheWall;
                break;
            }

            // Judged on progress rather than on the step count alone. A walk creeping towards the far
            // crease and a walk running parallel to it both keep stepping, and only the first is
            // measuring a width; a run of steps that buys nothing separates them long before the
            // budget does, and leaves a walk that had all but crossed counted as having crossed.
            if (now < closest - (step * 0.1f)) { closest = now; sinceCloser = 0; }
            else if (++sinceCloser >= 8)
            {
                if (float.IsPositiveInfinity(distance) && closest < span * 0.25f)
                    return (at, travelled + closest);

                stopped = WalkStop.LeftTheWall;
                break;
            }

            // Nor further than the wall could possibly be wide. Straight through is the shortest route
            // between the two creases whatever the surface does between them, so a walk several times
            // that length is going somewhere else.
            if (float.IsPositiveInfinity(distance) && travelled > span * 4f)
            {
                stopped = WalkStop.LeftTheWall;
                break;
            }

            if (travelled >= distance - 1e-5f) return (at, travelled);
        }

        if (stopped == WalkStop.Arrived) stopped = WalkStop.Budget;
        return float.IsPositiveInfinity(distance) ? (at, float.PositiveInfinity) : (at, travelled);
    }

    /// <summary>The in-surface direction square to the wall's run, pointed at the far crease.</summary>
    private static Vector3 Square(Vector3 normal, Vector3 run, Vector3 toward)
    {
        var across = Vector3.Cross(normal, run);
        if (across.LengthSquared() < 1e-12f) return Vector3.Zero;

        across = Vector3.Normalize(across);
        return Vector3.Dot(across, toward) < 0f ? -across : across;
    }

    // ---------------------------------------------------------------- surface normals

    /// <summary>
    /// Face normals in a uniform grid, so the direction the surface faces can be asked at any point
    /// without an engine and without walking every triangle.
    ///
    /// <para>
    /// Averaged over the faces within a couple of edges rather than taken from the nearest one. A single
    /// face normal is a step function across the mesh, and a walk steered by one turns in facets - which
    /// arrives as exactly the kind of kink in the finished curve that this method exists to avoid.
    /// </para>
    ///
    /// <para>
    /// Averaged over all of them, and not filtered to the sheet the point is on. Filtering was tried,
    /// on the reasoning that a few millimetres of reach reaches through a bolus wall and sums two
    /// surfaces facing opposite ways: taking only the faces agreeing with the nearest one made every
    /// body worse - <c>chin</c> from 11.5% of its points off centre to 36.3% - because a rim's own
    /// facets face every which way over its width, so the nearest face flips as the walk crosses and
    /// the filtered average jumps with it. The unfiltered average is the steadier of the two.
    /// </para>
    /// </summary>
    private sealed class SurfaceNormals
    {
        private readonly Vector3[] _centroids;
        private readonly Vector3[] _normals;
        private readonly float[] _areas;
        private readonly Dictionary<(int, int, int), List<int>> _cells = new();
        private readonly float _cell;

        public float MeanEdge { get; }

        private SurfaceNormals(Vector3[] centroids, Vector3[] normals, float[] areas, float meanEdge)
        {
            _centroids = centroids;
            _normals = normals;
            _areas = areas;
            MeanEdge = meanEdge;
            _cell = MathF.Max(meanEdge * 2f, 1e-4f);

            for (int f = 0; f < centroids.Length; f++)
            {
                var key = Cell(centroids[f]);
                if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<int>(4);
                list.Add(f);
            }
        }

        public static SurfaceNormals? Build(IMesh mesh)
        {
            var vertices = mesh.Vertices;
            var triangles = mesh.Triangles;
            int faceCount = triangles.Length / 3;
            if (faceCount == 0) return null;

            var centroids = new Vector3[faceCount];
            var normals = new Vector3[faceCount];
            var areas = new float[faceCount];
            double edgeTotal = 0d;

            for (int f = 0; f < faceCount; f++)
            {
                var a = vertices[triangles[f * 3]];
                var b = vertices[triangles[(f * 3) + 1]];
                var c = vertices[triangles[(f * 3) + 2]];

                var cross = Vector3.Cross(b - a, c - a);
                float length = cross.Length();

                centroids[f] = (a + b + c) / 3f;
                areas[f] = length * 0.5f;
                normals[f] = length < 1e-12f ? Vector3.Zero : cross / length;
                edgeTotal += Vector3.Distance(a, b);
            }

            float meanEdge = (float)(edgeTotal / faceCount);
            return meanEdge < 1e-6f ? null : new SurfaceNormals(centroids, normals, areas, meanEdge);
        }

        private (int, int, int) Cell(Vector3 p) => (
            (int)MathF.Floor(p.X / _cell), (int)MathF.Floor(p.Y / _cell), (int)MathF.Floor(p.Z / _cell));

        public Vector3 At(Vector3 point)
        {
            var (cx, cy, cz) = Cell(point);
            float reach = MeanEdge * 2f;
            float reachSquared = reach * reach;

            var nearby = new List<int>(24);
            int nearest = -1;
            float nearestDistance = float.MaxValue;

            for (int x = cx - 1; x <= cx + 1; x++)
                for (int y = cy - 1; y <= cy + 1; y++)
                    for (int z = cz - 1; z <= cz + 1; z++)
                    {
                        if (!_cells.TryGetValue((x, y, z), out var faces)) continue;

                        foreach (int f in faces)
                        {
                            float d = Vector3.DistanceSquared(_centroids[f], point);
                            if (d < nearestDistance) { nearestDistance = d; nearest = f; }
                            if (d <= reachSquared) nearby.Add(f);
                        }
                    }

            if (nearest < 0) return Vector3.UnitZ;

            var sum = Vector3.Zero;
            foreach (int f in nearby)
            {
                float d = MathF.Sqrt(Vector3.DistanceSquared(_centroids[f], point));
                sum += _normals[f] * _areas[f] * (1f - (d / reach));
            }

            return sum.LengthSquared() > 1e-12f ? Vector3.Normalize(sum) : _normals[nearest];
        }
    }

    // ---------------------------------------------------------------- finishing

    /// <summary>
    /// Replaces each unmeasured crossing with a blend of the nearest measured one either side, so the
    /// profile is continuous before it is smoothed. A gap left as the median instead is a step at each
    /// of its ends, and smoothing turns a step into a ramp rather than into nothing.
    /// </summary>
    private static float[] Fill(float[] values, float fallback)
    {
        int n = values.Length;
        var filled = new float[n];

        for (int i = 0; i < n; i++)
        {
            if (float.IsFinite(values[i])) { filled[i] = values[i]; continue; }

            int back = 0, forward = 0;
            while (back < n && !float.IsFinite(values[((i - back - 1) % n + n) % n])) back++;
            while (forward < n && !float.IsFinite(values[(i + forward + 1) % n])) forward++;

            if (back >= n || forward >= n) { filled[i] = fallback; continue; }

            float before = values[((i - back - 1) % n + n) % n];
            float after = values[(i + forward + 1) % n];
            float t = (back + 1f) / (back + forward + 2f);
            filled[i] = before + ((after - before) * t);
        }

        return filled;
    }

    private static void SmoothCircular(float[] values, int passes)
    {
        int n = values.Length;
        if (n < 3 || passes <= 0) return;

        var scratch = new float[n];
        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = 0; i < n; i++)
                scratch[i] = (values[(i - 1 + n) % n] + (values[i] * 2f) + values[(i + 1) % n]) * 0.25f;
            Array.Copy(scratch, values, n);
        }
    }

    private static Vector3[] Smooth(
        Vector3[] points, float spacing, int passes, ISurfaceProjector? projector)
    {
        var work = Resample(points, spacing);
        int n = work.Length;
        if (n < 8 || passes <= 0) return work;

        var scratch = new Vector3[n];
        for (int pass = 0; pass < passes; pass++)
        {
            Sweep(work, scratch, 0.55f);
            Sweep(scratch, work, -0.58f);
            if (projector is null) continue;

            for (int i = 0; i < n; i++) work[i] = projector.Project(work[i]);
        }

        // Two neighbouring walks that crossed leave a sample doubled back on itself, and Taubin will
        // not take that out - a spike whose neighbours are both where they should be is the one shape
        // relaxation leaves alone. Rare enough after the steering was fixed that this is a guard rather
        // than a stage, and cheap enough to keep as one.
        Unkink(work, projector);
        return work;

        static void Sweep(Vector3[] source, Vector3[] destination, float factor)
        {
            int count = source.Length;
            for (int i = 0; i < count; i++)
            {
                var midpoint = (source[(i - 1 + count) % count] + source[(i + 1) % count]) * 0.5f;
                destination[i] = source[i] + (factor * (midpoint - source[i]));
            }
        }
    }

    /// <summary>
    /// Eases the samples where the curve doubles back and leaves the rest of it alone. The set is
    /// chosen once from the curve as it arrives rather than re-decided each round: re-deciding sets off
    /// a cascade in which easing one spike tips its neighbour over the threshold, and the correction
    /// walks off along the line rewriting stretches that were never kinked.
    /// </summary>
    private static void Unkink(
        Vector3[] points, ISurfaceProjector? projector, float limit = 60f, int passes = 24)
    {
        int count = points.Length;
        if (count < 8) return;

        var kinked = new bool[count];
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            if (Turn(points, i) < limit) continue;
            kinked[i] = true;
            found = true;
        }

        if (!found) return;

        for (int pass = 0; pass < passes; pass++)
        {
            bool moved = false;

            for (int i = 0; i < count; i++)
            {
                if (!kinked[i] || Turn(points, i) < limit) continue;

                var midpoint = (points[(i - 1 + count) % count] + points[(i + 1) % count]) * 0.5f;
                points[i] += (midpoint - points[i]) * 0.5f;
                if (projector is not null) points[i] = projector.Project(points[i]);
                moved = true;
            }

            if (!moved) break;
        }
    }

    private static float Turn(Vector3[] points, int index)
    {
        int count = points.Length;
        var incoming = points[index] - points[(index - 1 + count) % count];
        var outgoing = points[(index + 1) % count] - points[index];

        if (incoming.LengthSquared() < 1e-12f || outgoing.LengthSquared() < 1e-12f) return 0f;

        return MathF.Acos(Math.Clamp(
            Vector3.Dot(Vector3.Normalize(incoming), Vector3.Normalize(outgoing)), -1f, 1f))
            * 180f / MathF.PI;
    }

    private static float MedianStep(IReadOnlyList<Vector3> points)
    {
        int n = points.Count;
        if (n < 2) return 0f;

        var steps = new float[n];
        for (int i = 0; i < n; i++) steps[i] = Vector3.Distance(points[i], points[(i + 1) % n]);

        Array.Sort(steps);
        return steps[steps.Length / 2];
    }

    private static Vector3[] Resample(IReadOnlyList<Vector3> points, float spacing)
    {
        int n = points.Count;
        if (n < 4 || spacing <= 1e-4f) return points.ToArray();

        var cumulative = new float[n + 1];
        for (int i = 0; i < n; i++)
            cumulative[i + 1] = cumulative[i] + Vector3.Distance(points[i], points[(i + 1) % n]);

        float perimeter = cumulative[n];
        if (perimeter < 1e-4f) return points.ToArray();

        int count = Math.Clamp((int)MathF.Round(perimeter / spacing), 16, 20000);
        var result = new Vector3[count];

        int segment = 0;
        for (int k = 0; k < count; k++)
        {
            float target = perimeter * k / count;
            while (segment < n - 1 && cumulative[segment + 1] < target) segment++;

            float span = cumulative[segment + 1] - cumulative[segment];
            float t = span > 1e-6f ? Math.Clamp((target - cumulative[segment]) / span, 0f, 1f) : 0f;
            result[k] = Vector3.Lerp(points[segment], points[(segment + 1) % n], t);
        }

        return result;
    }

    private static float Percentile(float[] values, float fraction)
    {
        if (values.Length == 0) return 0f;

        var sorted = (float[])values.Clone();
        Array.Sort(sorted);
        return sorted[Math.Clamp((int)MathF.Round(fraction * (sorted.Length - 1)), 0, sorted.Length - 1)];
    }
}
