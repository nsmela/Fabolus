using System.Numerics;

namespace Fabolus.Core.Geometry;

/// <summary>
/// The 3D loop(s) that mark where a mould should be divided along a given pull direction.
/// A mould with an internal hole (e.g. a tunnel/channel) produces one loop for the outer
/// silhouette plus one additional loop per hole - each of those extra loops is a signal that
/// an additional parting (shut-off) surface is required to separate the mould cleanly there.
/// </summary>
public sealed record PartingLine
{
    /// <summary>
    /// Each entry is one closed loop of ordered 3D points. The loop with the largest projected
    /// area (relative to the pull direction) is the outer silhouette; every other loop marks an
    /// internal hole.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Vector3>> Loops { get; }

    public PartingLine(IEnumerable<IEnumerable<Vector3>> loops)
    {
        Loops = loops.Select(l => (IReadOnlyList<Vector3>)l.ToList()).ToList();
    }

    public static PartingLine Empty { get; } = new(Array.Empty<Vector3[]>());

    public bool IsValid => Loops.Count > 0 && Loops.All(l => l.Count > 2);

    /// <summary>
    /// Number of loops beyond the outer silhouette - i.e. how many internal holes were
    /// detected and will need their own shut-off parting surface.
    /// </summary>
    public int InternalHoleCount => Math.Max(0, Loops.Count - 1);
}


/// <summary>
/// Smooths the closed loops of a <see cref="PartingLine"/>. The raw loops come out of the
/// marching-triangles isoline pass zig-zagging from one triangle edge-crossing to the next, so
/// they read as faceted; this relaxes that high-frequency noise into a cleaner curve.
///
/// <para>
/// Uses Taubin's λ|μ scheme rather than a plain Laplacian: a Laplacian pass alone would pull every
/// point toward the loop's centroid and steadily shrink it, whereas alternating a positive (λ)
/// shrinking pass with a negative (μ) inflating pass smooths without net shrinkage - important here
/// because the loop is meant to sit on the silhouette, not collapse inward.
/// </para>
///
/// <para>
/// What the flange actually needs is a clean <em>footprint</em> - the loop seen looking along the
/// pull direction - because that is what the wavefront ribbons are offset from and what the mould
/// halves have to slide apart across. A plain 3D Taubin pass doesn't deliver one: on anatomy where
/// the silhouette plunges (the jaw corners of chin.3mf) the footprint doubles back on itself into
/// hooks that the 3D smoothing barely touches, since most of the offending edge length is height,
/// not footprint. So this smoother works on the footprint specifically:
/// </para>
/// <list type="bullet">
///   <item>extra Taubin pairs applied to the in-plane components only, so footprint wiggle is
///     relaxed far harder than the height it is bundled with;</item>
///   <item>an explicit de-looping pass that excises footprint self-intersections outright - no
///     amount of relaxation removes a crossing, it has to be cut;</item>
///   <item>an optional snap back onto the surface the loop was traced on (see the
///     <paramref name="snapToSurface"/> overload), so smoothing never lifts a point off the mesh.</item>
/// </list>
///
/// <para>
/// Pure geometry apart from that one callback: callers without a surface to snap to still get the
/// footprint work, they just accept a few tenths of a millimetre of drift.
/// </para>
/// </summary>
public static class PartingLineSmoother {
    /// <summary>Number of Taubin (λ then μ) iterations at full strength.</summary>
    private const int MaxIterations = 50;

    /// <summary>Shrinking-pass factor.</summary>
    private const float Lambda = 0.5f;

    /// <summary>Inflating-pass factor, tuned so the two passes roughly cancel net shrinkage.</summary>
    private const float Mu = -0.53f;

    /// <summary>Loops shorter than this can't be meaningfully smoothed and are passed through as-is.</summary>
    private const int MinLoopPoints = 4;

    /// <summary>
    /// Footprint-only Taubin pairs run per iteration, on top of the single full-3D pair. Three is
    /// what it takes to flatten the footprint hooks on chin.3mf without over-relaxing the height:
    /// the 3D pair alone leaves in-plane turns past 100 degrees, three extra in-plane pairs bring the
    /// worst turn under 45 while the pull-axis extent of the loop is unchanged to within 0.5mm.
    /// </summary>
    private const int FootprintPairsPerIteration = 3;

    public static PartingLine Smooth(PartingLine line, PartingLineSmoothingOptions options)
        => Smooth(line, options, DefaultPullDirection, snapToSurface: null);

    public static PartingLine Smooth(PartingLine line, double strength)
        => Smooth(line, strength, PartingLineSmoothingOptions.DefaultSpacingMm);

    public static PartingLine Smooth(PartingLine line, double strength, float spacingMm)
        => Smooth(line, strength, spacingMm, DefaultPullDirection, snapToSurface: null);

    /// <summary>
    /// The pull direction assumed by the overloads that don't take one. The parting-split feature is
    /// +Y-only today (see PartingMeshParameters.Axis), so the footprint is the world XZ plane.
    /// </summary>
    private static Vector3 DefaultPullDirection => Vector3.UnitY;

    /// <summary>
    /// Returns a new <see cref="PartingLine"/> with each loop resampled to a uniform spacing,
    /// Taubin-smoothed with an in-plane bias, and de-looped in the footprint.
    /// </summary>
    /// <param name="pullDirection">
    /// The axis the halves separate along. The plane perpendicular to it is the footprint that the
    /// extra smoothing passes and the de-looping act on.
    /// </param>
    /// <param name="snapToSurface">
    /// Maps a point to the closest point on the surface the loop was traced from, applied after every
    /// iteration. Supplied by the geometry engine (see IPartingTools.SmoothPartingLineOnSurface);
    /// null leaves the loop free to drift off the surface.
    /// </param>
    public static PartingLine Smooth(
        PartingLine line,
        PartingLineSmoothingOptions options,
        Vector3 pullDirection,
        Func<Vector3, Vector3>? snapToSurface)
        => Smooth(line,
                  options?.Strength ?? PartingLineSmoothingOptions.DefaultStrength,
                  options?.SpacingMm ?? PartingLineSmoothingOptions.DefaultSpacingMm,
                  pullDirection,
                  snapToSurface);

    /// <summary>
    /// Loop count and winding order are preserved; the per-loop <em>point</em> count changes when
    /// <paramref name="spacingMm"/> resampling is active, and again wherever de-looping excises a
    /// crossing. That's safe for the downstream outer-vs-hole classification, which keys off projected
    /// loop area rather than point count.
    /// </summary>
    public static PartingLine Smooth(
        PartingLine line,
        double strength,
        float spacingMm,
        Vector3 pullDirection,
        Func<Vector3, Vector3>? snapToSurface) {
        if (line is null || line.Loops.Count == 0) return PartingLine.Empty;
        if (pullDirection == Vector3.Zero) pullDirection = DefaultPullDirection;

        int iterations = IterationsFor(strength);
        bool resample = spacingMm > 1e-4f;
        if (iterations <= 0 && !resample) return line; // nothing to do -> raw loops, untouched.

        var (u, v) = FootprintFrame(pullDirection);

        var smoothedLoops = new List<Vector3[]>(line.Loops.Count);
        foreach (var loop in line.Loops) {
            smoothedLoops.Add(SmoothLoop(loop, iterations, spacingMm, u, v, snapToSurface));
        }

        return new PartingLine(smoothedLoops);
    }

    private static int IterationsFor(double strength) {
        double clamped = Math.Clamp(strength, 0.0, 1.0);
        return (int)Math.Round(clamped * MaxIterations);
    }

    /// <summary>Any orthonormal pair spanning the plane perpendicular to <paramref name="pullDirection"/>.</summary>
    private static (Vector3 U, Vector3 V) FootprintFrame(Vector3 pullDirection) {
        var d = Vector3.Normalize(pullDirection);
        var seed = MathF.Abs(d.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(seed, d));
        return (u, Vector3.Cross(d, u));
    }

    private static Vector2 Footprint(Vector3 p, Vector3 u, Vector3 v) =>
        new(Vector3.Dot(p, u), Vector3.Dot(p, v));

    private static Vector3[] SmoothLoop(
        IReadOnlyList<Vector3> loop, int iterations, float spacingMm,
        Vector3 u, Vector3 v, Func<Vector3, Vector3>? snapToSurface) {
        // 1. Uniform arc-length resample. The marching-triangles isoline occasionally wobbles back and
        // forth across a cluster of near-coincident points - a sub-millimetre "needle" that reads as a
        // sharp spike in an otherwise smooth curve. Taubin smoothing alone can't collapse such a hairpin
        // because the Laplacian midpoint of two alternating near-coincident neighbours lands right on
        // top of the vertex, so it barely moves. Resampling to a spacing coarser than the wobble merges
        // the cluster into a single point, erasing the reversal before any smoothing runs.
        Vector3[] work = spacingMm > 1e-4f ? ResampleUniform(loop, spacingMm) : loop.ToArray();

        // 2. Despike guard. Belt-and-suspenders for any reversal that survives (or when resampling is
        // disabled): drop vertices whose incoming/outgoing edges reverse by more than DespikeAngle.
        work = Despike(work);
        work = DeloopFootprint(work, u, v);

        if (work.Length < MinLoopPoints || iterations <= 0) return work; // Nothing to relax on short loops.

        // 3. Taubin lambda|mu iterations. The in-plane pairs run on top of the 3D one rather than
        // instead of it: dropping the 3D pair leaves the pull-axis height completely unrelaxed, which
        // reintroduces the sharp 3D turns (and the steep flange faces that follow from them) even
        // though the footprint itself reads as clean.
        var buffer = new Vector3[work.Length];
        for (int pass = 0; pass < iterations; pass++) {
            LaplacianPair(ref work, ref buffer, Lambda, Mu);

            for (int k = 0; k < FootprintPairsPerIteration; k++)
                FootprintLaplacianPair(ref work, ref buffer, u, v, Lambda, Mu);

            // De-looping changes the point count, so the scratch buffer has to be re-sized with it.
            int before = work.Length;
            work = DeloopFootprint(work, u, v);
            if (work.Length != before) buffer = new Vector3[work.Length];

            Snap(work, snapToSurface);
        }

        work = Despike(work);
        work = DeloopFootprint(work, u, v);
        Snap(work, snapToSurface);
        return work;
    }

    private static void Snap(Vector3[] pts, Func<Vector3, Vector3>? snapToSurface) {
        if (snapToSurface is null) return;
        for (int i = 0; i < pts.Length; i++) pts[i] = snapToSurface(pts[i]);
    }

    /// <summary>
    /// Resamples a closed loop to <paramref name="spacingMm"/> uniform 3D arc-length spacing. Merges
    /// sub-spacing wobble (needles) into single points. Orientation-agnostic: uses full 3D distance,
    /// so it needs no knowledge of the pull direction.
    /// </summary>
    private static Vector3[] ResampleUniform(IReadOnlyList<Vector3> loop, float spacingMm) {
        int n = loop.Count;
        if (n < MinLoopPoints) return loop.ToArray();

        var cum = new float[n + 1];
        for (int i = 0; i < n; i++)
            cum[i + 1] = cum[i] + Vector3.Distance(loop[i], loop[(i + 1) % n]);

        float perim = cum[n];
        if (perim < 1e-4f) return loop.ToArray();

        int count = Math.Clamp((int)MathF.Round(perim / spacingMm), 16, 4000);
        var resampled = new Vector3[count];
        int seg = 0;
        for (int k = 0; k < count; k++) {
            float target = perim * k / count;
            while (seg < n - 1 && cum[seg + 1] < target) seg++;
            float segLen = cum[seg + 1] - cum[seg];
            float t = segLen > 1e-6f ? (target - cum[seg]) / segLen : 0f;
            resampled[k] = Vector3.Lerp(loop[seg], loop[(seg + 1) % n], t);
        }
        return resampled;
    }

    /// <summary>Angle (deg) past straight beyond which a vertex is treated as a reversal spike.</summary>
    private const float DespikeAngle = 120f;

    /// <summary>
    /// Removes vertices that form a near-reversal (turn angle &gt; <see cref="DespikeAngle"/>) with their
    /// neighbours, repeating until none remain (bounded passes). Never removes two adjacent vertices in
    /// the same pass, and never shrinks the loop below <see cref="MinLoopPoints"/>, so a genuinely tight
    /// but valid corner is thinned rather than erased.
    /// </summary>
    private static Vector3[] Despike(Vector3[] loop, int maxPasses = 4) {
        float cosLimit = MathF.Cos(DespikeAngle * MathF.PI / 180f); // turn > 120deg => dir dot < -0.5

        var pts = loop;
        for (int pass = 0; pass < maxPasses; pass++) {
            int n = pts.Length;
            if (n <= MinLoopPoints) break;

            var keep = new bool[n];
            for (int i = 0; i < n; i++) keep[i] = true;

            bool prevRemoved = false;
            int removed = 0;
            for (int i = 0; i < n; i++) {
                if (prevRemoved) { prevRemoved = false; continue; } // don't drop adjacent pair together

                var e0 = pts[i] - pts[(i - 1 + n) % n];
                var e1 = pts[(i + 1) % n] - pts[i];
                float l0 = e0.Length(), l1 = e1.Length();
                if (l0 < 1e-6f || l1 < 1e-6f) continue;

                if (Vector3.Dot(e0 / l0, e1 / l1) < cosLimit) {
                    keep[i] = false;
                    prevRemoved = true;
                    if (++removed >= n - MinLoopPoints) break;
                }
            }

            if (removed == 0) break;

            var next = new Vector3[n - removed];
            int w = 0;
            for (int i = 0; i < n; i++)
                if (keep[i]) next[w++] = pts[i];
            pts = next;
        }
        return pts;
    }

    /// <summary>
    /// A full Taubin λ-then-μ pair in 3D. <paramref name="work"/> ends up holding the result and
    /// <paramref name="scratch"/> the discarded intermediate - two passes means two swaps, so the
    /// caller's references come back the way round they went in.
    /// </summary>
    private static void LaplacianPair(ref Vector3[] work, ref Vector3[] scratch, float lambda, float mu) {
        LaplacianPassInPlace(work, scratch, lambda);
        Swap(ref work, ref scratch);
        LaplacianPassInPlace(work, scratch, mu);
        Swap(ref work, ref scratch);
    }

    /// <summary>As <see cref="LaplacianPair"/>, but relaxing the in-plane components only.</summary>
    private static void FootprintLaplacianPair(
        ref Vector3[] work, ref Vector3[] scratch, Vector3 u, Vector3 v, float lambda, float mu) {
        FootprintPassInPlace(work, scratch, u, v, lambda);
        Swap(ref work, ref scratch);
        FootprintPassInPlace(work, scratch, u, v, mu);
        Swap(ref work, ref scratch);
    }

    /// <summary>
    /// Executes a Laplacian pass reading from <paramref name="source"/> and writing directly
    /// into <paramref name="destination"/> without allocating memory on the managed heap.
    /// </summary>
    private static void LaplacianPassInPlace(Vector3[] source, Vector3[] destination, float factor) {
        int n = source.Length;
        for (int i = 0; i < n; i++) {
            var prev = source[(i - 1 + n) % n];
            var next = source[(i + 1) % n];
            var midpoint = (prev + next) * 0.5f;
            destination[i] = source[i] + factor * (midpoint - source[i]);
        }
    }

    /// <summary>
    /// A Laplacian pass whose displacement is confined to the footprint plane: the height along the
    /// pull axis is carried through untouched, so this relaxes in-plane wiggle without flattening the
    /// undulation the flange has to follow.
    /// </summary>
    private static void FootprintPassInPlace(
        Vector3[] source, Vector3[] destination, Vector3 u, Vector3 v, float factor) {
        int n = source.Length;
        for (int i = 0; i < n; i++) {
            var prev = Footprint(source[(i - 1 + n) % n], u, v);
            var next = Footprint(source[(i + 1) % n], u, v);
            var here = Footprint(source[i], u, v);

            var delta = (((prev + next) * 0.5f) - here) * factor;
            destination[i] = source[i] + (u * delta.X) + (v * delta.Y);
        }
    }

    private static void Swap(ref Vector3[] a, ref Vector3[] b) {
        var temp = a;
        a = b;
        b = temp;
    }

    /// <summary>
    /// Removes self-intersections of the loop's <em>footprint</em> - the projection onto the plane
    /// perpendicular to the pull direction. Where the silhouette plunges, the isoline walks out along
    /// a ridge and back, and seen from the pull direction that excursion crosses itself: a hook that
    /// the wavefront flange then has to offset outward from on both sides at once. Relaxation can
    /// never remove a crossing, only soften it, so the excursion is cut out instead - the shorter of
    /// the two arcs the crossing divides the loop into is dropped, and the loop rejoined at the
    /// crossing point itself.
    ///
    /// <para>
    /// Always the shorter arc, so the loop's overall course is never the part discarded. The removed
    /// span is off the surface for the moment it takes to rejoin; the caller's surface snap puts the
    /// join back on it.
    /// </para>
    /// </summary>
    private static Vector3[] DeloopFootprint(Vector3[] loop, Vector3 u, Vector3 v, int maxPasses = 12) {
        var pts = loop;

        for (int pass = 0; pass < maxPasses; pass++) {
            int n = pts.Length;
            if (n < MinDeloopPoints) break;

            var flat = new Vector2[n];
            for (int i = 0; i < n; i++) flat[i] = Footprint(pts[i], u, v);

            var arc = new float[n + 1];
            for (int i = 0; i < n; i++) arc[i + 1] = arc[i] + Vector2.Distance(flat[i], flat[(i + 1) % n]);
            float total = arc[n];

            var cut = FindCrossing(flat, arc, total, pts);
            if (cut is null) break;

            pts = cut;
        }

        return pts;
    }

    /// <summary>
    /// Below this a loop has too few points left for an excision to leave anything meaningful, so
    /// de-looping stops rather than eat the last of it.
    /// </summary>
    private const int MinDeloopPoints = 6;

    /// <summary>
    /// Finds the first footprint self-crossing and returns the loop with the shorter arc excised, or
    /// null when the footprint is already simple.
    /// </summary>
    private static Vector3[]? FindCrossing(Vector2[] flat, float[] arc, float total, Vector3[] pts) {
        int n = flat.Length;

        for (int i = 0; i < n; i++) {
            for (int j = i + 2; j < n; j++) {
                // i and j are adjacent around the seam, so they share a point rather than cross.
                if (i == 0 && j == n - 1) continue;

                if (!SegmentsCross(flat[i], flat[(i + 1) % n], flat[j], flat[(j + 1) % n], out float t))
                    continue;

                // The crossing divides the loop into the arc from i+1 to j and everything else.
                float inner = arc[j + 1] - arc[i + 1];
                var join = Vector3.Lerp(pts[i], pts[(i + 1) % n], t);

                var kept = new List<Vector3>(n);
                if (inner <= total - inner) {
                    for (int k = 0; k <= i; k++) kept.Add(pts[k]);
                    kept.Add(join);
                    for (int k = j + 1; k < n; k++) kept.Add(pts[k]);
                } else {
                    kept.Add(join);
                    for (int k = i + 1; k <= j; k++) kept.Add(pts[k]);
                }

                if (kept.Count < MinDeloopPoints) continue;
                return kept.ToArray();
            }
        }

        return null;
    }

    /// <summary>
    /// True when the two segments cross properly (not merely touch at an endpoint), with
    /// <paramref name="t"/> the crossing's parameter along <paramref name="a0"/>-<paramref name="a1"/>.
    /// </summary>
    private static bool SegmentsCross(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1, out float t) {
        t = 0f;

        var r = a1 - a0;
        var s = b1 - b0;
        float denominator = Cross(r, s);
        if (MathF.Abs(denominator) < 1e-12f) return false; // parallel or degenerate

        float onA = Cross(b0 - a0, s) / denominator;
        float onB = Cross(b0 - a0, r) / denominator;
        if (onA <= 0f || onA >= 1f || onB <= 0f || onB >= 1f) return false;

        t = onA;
        return true;

        static float Cross(Vector2 p, Vector2 q) => (p.X * q.Y) - (p.Y * q.X);
    }

}

/// <summary>
/// Options controlling how aggressively a <see cref="PartingLine"/> is smoothed.
/// </summary>
public sealed record PartingLineSmoothingOptions
{
    /// <summary>The auto/default strength applied when the user hasn't overridden it.</summary>
    public const double DefaultStrength = 0.5;

    /// <summary>
    /// Default uniform resample spacing (mm) applied before smoothing. Coarser than the sub-millimetre
    /// isoline wobble that produces spikes, fine enough to preserve the true silhouette shape.
    ///
    /// <para>
    /// 2mm rather than the 1.2mm this started at: the footprint smoothing works vertex-to-vertex, so
    /// spacing sets the wavelength it can flatten, and at 1.2mm the chin's jaw hooks survive as ~70
    /// degree in-plane turns. 2mm clears them (worst turn under 45 degrees on both chin.3mf and
    /// scalp.3mf) while enclosed footprint area is unchanged to well under 0.2%. Going coarser starts
    /// costing real anatomy - at 2.5mm the chin's jaw notch is under-sampled and the worst turn climbs
    /// back to 80 degrees.
    /// </para>
    /// </summary>
    public const float DefaultSpacingMm = 2.0f;

    /// <summary>
    /// How much to smooth, in [0, 1]. 0 leaves the loops un-Taubin'd (they are still resampled unless
    /// <see cref="SpacingMm"/> is also 0); 1 applies the maximum number of smoothing passes. Values
    /// outside the range are clamped.
    /// </summary>
    public double Strength { get; init; } = DefaultStrength;

    /// <summary>
    /// Uniform arc-length spacing (mm) the loop is resampled to before smoothing, which collapses the
    /// marching-triangles needle spikes that Taubin smoothing cannot. 0 (or negative) disables
    /// resampling and leaves the point count untouched.
    /// </summary>
    public float SpacingMm { get; init; } = DefaultSpacingMm;

    public static PartingLineSmoothingOptions Default { get; } = new();
    public static PartingLineSmoothingOptions None { get; } = new() { Strength = 0.0, SpacingMm = 0f };
}

/// <summary>
/// Options controlling the rejection of redundant, closely-spaced shadow contours.
/// </summary>
public sealed record PartingLineFilterOptions {
    /// <summary>
    /// The minimum allowable clearance (in mm) between two distinct loops. 
    /// Loops closer than this distance will be evaluated for redundancy.
    /// </summary>
    public float MinimumClearance { get; init; } = 1.5f;

    /// <summary>
    /// The fraction of a candidate loop's vertices [0, 1] that must fall within 
    /// <see cref="MinimumClearance"/> of a dominant loop to be considered a shadow contour.
    /// </summary>
    public float ShadowOverlapRatio { get; init; } = 0.60f;

    public static PartingLineFilterOptions Default { get; } = new();
}

/// <summary>
/// Pure geometry domain filter that prunes redundant, closely-spaced isoline loops.
/// Prevents tooling generation failures caused by parallel "shadow contours" extracted 
/// across shallow draft angles or noisy surface meshes.
/// </summary>
public static class PartingLineProximityFilter {
    public static PartingLine PruneShadowLoops(PartingLine line, PartingLineFilterOptions options) {
        if (line is null || line.Loops.Count <= 1) return line ?? PartingLine.Empty;

        float clearanceSq = options.MinimumClearance * options.MinimumClearance;

        // 1. Rank loops by dominance (Length is most reliable for 3D contour importance)
        var rankedLoops = line.Loops
            .Select(loop => new LoopCandidate(loop))
            .OrderByDescending(c => c.Length)
            .ToList();

        var acceptedLoops = new List<Vector3[]>(rankedLoops.Count);
        var acceptedBoxes = new List<BoundingBox3D>(rankedLoops.Count);

        // 2. Greedy selection: Keep dominant loops, reject close shadows
        foreach (var candidate in rankedLoops) {
            if (IsShadowOfAcceptedLoop(candidate, acceptedLoops, acceptedBoxes, clearanceSq, options.ShadowOverlapRatio)) {
                continue; // Skip this curve entirely
            }

            acceptedLoops.Add(candidate.Points);
            acceptedBoxes.Add(candidate.Bounds);
        }

        return new PartingLine(acceptedLoops);
    }

    private static bool IsShadowOfAcceptedLoop(
        LoopCandidate candidate,
        IReadOnlyList<Vector3[]> acceptedLoops,
        IReadOnlyList<BoundingBox3D> acceptedBoxes,
        float clearanceSq,
        float overlapRatioThreshold) {
        for (int i = 0; i < acceptedLoops.Count; i++) {
            // Fast $O(1)$ Rejection: If bounding boxes don't overlap, loops aren't close
            if (!candidate.Bounds.IntersectsWithTolerance(acceptedBoxes[i], clearanceSq))
                continue;

            var dominantLoop = acceptedLoops[i];
            int pointsWithinClearance = 0;

            // Measure how much of the candidate loop is swallowed by the dominant loop's clearance zone
            for (int j = 0; j < candidate.Points.Length; j++) {
                if (IsPointNearLoop(candidate.Points[j], dominantLoop, clearanceSq)) {
                    pointsWithinClearance++;
                }
            }

            float ratio = (float)pointsWithinClearance / candidate.Points.Length;
            if (ratio >= overlapRatioThreshold) {
                return true; // Classified as a redundant shadow contour
            }
        }

        return false;
    }

    private static bool IsPointNearLoop(Vector3 point, Vector3[] loop, float clearanceSq) {
        // For production performance on massive loops, replace this linear scan 
        // with a spatial grid lookup similar to our discussed IsolineGraph optimization.
        for (int i = 0; i < loop.Length; i++) {
            if (Vector3.DistanceSquared(point, loop[i]) <= clearanceSq)
                return true;
        }
        return false;
    }

    // --- Lightweight Domain Value Objects ---

    private readonly struct LoopCandidate {
        public Vector3[] Points { get; }
        public float Length { get; }
        public BoundingBox3D Bounds { get; }

        public LoopCandidate(IReadOnlyList<Vector3> loop) {
            int n = loop.Count;
            Points = new Vector3[n];

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            float len = 0f;

            for (int i = 0; i < n; i++) {
                var p = loop[i];
                Points[i] = p;

                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);

                if (i > 0) len += Vector3.Distance(loop[i - 1], p);
            }
            if (n > 1) len += Vector3.Distance(loop[n - 1], loop[0]); // Close loop

            Length = len;
            Bounds = new BoundingBox3D(min, max);
        }
    }

    private readonly struct BoundingBox3D {
        public Vector3 Min { get; }
        public Vector3 Max { get; }

        public BoundingBox3D(Vector3 min, Vector3 max) {
            Min = min;
            Max = max;
        }

        public bool IntersectsWithTolerance(BoundingBox3D other, float toleranceSq) {
            // Approximate tolerance expansion using square root of clearanceSq for box bounds
            float tol = (float)Math.Sqrt(toleranceSq);
            return (Min.X - tol <= other.Max.X && Max.X + tol >= other.Min.X) &&
                   (Min.Y - tol <= other.Max.Y && Max.Y + tol >= other.Min.Y) &&
                   (Min.Z - tol <= other.Max.Z && Max.Z + tol >= other.Min.Z);
        }
    }
}

/// <summary>
/// Options controlling the amputation of intra-loop self-proximities (peninsulas and pinch points).
/// </summary>
public sealed record PartingLinePinchOptions {
    /// <summary>
    /// The physical 3D distance (in mm) under which two non-adjacent sections of the same 
    /// loop are considered to be pinching or colliding.
    /// </summary>
    public float PinchClearance { get; init; } = 1.2f;

    /// <summary>
    /// The minimum topological distance (arc length in mm) along the loop required before a 
    /// spatial proximity is treated as a bypassable peninsula. Prevents the filter from 
    /// accidentally short-circuiting normal, valid tight corners.
    /// </summary>
    public float MinPeninsulaLength { get; init; } = 6.0f;

    public static PartingLinePinchOptions Default { get; } = new();
}

/// <summary>
/// Pure geometry domain filter that eliminates intra-loop pinch points and narrow peninsulas.
/// When a contour doubles back close to itself, this service short-circuits the gap,
/// snipping off the minor loop excursion while preserving the dominant mold silhouette.
/// </summary>
public static class PartingLinePinchFilter {
    public static PartingLine AmputatePinches(PartingLine line, PartingLinePinchOptions options) {
        if (line is null || line.Loops.Count == 0) return line ?? PartingLine.Empty;

        var cleanedLoops = new List<Vector3[]>(line.Loops.Count);
        foreach (var loop in line.Loops) {
            cleanedLoops.Add(AmputateLoopPinches(loop, options));
        }

        return new PartingLine(cleanedLoops);
    }

    private static Vector3[] AmputateLoopPinches(IReadOnlyList<Vector3> loop, PartingLinePinchOptions options) {
        int n = loop.Count;
        if (n < 6) return loop.ToArray(); // Too small to contain a meaningful peninsula

        float clearanceSq = options.PinchClearance * options.PinchClearance;

        // 1. Precompute cumulative arc lengths for O(1) topological distance queries
        var (arcLengths, totalLength) = ComputeArcLengths(loop);

        // If the entire loop is smaller than our peninsula threshold, we cannot snip it
        if (totalLength <= options.MinPeninsulaLength * 2f) return loop.ToArray();

        var result = new List<Vector3>(n);
        int curr = 0;

        // 2. Greedy traversal: walk the loop and short-circuit across any pinches
        while (curr < n) {
            result.Add(loop[curr]);

            int bestJumpTarget = -1;
            float maxPeninsulaBypassed = 0f;

            // Search ahead for the furthest valid pinch target that removes a minor peninsula
            for (int target = curr + 1; target < n; target++) {
                // Calculate topological distance (forward arc length along the loop)
                float forwardArc = arcLengths[target] - arcLengths[curr];

                // INVARIANT 1: Must be topologically distant (exceeds minimum peninsula length)
                if (forwardArc < options.MinPeninsulaLength) continue;

                // INVARIANT 2: Must be a MINOR excursion (we never snip > 50% of the loop's total body)
                if (forwardArc >= totalLength * 0.5f) break;

                // INVARIANT 3: Must be spatially close (Euclidean distance < PinchClearance)
                if (Vector3.DistanceSquared(loop[curr], loop[target]) <= clearanceSq) {
                    // We found a pinch! Keep looking to see if we can jump even further 
                    // across the bottleneck to remove the entire peninsula cleanly.
                    if (forwardArc > maxPeninsulaBypassed) {
                        maxPeninsulaBypassed = forwardArc;
                        bestJumpTarget = target;
                    }
                }
            }

            if (bestJumpTarget != -1) {
                // SHORT-CIRCUIT: Jump directly to the target, amputating the peninsula!
                curr = bestJumpTarget;
            } else {
                curr++;
            }
        }

        return result.ToArray();
    }

    private static (float[] ArcLengths, float TotalLength) ComputeArcLengths(IReadOnlyList<Vector3> loop) {
        int n = loop.Count;
        var lengths = new float[n + 1];
        float total = 0f;

        for (int i = 1; i < n; i++) {
            total += Vector3.Distance(loop[i - 1], loop[i]);
            lengths[i] = total;
        }

        // Close the loop distance
        total += Vector3.Distance(loop[n - 1], loop[0]);
        lengths[n] = total;

        return (lengths, total);
    }
}