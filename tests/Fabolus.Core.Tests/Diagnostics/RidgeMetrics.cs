using Fabolus.Core.Geometry;

namespace Fabolus.Tests.Diagnostics;

internal sealed record ContourMetrics(
    int Index, int Points, bool Closed, double Length, double LengthOverDiagonal,
    double WorstTurnDegrees, double MinSelfClearance, double MinSelfClearanceOverDiagonal,
    double TurningNumber,
    double SeamMeanDistance, double SeamP95Distance, double SeamMaxDistance);

/// <summary>
/// How much of the surface a single closed contour cuts off, and into how many pieces.
///
/// <para>
/// <see cref="Separates"/> is the distinction that matters on a body with a hole. A closed curve round
/// the hole of a torus divides nothing - flooding either side of it reaches the whole surface - and
/// that is correct behaviour for the rim of the hole, not a failed detection. Only a curve that leaves
/// two substantial pieces is a candidate for a parting line.
/// </para>
/// </summary>
internal sealed record ContourSeparation(
    int ContourIndex, int Components, double LargestShare, double SecondShare)
{
    public bool Separates => Components >= 2 && SecondShare >= 0.05;
}

/// <summary>
/// How wide the rim band is along one of the two contours that bound it, sampled at every point.
///
/// <para>
/// A rim wall is a shell of roughly constant thickness swept round the piece, so its width should
/// barely vary. That makes width the measurement that catches what neither curvature nor thickness
/// can: a hole in the band, or a stretch where the body's own rim is irregular, both show as a local
/// excursion in a quantity that is otherwise flat. <see cref="CoefficientOfVariation"/> is the whole
/// story in one number; the per-point array is what says where.
/// </para>
///
/// <para>
/// Width is measured to the partner contour rather than across the filled faces, because the contours
/// are resampled evenly and sit where the rim actually is, while the faces are whatever the
/// tessellation happened to produce.
/// </para>
/// </summary>
internal sealed record BandWidth(
    int ContourIndex, int PartnerIndex, bool Paired,
    double Median, double Mean, double StandardDeviation,
    double Minimum, double Maximum, double P5, double P95,
    double CoefficientOfVariation,
    int OutlierPoints, double OutlierFraction, double OutlierThresholdLow, double OutlierThresholdHigh,
    double[] PerPoint,
    double[] LocalThickness,
    int ThicknessSamples, double WidthThicknessCorrelation, double MedianWidthOverThickness,
    double NormalMedianWidth, double NormalMedianThickness,
    double OutlierMedianWidth, double OutlierMedianThickness)
{
    /// <summary>
    /// Whether the width variation is explained by the shell itself varying in thickness.
    ///
    /// <para>
    /// This is what separates a finding from an artifact. A rim wall is the shell's own thickness seen
    /// edge-on, so where the shell tapers the band must narrow with it - that is the body being thin,
    /// not the detector being wrong. Width that collapses while the thickness beside it holds steady is
    /// the opposite: nothing about the body accounts for it, so it is the measurement at fault, and on
    /// a body with two rims the likeliest fault is the pairing having jumped to the other one.
    /// </para>
    /// </summary>
    public bool TracksThickness =>
        ThicknessSamples > 0 && OutlierPoints > 0 && NormalMedianThickness > 1e-6
        && OutlierMedianThickness / NormalMedianThickness < 0.75;
}

/// <summary>One inlet of surface reaching into the rim band.</summary>
/// <param name="MouthLength">
/// How wide the opening back to the surface is. This is what separates a blemish from the band
/// genuinely stopping: a deep pocket on a narrow mouth is an intrusion into the wall, while one whose
/// mouth is as wide as the pocket is just the edge of the band being locally concave.
/// </param>
internal sealed record BayComponent(int Faces, double Area, double MouthLength, double Depth);

/// <summary>
/// Inlets of surface reaching into the band, found by closing the band and taking the difference.
///
/// <para>
/// A bay is not a hole and cannot be found the way a hole is. It is continuous with the surface it
/// comes from, so it is part of that territory, sits in no region of its own, and is enclosed by
/// nothing - every test built on connectivity looks straight past it. What makes it a bay is its
/// shape: a concavity in the band narrower than the band itself. Dilating the band and eroding it
/// back by the same amount fills exactly the concavities narrower than twice the radius and leaves
/// everything else where it was, so the difference between that and the band <em>is</em> the set of
/// bays, with no threshold beyond the radius itself.
/// </para>
///
/// <para>
/// Measured, not applied. The same operation would fix these if it were written back, so running it
/// as a measurement first says precisely what a fix would swallow before anything is swallowed.
/// </para>
/// </summary>
internal sealed record BayReport(
    bool Available, int RadiusSteps, double RadiusMm, double BandWidth,
    double BandArea, double BayArea, double BayAreaFraction,
    int Count, IReadOnlyList<BayComponent> Bays, bool[] PerFace,
    IReadOnlyList<BaySweepStep> Sweep)
{
    public static BayReport Unavailable { get; } =
        new(false, 0, 0, 0, 0, 0, 0, 0, Array.Empty<BayComponent>(), Array.Empty<bool>(),
            Array.Empty<BaySweepStep>());
}

/// <summary>
/// The same closing at a range of radii. This is what says whether a concavity is a blemish at all.
///
/// <para>
/// Closing at one radius answers only "is there a concavity narrower than this", and a pocket that
/// stays open says nothing by itself - it might be a hair too wide, or it might not be a pocket. Run
/// across radii, the shape of the answer is the finding: bay area that climbs gently with the radius
/// is the band's own sawtooth edge being nibbled at, while a pocket that appears all at once at some
/// radius has a mouth of that width. One that never appears is not a concavity at all - it is the
/// band ending and the surface carrying on.
/// </para>
/// </summary>
internal sealed record BaySweepStep(double WidthFraction, double RadiusMm, int Steps, int Count, double BayArea);

/// <summary>Which of the two the face is called by, per face, for the disagreement map.</summary>
internal enum RidgeAgreementClass { Neither, Both, RidgeOnly, ThicknessOnly }

/// <summary>
/// The ridge band judged against a rim mask derived from wall thickness.
///
/// <para>
/// Worth having because the two measurements share nothing. The ridge is a local dihedral angle
/// between neighbouring faces; the thickness is a ray fired through the solid. A face whose probe
/// never exits is looking along the shell rather than across it, which is what being on the rim means
/// - and it says so without any threshold, and without caring how coarsely the mesh is tessellated.
/// </para>
///
/// <para>
/// Not an oracle, though, and the report must not read as if it were. A genuinely thick region reads
/// unmeasured too once it passes <see cref="WallThicknessOptions.MaxThicknessMm"/>, and a probe
/// grazing a rim exits unpredictably. Disagreement localises suspicion; it does not settle it.
/// </para>
/// </summary>
internal sealed record ThicknessAgreement(
    string Mask,
    double RidgeArea, double ThicknessArea, double SharedArea, double TotalArea,
    double Precision, double Recall, double IoU,
    RidgeAgreementClass[] PerFace)
{
    public double RidgeOnlyArea => RidgeArea - SharedArea;
    public double ThicknessOnlyArea => ThicknessArea - SharedArea;
}

internal sealed record ThicknessReport(
    bool Available, string? Error,
    double Median, double Mean, double StandardDeviation, double Minimum, double Maximum,
    double FifthPercentile, double NinetyFifthPercentile,
    int MeasuredFaces, int TotalFaces, double UnmeasuredFraction,
    double SurfaceBand,
    RidgeDistribution InsideBand, RidgeDistribution OutsideBand,
    double UnmeasuredAreaInsideBand, double UnmeasuredAreaOutsideBand,
    IReadOnlyList<ThicknessAgreement> Agreements)
{
    public static ThicknessReport Unavailable(string? error) => new(
        false, error, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        RidgeDistribution.Empty, RidgeDistribution.Empty, 0, 0, Array.Empty<ThicknessAgreement>());
}

internal sealed record RidgeQuality(
    int ContourCount, int ClosedCount, int OpenCount,
    double TotalLength, double TotalLengthOverDiagonal,
    double LargestLength, double LargestShare,
    IReadOnlyList<double> ContourLengths,
    int GapCount, IReadOnlyList<double> GapLengths, double LargestGapOverDiagonal, double MaxGapAllowance,
    int SurfaceRegions, double LargestRegionShare, double SecondRegionShare, bool RidgeSeparatesSurface,
    IReadOnlyList<ContourSeparation> ContourSeparations,
    double SeamMeanDistance, double SeamMedianDistance, double SeamP95Distance,
    double SeamHausdorffSymmetric, double SeamToRidgeMean, int SeamLoopCount, double SeamTotalLength,
    string? SeamError,
    IReadOnlyList<ContourMetrics> Contours,
    ThicknessReport Thickness,
    IReadOnlyList<BandWidth> BandWidths,
    BayReport Bays);

/// <summary>
/// Turns a ridge into numbers, so the evaluation is not purely a matter of looking at pictures.
///
/// <para>
/// The metrics are chosen around one question - would this curve work as a parting line? - which is a
/// stricter thing than "does it look like the rim". A parting line has to be closed, has to separate
/// the shell into two substantial pieces, and must not pinch against itself, because a flange swept
/// along one that does will self-intersect.
/// </para>
/// </summary>
internal static class RidgeMetrics
{
    public static RidgeQuality Evaluate(
        IMesh body, RidgeDiagnosis diagnosis, PartingLine? seam, string? seamError,
        RidgeDetectionOptions options, WallThickness? thickness, string? thicknessError)
    {
        var contours = diagnosis.Contours;
        double diagonal = Math.Max(diagnosis.Report.Surface.Diagonal, 1e-6);

        var lengths = contours.Select(Length).ToList();
        double total = lengths.Sum();
        double largest = lengths.Count > 0 ? lengths.Max() : 0.0;

        var seamLoops = seam?.Loops ?? Array.Empty<IReadOnlyList<Vector3>>();

        var perContour = new List<ContourMetrics>(contours.Count);
        for (int i = 0; i < contours.Count; i++)
        {
            var distances = seamLoops.Count == 0
                ? Array.Empty<double>()
                : contours[i].Points.Select(p => DistanceToLoops(p, seamLoops)).ToArray();

            double clearance = MinSelfClearance(contours[i]);
            perContour.Add(new ContourMetrics(
                i, contours[i].Points.Count, contours[i].IsClosed,
                lengths[i], lengths[i] / diagonal,
                WorstTurnDegrees(contours[i]),
                clearance, clearance / diagonal,
                TurningNumber(contours[i]),
                distances.Length == 0 ? -1.0 : distances.Average(),
                distances.Length == 0 ? -1.0 : Percentile(distances, 0.95),
                distances.Length == 0 ? -1.0 : distances.Max()));
        }

        var (gapCount, gapLengths) = Gaps(contours);

        // Region-level separation comes free: the fill pass already flood-filled the faces with the
        // ridge edges as walls, so the two largest regions being big and covering nearly everything
        // *is* the statement that the ridge parts the shell.
        var regions = diagnosis.Report.Fill.Regions.OrderByDescending(r => r.AreaFraction).ToList();
        double first = regions.Count > 0 ? regions[0].AreaFraction : 0.0;
        double second = regions.Count > 1 ? regions[1].AreaFraction : 0.0;
        double third = regions.Count > 2 ? regions[2].AreaFraction : 0.0;

        // Exactly two dominant territories, with everything else small. Asking instead that the two
        // largest cover 90% between them would be wrong: the rim band is a third territory in its own
        // right and runs 7-25% of the area on these bodies, so a perfectly separated shell fails that
        // test purely for having a wide rim.
        bool separates = first >= 0.15 && second >= 0.15 && third < 0.15;

        // One index for every contour question asked below - it carries the face grid and adjacency,
        // and rebuilding it per contour is what turns seconds of work into minutes.
        var index = contours.Count > 0 ? new SurfaceIndex(body) : null;

        var separations = new List<ContourSeparation>();
        if (index is not null)
            for (int i = 0; i < contours.Count; i++)
                if (contours[i].IsClosed)
                    separations.Add(index.Separate(contours[i], i));

        // Symmetric distance. Point-to-segment both ways: the seam is resampled to about one mean edge
        // and a vertex-only distance overstates by half a segment on every sample.
        var ridgeToSeam = perContour.Where(c => c.SeamMeanDistance >= 0.0).ToList();
        double seamToRidgeMean = -1.0, hausdorff = -1.0;
        if (seamLoops.Count > 0 && contours.Count > 0)
        {
            var back = seamLoops.SelectMany(l => l).Select(p => DistanceToContours(p, contours)).ToArray();
            seamToRidgeMean = back.Average();
            hausdorff = Math.Max(back.Max(), ridgeToSeam.Count > 0 ? ridgeToSeam.Max(c => c.SeamMaxDistance) : 0.0);
        }

        var allForward = contours.Count > 0 && seamLoops.Count > 0
            ? contours.SelectMany(c => c.Points).Select(p => DistanceToLoops(p, seamLoops)).ToArray()
            : Array.Empty<double>();

        return new RidgeQuality(
            contours.Count,
            contours.Count(c => c.IsClosed),
            contours.Count(c => !c.IsClosed),
            total, total / diagonal,
            largest, total > 0.0 ? largest / total : 0.0,
            lengths.OrderByDescending(l => l).ToList(),
            gapCount, gapLengths,
            gapLengths.Count > 0 ? gapLengths.Max() / diagonal : 0.0,
            options.MaxGapFraction,
            diagnosis.Report.Fill.RegionCount, first, second, separates,
            separations,
            allForward.Length == 0 ? -1.0 : allForward.Average(),
            allForward.Length == 0 ? -1.0 : Percentile(allForward, 0.50),
            allForward.Length == 0 ? -1.0 : Percentile(allForward, 0.95),
            hausdorff, seamToRidgeMean,
            seamLoops.Count, seamLoops.Sum(LoopLength),
            seamError,
            perContour,
            MeasureThickness(body, diagnosis, thickness, thicknessError),
            MeasureBandWidths(contours, index, thickness),
            MeasureBays(diagnosis, index));
    }

    // ---------------------------------------------------------------- bays

    /// <summary>
    /// Closes the band at a radius scaled to the band's own width and reports what the closing would
    /// have filled. Nothing is written back - <see cref="BayReport.PerFace"/> is the finding.
    /// </summary>
    private static BayReport MeasureBays(
        RidgeDiagnosis diagnosis, SurfaceIndex? index, double widthFraction = 1.0)
    {
        if (index is null) return BayReport.Unavailable;

        double bandWidth = diagnosis.Report.Fill.BandWidth;
        if (bandWidth <= 1e-6) return BayReport.Unavailable;

        var band = diagnosis.RidgeFaces;
        if (band.Length != index.Areas.Count) return BayReport.Unavailable;

        // The radius has to be at least a step or the closing is a no-op, and there is no point going
        // beyond the band's own width: a concavity wider than the wall is the wall ending, not a
        // blemish in it.
        double radiusMm = widthFraction * bandWidth;
        int radius = Math.Max(1, (int)Math.Round(radiusMm / Math.Max(index.MeanEdgeLength, 1e-6)));

        var closed = index.Close(band, radius);

        var bays = new bool[band.Length];
        double bayArea = 0.0, bandArea = 0.0;
        for (int f = 0; f < band.Length; f++)
        {
            if (band[f]) bandArea += index.Areas[f];
            if (!closed[f] || band[f]) continue;
            bays[f] = true;
            bayArea += index.Areas[f];
        }

        var components = index.Components(bays, band, index.MeanEdgeLength);

        var sweep = new List<BaySweepStep>();
        foreach (double fraction in new[] { 0.5, 1.0, 2.0, 3.0, 5.0 })
        {
            double mm = fraction * bandWidth;
            int steps = Math.Max(1, (int)Math.Round(mm / Math.Max(index.MeanEdgeLength, 1e-6)));

            var swept = index.Close(band, steps);
            double area = 0.0;
            var mask = new bool[band.Length];
            for (int f = 0; f < band.Length; f++)
            {
                if (!swept[f] || band[f]) continue;
                mask[f] = true;
                area += index.Areas[f];
            }

            sweep.Add(new BaySweepStep(
                fraction, mm, steps, index.Components(mask, band, index.MeanEdgeLength).Count, area));
        }

        return new BayReport(
            true, radius, radiusMm, bandWidth,
            bandArea, bayArea, bandArea > 1e-6 ? bayArea / bandArea : 0.0,
            components.Count, components.Take(20).ToList(), bays, sweep);
    }

    // ---------------------------------------------------------------- band width

    /// <summary>
    /// Pairs each contour with the one that bounds the same band and samples the width between them.
    ///
    /// <para>
    /// Pairing is by mean distance rather than by index or by length: on a body with two rims the
    /// contours arrive in whatever order the crease walk produced, and the only thing that reliably
    /// says two curves bound the same wall is that they run alongside each other the whole way.
    /// </para>
    /// </summary>
    private static IReadOnlyList<BandWidth> MeasureBandWidths(
        IReadOnlyList<RidgeContour> contours, SurfaceIndex? index, WallThickness? thickness)
    {
        var widths = new List<BandWidth>(contours.Count);

        for (int i = 0; i < contours.Count; i++)
        {
            int partner = -1;
            double closest = double.MaxValue;
            for (int j = 0; j < contours.Count; j++)
            {
                if (j == i) continue;
                double mean = contours[i].Points.Average(p => DistanceToContour(p, contours[j]));
                if (mean >= closest) continue;
                closest = mean;
                partner = j;
            }

            if (partner < 0)
            {
                widths.Add(new BandWidth(i, -1, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                    Array.Empty<double>(), Array.Empty<double>(), 0, 0, 0, 0, 0, 0, 0));
                continue;
            }

            var perPoint = contours[i].Points
                .Select(p => DistanceToContour(p, contours[partner]))
                .ToArray();

            var sorted = (double[])perPoint.Clone();
            Array.Sort(sorted);

            double median = sorted[sorted.Length / 2];
            double mean2 = perPoint.Average();
            double variance = perPoint.Sum(w => (w - mean2) * (w - mean2)) / perPoint.Length;
            double sd = Math.Sqrt(variance);

            // Against the median rather than the mean, and by ratio rather than by standard deviations:
            // a band with one bad stretch has a mean and a deviation that the bad stretch itself has
            // already moved, so measuring against them hides exactly what is being looked for.
            double low = median * 0.6;
            double high = median * 1.6;
            int outliers = perPoint.Count(w => w < low || w > high);

            // The shell's own thickness beside each sample, which is what says whether a narrow stretch
            // of band is a narrow stretch of body.
            var local = new double[perPoint.Length];
            if (index is not null && thickness is not null)
                for (int p = 0; p < perPoint.Length; p++)
                    local[p] = index.LocalThickness(contours[i].Points[p], thickness);
            else
                Array.Fill(local, double.PositiveInfinity);

            var pairedW = new List<double>();
            var pairedT = new List<double>();
            var normalW = new List<double>();
            var normalT = new List<double>();
            var outlierW = new List<double>();
            var outlierT = new List<double>();

            for (int p = 0; p < perPoint.Length; p++)
            {
                if (double.IsPositiveInfinity(local[p])) continue;

                pairedW.Add(perPoint[p]);
                pairedT.Add(local[p]);

                bool isOutlier = perPoint[p] < low || perPoint[p] > high;
                (isOutlier ? outlierW : normalW).Add(perPoint[p]);
                (isOutlier ? outlierT : normalT).Add(local[p]);
            }

            widths.Add(new BandWidth(
                i, partner, true,
                median, mean2, sd,
                sorted[0], sorted[^1],
                Percentile(sorted, 0.05), Percentile(sorted, 0.95),
                median > 1e-6 ? sd / median : 0.0,
                outliers, (double)outliers / perPoint.Length, low, high,
                perPoint, local,
                pairedW.Count,
                Correlation(pairedW, pairedT),
                Median(pairedW.Zip(pairedT, (w, t) => t > 1e-6 ? w / t : 0.0).ToList()),
                Median(normalW), Median(normalT),
                Median(outlierW), Median(outlierT)));
        }

        return widths;
    }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0.0;
        var sorted = values.ToArray();
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }

    /// <summary>Pearson correlation, or zero where either series is flat and the question is undefined.</summary>
    private static double Correlation(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        if (a.Count < 3) return 0.0;

        double meanA = a.Average();
        double meanB = b.Average();
        double sumAB = 0.0, sumAA = 0.0, sumBB = 0.0;

        for (int i = 0; i < a.Count; i++)
        {
            double da = a[i] - meanA;
            double db = b[i] - meanB;
            sumAB += da * db;
            sumAA += da * da;
            sumBB += db * db;
        }

        double denominator = Math.Sqrt(sumAA * sumBB);
        return denominator < 1e-9 ? 0.0 : sumAB / denominator;
    }

    private static double DistanceToContour(Vector3 point, RidgeContour contour)
    {
        var points = contour.Points;
        int spans = contour.IsClosed ? points.Count : points.Count - 1;

        double best = double.MaxValue;
        for (int i = 0; i < spans; i++)
            best = Math.Min(best, PointToSegment(point, points[i], points[(i + 1) % points.Count]));
        return best;
    }

    // ---------------------------------------------------------------- against wall thickness

    private static ThicknessReport MeasureThickness(
        IMesh body, RidgeDiagnosis diagnosis, WallThickness? thickness, string? error)
    {
        if (thickness is null) return ThicknessReport.Unavailable(error);

        var band = diagnosis.RidgeFaces;
        int faceCount = body.Triangles.Length / 3;
        if (thickness.PerFace.Count != faceCount || band.Length != faceCount)
            return ThicknessReport.Unavailable("thickness and ridge were measured on different meshes");

        var areas = FaceAreas(body);
        double median = thickness.Statistics.Median;

        // Two masks, because they fail differently and the pair says more than either alone.
        // Unmeasured is threshold-free: the probe left along the shell instead of crossing it.
        // Corridor is the wider definition ThicknessParting parts along, so it also picks up the
        // transition either side of the rim.
        var unmeasured = new bool[faceCount];
        var corridor = new bool[faceCount];
        double low = median * (1.0 - ThicknessPartingOptions.Default.SurfaceBand);
        double high = median * (1.0 + ThicknessPartingOptions.Default.SurfaceBand);

        var inside = new List<double>();
        var outside = new List<double>();
        double unmeasuredInside = 0.0, unmeasuredOutside = 0.0;

        for (int f = 0; f < faceCount; f++)
        {
            double t = thickness.PerFace[f];
            bool never = double.IsPositiveInfinity(t) || thickness.PartnerFace[f] < 0;

            unmeasured[f] = never;
            corridor[f] = never || t < low || t > high;

            if (never)
            {
                if (band[f]) unmeasuredInside += areas[f];
                else unmeasuredOutside += areas[f];
                continue;
            }

            // Only measured faces go into the distributions; an infinity would swamp every percentile
            // and say nothing the unmeasured areas beside them do not already say.
            (band[f] ? inside : outside).Add(t);
        }

        double ceiling = Math.Max(median * 4.0, 1.0);
        var s = thickness.Statistics;

        return new ThicknessReport(
            true, null,
            s.Median, s.Mean, s.StandardDeviation, s.Minimum, s.Maximum,
            s.FifthPercentile, s.NinetyFifthPercentile,
            s.MeasuredFaces, s.TotalFaces, s.UnmeasuredFraction,
            ThicknessPartingOptions.Default.SurfaceBand,
            RidgeDistribution.From(inside, 0.0, ceiling, 40),
            RidgeDistribution.From(outside, 0.0, ceiling, 40),
            unmeasuredInside, unmeasuredOutside,
            new[]
            {
                Agree("unmeasured", band, unmeasured, areas),
                Agree("corridor", band, corridor, areas),
            });
    }

    /// <summary>
    /// Overlap of the two masks, weighted by area rather than counted by face. Face areas on these
    /// bodies span orders of magnitude - the rim is tessellated far finer than the surfaces it divides -
    /// so counting faces would weight a sliver on the rim the same as a broad face in the middle of the
    /// shell and report an agreement that has little to do with how much of the model agrees.
    /// </summary>
    private static ThicknessAgreement Agree(string mask, bool[] band, bool[] rim, double[] areas)
    {
        double ridgeArea = 0.0, thicknessArea = 0.0, shared = 0.0, total = 0.0;
        var perFace = new RidgeAgreementClass[areas.Length];

        for (int f = 0; f < areas.Length; f++)
        {
            total += areas[f];
            if (band[f]) ridgeArea += areas[f];
            if (rim[f]) thicknessArea += areas[f];
            if (band[f] && rim[f]) shared += areas[f];

            perFace[f] = (band[f], rim[f]) switch
            {
                (true, true) => RidgeAgreementClass.Both,
                (true, false) => RidgeAgreementClass.RidgeOnly,
                (false, true) => RidgeAgreementClass.ThicknessOnly,
                _ => RidgeAgreementClass.Neither,
            };
        }

        double union = ridgeArea + thicknessArea - shared;
        return new ThicknessAgreement(
            mask, ridgeArea, thicknessArea, shared, total,
            ridgeArea > 0.0 ? shared / ridgeArea : 0.0,
            thicknessArea > 0.0 ? shared / thicknessArea : 0.0,
            union > 0.0 ? shared / union : 0.0,
            perFace);
    }

    private static double[] FaceAreas(IMesh body)
    {
        var vertices = body.Vertices;
        var triangles = body.Triangles;
        var areas = new double[triangles.Length / 3];

        for (int f = 0; f < areas.Length; f++)
        {
            var a = vertices[triangles[f * 3]];
            var b = vertices[triangles[(f * 3) + 1]];
            var c = vertices[triangles[(f * 3) + 2]];
            areas[f] = (b - a).Cross(c - a).Length * 0.5;
        }
        return areas;
    }

    // ---------------------------------------------------------------- shape of one contour

    public static double Length(RidgeContour contour)
    {
        var points = contour.Points;
        int spans = contour.IsClosed ? points.Count : points.Count - 1;

        double total = 0.0;
        for (int i = 0; i < spans; i++) total += points[i].DistanceTo(points[(i + 1) % points.Count]);
        return total;
    }

    private static double LoopLength(IReadOnlyList<Vector3> loop)
    {
        double total = 0.0;
        for (int i = 0; i < loop.Count; i++) total += loop[i].DistanceTo(loop[(i + 1) % loop.Count]);
        return total;
    }

    private static double WorstTurnDegrees(RidgeContour contour)
    {
        var points = contour.Points;
        int first = contour.IsClosed ? 0 : 1;
        int last = contour.IsClosed ? points.Count : points.Count - 1;

        double worst = 0.0;
        for (int i = first; i < last; i++)
        {
            var incoming = points[i] - points[(i - 1 + points.Count) % points.Count];
            var outgoing = points[(i + 1) % points.Count] - points[i];
            if (incoming.Length < 1e-6 || outgoing.Length < 1e-6) continue;

            double turn = Math.Acos(Math.Clamp(
                incoming.Normalize().Dot(outgoing.Normalize()), -1.0, 1.0));
            worst = Math.Max(worst, turn * 180.0 / Math.PI);
        }
        return worst;
    }

    /// <summary>
    /// Closest approach between two parts of the same contour that are not neighbours along it. A true
    /// 3D self-intersection is measure-zero and will essentially never be found by sampling; a near
    /// touch is the measurable form of the same defect, and it is what wrecks a flange sweep.
    /// </summary>
    private static double MinSelfClearance(RidgeContour contour, int skip = 6)
    {
        var points = contour.Points;
        int n = points.Count;
        if (n < (skip * 2) + 4) return double.PositiveInfinity;

        double best = double.PositiveInfinity;
        for (int i = 0; i < n; i++)
        {
            for (int j = i + skip; j < n; j++)
            {
                if (contour.IsClosed && n - (j - i) < skip) continue;
                best = Math.Min(best, points[i].DistanceTo(points[j]));
            }
        }
        return best;
    }

    /// <summary>
    /// How many times the contour goes round, measured in its own best-fit plane. +-1 means it
    /// circumnavigates once, which is what a rim should do; 0 means it doubles back on itself.
    /// </summary>
    private static double TurningNumber(RidgeContour contour)
    {
        var points = contour.Points;
        if (points.Count < 4) return 0.0;

        var centroid = Vector3.Zero;
        foreach (var p in points) centroid += p;
        centroid /= points.Count;

        // Newell's method: robust for a non-planar loop, which every real rim is.
        var normal = Vector3.Zero;
        for (int i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            normal += new Vector3(
                (a.Y - b.Y) * (a.Z + b.Z),
                (a.Z - b.Z) * (a.X + b.X),
                (a.X - b.X) * (a.Y + b.Y));
        }
        if (normal.LengthSquared < 1e-12) return 0.0;
        normal = normal.Normalize();

        var seed = Math.Abs(normal.Y) > 0.9 ? Vector3.UnitX : Vector3.UnitY;
        var u = seed.Cross(normal).Normalize();
        var v = normal.Cross(u);

        double total = 0.0;
        for (int i = 0; i < points.Count; i++)
        {
            var a = points[i] - centroid;
            var b = points[(i + 1) % points.Count] - centroid;
            double a0 = Math.Atan2(a.Dot(v), a.Dot(u));
            double b0 = Math.Atan2(b.Dot(v), b.Dot(u));

            double step = b0 - a0;
            while (step > Math.PI) step -= 2.0 * Math.PI;
            while (step < -Math.PI) step += 2.0 * Math.PI;
            total += step;
        }
        return total / (2.0 * Math.PI);
    }

    // ---------------------------------------------------------------- gaps

    /// <summary>
    /// Pairs each open contour's endpoints with the nearest endpoint that is not its own partner. A
    /// gap shorter than <see cref="RidgeDetectionOptions.MaxGapFraction"/> that is still open is a
    /// bridging failure, which is the one directly actionable thing this measures.
    /// </summary>
    private static (int Count, IReadOnlyList<double> Lengths) Gaps(IReadOnlyList<RidgeContour> contours)
    {
        var ends = new List<(Vector3 Point, int Owner)>();
        for (int i = 0; i < contours.Count; i++)
        {
            if (contours[i].IsClosed || contours[i].Points.Count < 2) continue;
            ends.Add((contours[i].Points[0], i));
            ends.Add((contours[i].Points[^1], i));
        }

        var lengths = new List<double>();
        foreach (var (point, owner) in ends)
        {
            double best = double.PositiveInfinity;
            foreach (var (other, otherOwner) in ends)
            {
                if (otherOwner == owner) continue;
                best = Math.Min(best, point.DistanceTo(other));
            }
            if (!double.IsPositiveInfinity(best)) lengths.Add(best);
        }

        lengths.Sort();
        lengths.Reverse();
        return (ends.Count, lengths);
    }

    // ---------------------------------------------------------------- separation

    /// <summary>
    /// Face centroids in a uniform grid plus face adjacency, built once per body. Both the nearest-face
    /// lookup and the flood are per-contour, and rebuilding this for each of a dozen contours is what
    /// turns a second of work into a minute of it.
    /// </summary>
    private sealed class SurfaceIndex
    {
        private readonly int[] _triangles;
        private readonly Vector3[] _centroids;
        private readonly double[] _areas;
        private readonly double _totalArea;
        private readonly Dictionary<(int, int), List<int>> _edges = new();

        private readonly Dictionary<(int, int, int), List<int>> _cells = new();
        private readonly double _cell;
        private readonly List<int>[] _faceNeighbours;

        public SurfaceIndex(IMesh body)
        {
            var vertices = body.Vertices;
            _triangles = body.Triangles.ToArray();
            int faceCount = _triangles.Length / 3;

            _centroids = new Vector3[faceCount];
            _areas = new double[faceCount];

            double edgeTotal = 0d;
            for (int f = 0; f < faceCount; f++)
            {
                var a = vertices[_triangles[f * 3]];
                var b = vertices[_triangles[(f * 3) + 1]];
                var c = vertices[_triangles[(f * 3) + 2]];

                _centroids[f] = (a + b + c) / 3.0;
                _areas[f] = (b - a).Cross(c - a).Length * 0.5;
                _totalArea += _areas[f];
                edgeTotal += a.DistanceTo(b);

                for (int e = 0; e < 3; e++)
                {
                    int i = _triangles[(f * 3) + e];
                    int j = _triangles[(f * 3) + ((e + 1) % 3)];
                    var key = i < j ? (i, j) : (j, i);
                    if (!_edges.TryGetValue(key, out var list)) _edges[key] = list = new List<int>(2);
                    list.Add(f);
                }
            }

            _cell = Math.Max((double)(edgeTotal / Math.Max(faceCount, 1)) * 2.0, 1e-4);
            for (int f = 0; f < faceCount; f++)
            {
                var key = Cell(_centroids[f]);
                if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<int>(4);
                list.Add(f);
            }

            _faceNeighbours = new List<int>[faceCount];
            for (int f = 0; f < faceCount; f++) _faceNeighbours[f] = new List<int>(3);
            foreach (var shared in _edges.Values)
                for (int i = 0; i < shared.Count; i++)
                    for (int j = 0; j < shared.Count; j++)
                        if (i != j) _faceNeighbours[shared[i]].Add(shared[j]);
        }

        private (int, int, int) Cell(Vector3 p) => (
            (int)Math.Floor(p.X / _cell), (int)Math.Floor(p.Y / _cell), (int)Math.Floor(p.Z / _cell));

        /// <summary>
        /// Shell thickness near a point, from the nearest face that could actually be measured.
        ///
        /// <para>
        /// The walk outward is the whole point. A contour sits on the crease, and a face on the crease
        /// is looking along the shell rather than across it, so its own probe never exits - asking the
        /// nearest face directly returns "unmeasured" almost every time and the comparison collapses.
        /// The faces a step or two away are on the surface either side, and those read the wall.
        /// </para>
        /// </summary>
        public double LocalThickness(Vector3 point, WallThickness thickness, int maxSteps = 4)
        {
            int start = Nearest(point);
            if (start < 0) return double.PositiveInfinity;

            var seen = new HashSet<int> { start };
            var frontier = new List<int> { start };
            var next = new List<int>();

            for (int step = 0; step <= maxSteps; step++)
            {
                double best = double.PositiveInfinity;
                foreach (int face in frontier)
                {
                    double t = thickness.PerFace[face];
                    if (double.IsPositiveInfinity(t) || thickness.PartnerFace[face] < 0) continue;
                    best = Math.Min(best, t);
                }
                if (!double.IsPositiveInfinity(best)) return best;

                next.Clear();
                foreach (int face in frontier)
                    foreach (int neighbour in _faceNeighbours[face])
                        if (seen.Add(neighbour)) next.Add(neighbour);

                if (next.Count == 0) break;
                (frontier, next) = (next, frontier);
            }

            return double.PositiveInfinity;
        }

        private int Nearest(Vector3 point)
        {
            var (cx, cy, cz) = Cell(point);

            // Widen until something is found: a contour is lifted off the surface, so the cell it
            // lands in is occasionally empty even though a face is right beside it.
            for (int radius = 1; radius <= 6; radius++)
            {
                int best = -1;
                double bestDistance = double.MaxValue;

                for (int x = cx - radius; x <= cx + radius; x++)
                    for (int y = cy - radius; y <= cy + radius; y++)
                        for (int z = cz - radius; z <= cz + radius; z++)
                        {
                            if (!_cells.TryGetValue((x, y, z), out var faces)) continue;
                            foreach (int f in faces)
                            {
                                double d = _centroids[f].DistanceSquared(point);
                                if (d >= bestDistance) continue;
                                bestDistance = d;
                                best = f;
                            }
                        }

                if (best >= 0) return best;
            }
            return -1;
        }

        public double MeanEdgeLength => _cell * 0.5;
        public IReadOnlyList<double> Areas => _areas;

        /// <summary>
        /// Morphological closing of a face mask: grow it by <paramref name="radius"/> steps over the
        /// face graph, then shrink it back by the same. A concavity narrower than twice the radius is
        /// bridged by the growth and does not reopen when it shrinks; anything wider is restored
        /// exactly, which is what makes the difference a statement about shape rather than about size.
        /// </summary>
        public bool[] Close(bool[] mask, int radius)
        {
            var grown = Grow(mask, radius);

            // Eroding the grown mask is the same as growing its complement and taking what is left.
            var outside = new bool[grown.Length];
            for (int f = 0; f < grown.Length; f++) outside[f] = !grown[f];

            var outsideGrown = Grow(outside, radius);

            var closed = new bool[grown.Length];
            for (int f = 0; f < grown.Length; f++) closed[f] = !outsideGrown[f];
            return closed;
        }

        private bool[] Grow(bool[] mask, int radius)
        {
            var current = (bool[])mask.Clone();
            var frontier = new List<int>();
            for (int f = 0; f < current.Length; f++)
                if (current[f]) frontier.Add(f);

            var next = new List<int>();
            for (int step = 0; step < radius && frontier.Count > 0; step++)
            {
                next.Clear();
                foreach (int face in frontier)
                    foreach (int neighbour in _faceNeighbours[face])
                        if (!current[neighbour])
                        {
                            current[neighbour] = true;
                            next.Add(neighbour);
                        }

                (frontier, next) = (next, frontier);
            }

            return current;
        }

        /// <summary>Splits a mask into connected components, with each one's area and its boundary to
        /// faces that are in neither the mask nor <paramref name="band"/> - the mouth back to the surface.</summary>
        public IReadOnlyList<BayComponent> Components(bool[] mask, bool[] band, double meanEdge)
        {
            var seen = new bool[mask.Length];
            var found = new List<BayComponent>();
            var stack = new Stack<int>();
            var member = new List<int>();

            for (int seed = 0; seed < mask.Length; seed++)
            {
                if (seen[seed] || !mask[seed]) continue;

                member.Clear();
                seen[seed] = true;
                stack.Push(seed);

                double area = 0.0;
                int mouthFaces = 0;

                while (stack.Count > 0)
                {
                    int face = stack.Pop();
                    member.Add(face);
                    area += _areas[face];

                    foreach (int neighbour in _faceNeighbours[face])
                    {
                        if (mask[neighbour])
                        {
                            if (!seen[neighbour])
                            {
                                seen[neighbour] = true;
                                stack.Push(neighbour);
                            }
                            continue;
                        }

                        // Neither bay nor band: the surface the bay opens onto.
                        if (!band[neighbour]) mouthFaces++;
                    }
                }

                double mouth = mouthFaces * meanEdge;
                found.Add(new BayComponent(
                    member.Count, area, mouth, mouth > 1e-6 ? area / mouth : double.PositiveInfinity));
            }

            return found.OrderByDescending(b => b.Area).ToList();
        }

        /// <summary>Marks the faces along the shortest face path between two marks, sealing the ribbon.</summary>
        private void Connect(bool[] wall, int from, int to, int maxSteps = 24)
        {
            if (from == to) return;

            var previous = new Dictionary<int, int> { [from] = -1 };
            var frontier = new Queue<int>();
            frontier.Enqueue(from);

            for (int step = 0; step < maxSteps && frontier.Count > 0; step++)
            {
                int wide = frontier.Count;
                for (int i = 0; i < wide; i++)
                {
                    int current = frontier.Dequeue();
                    foreach (int next in _faceNeighbours[current])
                    {
                        if (previous.ContainsKey(next)) continue;
                        previous[next] = current;

                        if (next == to)
                        {
                            for (int at = to; at >= 0; at = previous[at]) wall[at] = true;
                            return;
                        }
                        frontier.Enqueue(next);
                    }
                }
            }
        }

        /// <summary>
        /// Marks the faces the contour runs across as a wall and floods what is left, so the question
        /// "does this curve divide the body" can be answered without cutting the mesh.
        /// </summary>
        public ContourSeparation Separate(RidgeContour contour, int index)
        {
            int faceCount = _areas.Length;

            var wall = new bool[faceCount];

            // Marking the nearest face to each contour point is not enough on its own. Consecutive
            // points frequently land on the same face and then skip one entirely where the curve
            // crosses a corner, and a ribbon with a single face missing does not block a flood at all -
            // it reports one component and looks exactly like a curve that genuinely divides nothing.
            // Walking the gap closes it.
            var marks = new List<int>(contour.Points.Count);
            foreach (var point in contour.Points)
            {
                int nearest = Nearest(point);
                if (nearest < 0) continue;
                wall[nearest] = true;
                marks.Add(nearest);
            }

            int spans = contour.IsClosed ? marks.Count : marks.Count - 1;
            for (int i = 0; i < spans; i++)
                Connect(wall, marks[i], marks[(i + 1) % marks.Count]);

            var region = new int[faceCount];
            Array.Fill(region, -1);
            var componentArea = new List<double>();
            var stack = new Stack<int>();

            for (int seed = 0; seed < faceCount; seed++)
            {
                if (region[seed] >= 0 || wall[seed]) continue;

                int id = componentArea.Count;
                double area = 0.0;
                region[seed] = id;
                stack.Push(seed);

                while (stack.Count > 0)
                {
                    int face = stack.Pop();
                    area += _areas[face];

                    for (int e = 0; e < 3; e++)
                    {
                        int a = _triangles[(face * 3) + e];
                        int b = _triangles[(face * 3) + ((e + 1) % 3)];
                        var key = a < b ? (a, b) : (b, a);
                        foreach (int across in _edges[key])
                        {
                            if (across == face || wall[across] || region[across] >= 0) continue;
                            region[across] = id;
                            stack.Push(across);
                        }
                    }
                }
                componentArea.Add(area);
            }

            componentArea.Sort();
            componentArea.Reverse();
            return new ContourSeparation(
                index, componentArea.Count,
                componentArea.Count > 0 && _totalArea > 0.0 ? componentArea[0] / _totalArea : 0.0,
                componentArea.Count > 1 && _totalArea > 0.0 ? componentArea[1] / _totalArea : 0.0);
        }
    }

    // ---------------------------------------------------------------- distances

    private static double DistanceToLoops(Vector3 point, IReadOnlyList<IReadOnlyList<Vector3>> loops)
    {
        double best = double.MaxValue;
        foreach (var loop in loops)
            for (int i = 0; i < loop.Count; i++)
                best = Math.Min(best, PointToSegment(point, loop[i], loop[(i + 1) % loop.Count]));
        return best;
    }

    private static double DistanceToContours(Vector3 point, IReadOnlyList<RidgeContour> contours)
    {
        double best = double.MaxValue;
        foreach (var contour in contours)
        {
            var points = contour.Points;
            int spans = contour.IsClosed ? points.Count : points.Count - 1;
            for (int i = 0; i < spans; i++)
                best = Math.Min(best, PointToSegment(point, points[i], points[(i + 1) % points.Count]));
        }
        return best;
    }

    private static double PointToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        double lengthSquared = ab.LengthSquared;
        if (lengthSquared < 1e-12) return p.DistanceTo(a);

        double t = Math.Clamp((p - a).Dot(ab) / lengthSquared, 0.0, 1.0);
        return p.DistanceTo(a + (ab * t));
    }

    private static double Percentile(double[] values, double fraction)
    {
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        return sorted[Math.Clamp((int)Math.Round(fraction * (sorted.Length - 1)), 0, sorted.Length - 1)];
    }
}
