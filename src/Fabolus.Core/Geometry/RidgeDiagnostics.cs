using System.Numerics;

namespace Fabolus.Core.Geometry;

/// <summary>
/// What <see cref="RidgeDetection"/>'s passes did, rather than only what they returned.
///
/// <para>
/// This exists because the answer alone cannot be debugged. Every quantity worth knowing when a ridge
/// comes out wrong is destroyed inside the passes: a run rejected for being too short never reaches
/// the kept set, the pre-guard edge count is gone the moment the percolation guard clears it, the
/// loose ends and the region areas are locals, and a chain dropped for length never becomes a
/// <see cref="RidgeContour"/>. Worse, the whole analysis returns null on the two paths most worth
/// explaining - no edges, and no ridge edges - so nothing can be recovered from its result at all.
/// An all-false face array cannot distinguish "no candidate edges" from "the guard fired", and that
/// distinction is the difference between loosening a threshold and tightening one.
/// </para>
///
/// <para>
/// Everything here is <c>internal</c>: it is a diagnostic instrument, not part of the geometry API,
/// and the app never sees it. Collection is opt-in - the passes take a nullable collector and only
/// <see cref="RidgeDetection.Diagnose"/> ever supplies one - so production runs are unchanged.
/// </para>
/// </summary>
internal sealed record RidgeDiagnosis(
    bool[] RidgeFaces,
    bool[] FilledFaces,
    IReadOnlyList<RidgeContour> Contours,
    RidgeReport Report,
    RidgeBandProfileReport BandProfile,
    IReadOnlyList<RidgeEdgeAdmission> Edges,
    RidgeTerritoryReport Territories);

/// <summary>
/// What the fill cut the surface into, per face.
///
/// <para>
/// The face mask says a face is not on the ridge; it never says what it is instead, and those are
/// different findings. A patch of surface left unshaded inside a rim is either its own region that
/// failed the fill's size tests - a band the fill declined - or it is part of one of the two shell
/// surfaces, which means the creases never closed it off from them and no fill threshold could have
/// helped. Only the region id separates the two.
/// </para>
/// </summary>
internal sealed record RidgeTerritoryReport(
    bool Available, int[] FaceRegion, int First, int Second,
    bool[] RegionIsBand, int[] RegionBandGroup)
{
    public static RidgeTerritoryReport Empty { get; } = new(
        false, Array.Empty<int>(), -1, -1, Array.Empty<bool>(), Array.Empty<int>());

    /// <summary>What a region is, in one word, for a report.</summary>
    public string Role(int region) =>
        region < 0 ? "none"
        : region == First ? "surface A"
        : region == Second ? "surface B"
        : RegionIsBand[region] ? $"band {RegionBandGroup[region]}"
        : "loose";
}

/// <summary>
/// One mesh edge, and every test that stood between it and the ridge.
///
/// <para>
/// Recording the edges that were <em>refused</em> is the whole point. When relaxing a threshold repairs
/// a band, nothing in the result says what the relaxation actually admitted: the repairing edges might
/// have been below the grow level, or above it and discarded along with a connected run that had no
/// seed or was too short. Those want opposite fixes, and per-edge verdicts are the only thing that
/// tells them apart. The same record on two runs of the same mesh diffs directly - welding is
/// deterministic, so the vertex ids in <see cref="A"/> and <see cref="B"/> mean the same points in
/// both.
/// </para>
/// </summary>
/// <param name="Verdict">The fate of the connected run this edge belonged to; null if it was never a
/// candidate, so no run contained it.</param>
/// <param name="Final">Whether it is in the ridge after bridging and the percolation guard.</param>
internal sealed record RidgeEdgeAdmission(
    int A, int B, Vector3 Mid, float Length, int FaceA, int FaceB,
    float Curvature, float AngleDegrees,
    bool Candidate, bool Seed,
    RidgeRunVerdict? Verdict, int RunEdges, float RunLength,
    bool Final)
{
    public (int, int) Key => (A, B);
}

/// <summary>
/// The band's width at each of its faces, and where it falls short of the band around it.
///
/// <para>
/// This is the measurement that sees what neither curvature nor thickness can. A stretch where a
/// crease went undetected leaves the band's boundary riding up over wall that is really there: the
/// wall still measures its full thickness, the creases that were found are all genuine, and every
/// test built on either looks past it. What gives it away is that the band is suddenly a third of the
/// width it is a centimetre away on both sides.
/// </para>
/// </summary>
internal sealed record RidgeBandProfileReport(
    bool Available, float MedianWidth,
    int BandFaces, int SuspectFaces, float SuspectArea, float BandArea,
    RidgeDistribution Width,
    float[] PerFaceWidth, float[] PerFaceExpected, bool[] PerFaceSuspect,
    float[] PerFaceToFirst, float[] PerFaceToSecond)
{
    public static RidgeBandProfileReport Empty { get; } = new(
        false, 0, 0, 0, 0, 0, RidgeDistribution.Empty,
        Array.Empty<float>(), Array.Empty<float>(), Array.Empty<bool>(),
        Array.Empty<float>(), Array.Empty<float>());

    public float SuspectAreaFraction => BandArea > 1e-6f ? SuspectArea / BandArea : 0f;
}

internal sealed record RidgeReport(
    RidgeSurfaceReport Surface,
    RidgeThresholdReport Threshold,
    RidgeBridgeReport Bridging,
    RidgeFillReport Fill,
    RidgeTraceReport Trace);

/// <summary>Percentiles plus a fixed-width histogram. Values outside the range land in the end bins,
/// so the counts always add up to <see cref="Count"/>.</summary>
internal sealed record RidgeDistribution(
    int Count, float Min, float P50, float P90, float P99, float Max, float Mean,
    float BinLow, float BinWidth, IReadOnlyList<int> Histogram)
{
    public static RidgeDistribution Empty { get; } =
        new(0, 0, 0, 0, 0, 0, 0, 0, 1, Array.Empty<int>());

    public static RidgeDistribution From(IReadOnlyList<float> values, float low, float high, int bins)
    {
        if (values.Count == 0) return Empty;

        var sorted = values.ToArray();
        Array.Sort(sorted);

        float width = (high - low) / bins;
        var histogram = new int[bins];
        double total = 0d;
        foreach (float value in values)
        {
            total += value;
            int bin = (int)MathF.Floor((value - low) / width);
            histogram[Math.Clamp(bin, 0, bins - 1)]++;
        }

        return new RidgeDistribution(
            sorted.Length, sorted[0],
            Percentile(sorted, 0.50f), Percentile(sorted, 0.90f), Percentile(sorted, 0.99f),
            sorted[^1], (float)(total / sorted.Length), low, width, histogram);
    }

    private static float Percentile(float[] sorted, float fraction) =>
        sorted[Math.Clamp((int)MathF.Round(fraction * (sorted.Length - 1)), 0, sorted.Length - 1)];
}

internal sealed record RidgeSurfaceReport(
    int SourceVertices, int WeldedVertices, int Faces,
    int Edges, int InteriorEdges, int BoundaryEdges,
    float Diagonal, float TotalArea, float MeanEdgeLength,
    RidgeDistribution FoldAngleDegrees,
    RidgeDistribution Curvature)
{
    public static RidgeSurfaceReport Empty { get; } = new(
        0, 0, 0, 0, 0, 0, 0, 0, 0, RidgeDistribution.Empty, RidgeDistribution.Empty);

    /// <summary>V - E + F on the welded surface.</summary>
    public int EulerCharacteristic => WeldedVertices - Edges + Faces;

    /// <summary>
    /// How many holes run through the body. This is not a detail: it says how many closed curves the
    /// body needs to be cut open, and how many of those can be expected to separate it. A shell with a
    /// hole is a torus, so one of its two rims runs round the hole and divides nothing - reading that
    /// as a detection failure is the natural mistake, and this is what forestalls it.
    /// </summary>
    public int Genus => BoundaryEdges > 0 ? -1 : (2 - EulerCharacteristic) / 2;
}

internal enum RidgeRunVerdict { Kept, NoSeed, TooShort }

internal sealed record RidgeRunReport(
    int EdgeCount, float Length, float LengthOverDiagonal,
    bool HasSeed, int SeedEdges, RidgeRunVerdict Verdict);

internal sealed record RidgeThresholdReport(
    int CandidateEdges, int SeedEdges,
    int SeedByCurvature, int SeedByAngle, int GrowByCurvature, int GrowByAngle,
    float MinRunLength, int RunCount, IReadOnlyList<RidgeRunReport> Runs,
    int KeptEdgesBeforeGuard, float KeptEdgeFraction,
    bool PercolationGuardFired, int KeptEdges)
{
    public static RidgeThresholdReport Empty { get; } =
        new(0, 0, 0, 0, 0, 0, 0, 0, Array.Empty<RidgeRunReport>(), 0, 0, false, 0);
}

internal sealed record RidgeBridgeReport(
    bool Ran, string SkipReason, float MaxGap,
    int RidgeEdgesBefore, int RidgeEdgesAfter,
    int LooseEndsBefore, int LooseEndsAfter,
    int BridgesAdded, IReadOnlyList<float> BridgeLengths, IReadOnlyList<int> BridgeEdgeCounts)
{
    public static RidgeBridgeReport Empty { get; } = new(
        false, "not reached", 0, 0, 0, 0, 0, 0, Array.Empty<float>(), Array.Empty<int>());
}

internal sealed record RidgeRegionReport(
    int FaceCount, float Area, float AreaFraction,
    float Perimeter, float MeanWidth, float MeanWidthFraction, bool Filled);

/// <summary>
/// One candidate pocket in the band, and what became of it. Carries the rejected ones too: a pocket
/// left open is the interesting case, and the reason it was left is the only thing that says whether
/// the limit is wrong or the pocket is not the shape the closing assumes.
/// </summary>
internal sealed record RidgeHoleReport(
    int Faces, float Area, float Perimeter, float Width, bool Enclosed, bool Closed, string Verdict);

internal sealed record RidgeFillReport(
    int RegionCount, IReadOnlyList<RidgeRegionReport> Regions,
    float MaxAreaFraction, float MaxWidthFraction,
    int FilledRegions, int FilledFaces, float FilledAreaFraction,
    int BandGroups, int ClosedHoles,
    float BandWidth, float MaxHoleWidth, IReadOnlyList<RidgeHoleReport> Holes)
{
    public static RidgeFillReport Empty { get; } =
        new(0, Array.Empty<RidgeRegionReport>(), 0, 0, 0, 0, 0, 0, 0, 0, 0,
            Array.Empty<RidgeHoleReport>());
}

internal enum RidgeChainVerdict { Kept, TooShort, Degenerate }

internal sealed record RidgeChainReport(
    int MeshPoints, int ResampledPoints, float TracedLength, float TracedLengthOverDiagonal,
    bool Closed, RidgeChainVerdict Verdict);

internal sealed record RidgeTraceReport(
    int RidgeEdges, int CreaseEdges, int BuriedEdges,
    int CreaseJunctions, int CreaseLooseEnds,
    float MinContourLength, float Spacing, float Lift,
    int ChainCount, IReadOnlyList<RidgeChainReport> Chains)
{
    public static RidgeTraceReport Empty { get; } =
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, Array.Empty<RidgeChainReport>());
}

/// <summary>
/// The collector the passes write into. Constructed only by <see cref="RidgeDetection.Diagnose"/>;
/// every recording site is a <c>diag?.</c> call, so a production run allocates nothing and branches
/// once per pass.
/// </summary>
internal sealed class RidgeDiagnostics
{
    private RidgeSurfaceReport _surface = RidgeSurfaceReport.Empty;
    private RidgeThresholdReport _threshold = RidgeThresholdReport.Empty;
    private RidgeBridgeReport _bridging = RidgeBridgeReport.Empty;
    private RidgeFillReport _fill = RidgeFillReport.Empty;
    private RidgeTraceReport _trace = RidgeTraceReport.Empty;

    private readonly List<RidgeRunReport> _runs = new();
    private readonly List<RidgeChainReport> _chains = new();
    private readonly List<float> _bridgeLengths = new();
    private readonly List<int> _bridgeEdgeCounts = new();

    private float _diagonal = 1f;

    /// <summary>
    /// Whether the passes should keep a <see cref="RidgeEdgeAdmission"/> per edge. Off by default
    /// because it is a record per interior edge - tens of thousands on a real body - which is worth
    /// paying for when two runs are being diffed and not otherwise.
    /// </summary>
    public bool TracingEdges { get; init; }

    /// <summary>
    /// The per-edge record, and where each edge sits in it. Filled in by the threshold pass, which is
    /// the only place that knows why an edge was refused, and completed once the final edge set is
    /// known.
    /// </summary>
    public List<RidgeEdgeAdmission> EdgeTrace { get; } = new();

    public Dictionary<(int, int), int> EdgeTraceIndex { get; } = new();

    public void Surface(RidgeSurfaceReport report)
    {
        _surface = report;
        _diagonal = report.Diagonal > 0f ? report.Diagonal : 1f;
    }

    public void Run(int edgeCount, float length, bool hasSeed, int seedEdges, RidgeRunVerdict verdict) =>
        _runs.Add(new RidgeRunReport(edgeCount, length, length / _diagonal, hasSeed, seedEdges, verdict));

    public void Threshold(
        int candidateEdges, int seedEdges,
        int seedByCurvature, int seedByAngle, int growByCurvature, int growByAngle,
        float minRunLength, int keptBeforeGuard, int totalEdges, bool guardFired) =>
        _threshold = new RidgeThresholdReport(
            candidateEdges, seedEdges, seedByCurvature, seedByAngle, growByCurvature, growByAngle,
            minRunLength, _runs.Count, Array.Empty<RidgeRunReport>(),
            keptBeforeGuard, totalEdges > 0 ? (float)keptBeforeGuard / totalEdges : 0f,
            guardFired, guardFired ? 0 : keptBeforeGuard);

    public void BridgingSkipped(string reason, float maxGap, int ridgeEdges, int looseEnds) =>
        _bridging = new RidgeBridgeReport(
            false, reason, maxGap, ridgeEdges, ridgeEdges, looseEnds, looseEnds,
            0, Array.Empty<float>(), Array.Empty<int>());

    public void BridgingStart(float maxGap, int ridgeEdges, int looseEnds) =>
        _bridging = new RidgeBridgeReport(
            true, "", maxGap, ridgeEdges, ridgeEdges, looseEnds, looseEnds,
            0, Array.Empty<float>(), Array.Empty<int>());

    public void Bridge(int edgesAdded, float length)
    {
        _bridgeEdgeCounts.Add(edgesAdded);
        _bridgeLengths.Add(length);
    }

    public void BridgingDone(int ridgeEdges, int looseEnds) =>
        _bridging = _bridging with
        {
            RidgeEdgesAfter = ridgeEdges,
            LooseEndsAfter = looseEnds,
            BridgesAdded = _bridgeLengths.Count,
        };

    public void Fill(RidgeFillReport report) => _fill = report;

    public void Trace(RidgeTraceReport report) => _trace = report;

    public void Chain(int meshPoints, int resampledPoints, float length, bool closed, RidgeChainVerdict verdict) =>
        _chains.Add(new RidgeChainReport(
            meshPoints, resampledPoints, length, length / _diagonal, closed, verdict));

    /// <summary>
    /// Freezes the report. Lists are sorted by size rather than left in collection order: the passes
    /// walk a <see cref="Dictionary{TKey, TValue}"/>, so run, bridge and chain ordering is not stable
    /// across runs and an unsorted report would diff against itself.
    /// </summary>
    public RidgeReport Build() => new(
        _surface,
        _threshold with { Runs = _runs.OrderByDescending(r => r.Length).ToList() },
        _bridging with
        {
            BridgeLengths = _bridgeLengths.OrderByDescending(l => l).ToList(),
            BridgeEdgeCounts = _bridgeEdgeCounts.OrderByDescending(c => c).ToList(),
        },
        _fill,
        _trace with
        {
            ChainCount = _chains.Count,
            Chains = _chains.OrderByDescending(c => c.TracedLength).ToList(),
        });
}
