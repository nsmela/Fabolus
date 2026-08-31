using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fabolus.Core.Geometry;

namespace Fabolus.Tests.Diagnostics;

internal sealed record RidgeSummaryRow(string Model, RidgeQuality Quality, RidgeReport Report);

/// <summary>
/// Writes the pass-by-pass narrative beside the pictures. The narrative matters as much as the images:
/// an empty result looks identical whether no edge cleared the grow threshold or the percolation guard
/// wiped a perfectly good ridge, and only the report can tell those apart.
/// </summary>
internal static class RidgeReportWriter
{
    /// <summary>Formats a measurement that cannot be negative, so a negative reads as "not measured".</summary>
    private static string N(float value, int decimals = 3) =>
        float.IsPositiveInfinity(value) ? "inf"
        : value < 0f ? "-"
        : value.ToString("F" + decimals, CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a value whose sign is part of the answer. A turning number of -1 means the curve goes
    /// round once the other way, which <see cref="N"/> would hide as "not measured".
    /// </summary>
    private static string S(float value, int decimals = 2) =>
        float.IsPositiveInfinity(value) ? "inf"
        : float.IsNegativeInfinity(value) ? "-inf"
        : value.ToString("F" + decimals, CultureInfo.InvariantCulture);

    public static void WriteModel(string directory, string model, RidgeDiagnosis diagnosis, RidgeQuality quality)
    {
        Directory.CreateDirectory(directory);

        var report = diagnosis.Report;
        var text = new StringBuilder();

        text.AppendLine($"# {model}").AppendLine();

        // --- verdict up front ---
        text.AppendLine("## Verdict").AppendLine();
        text.AppendLine($"- contours: **{quality.ContourCount}** ({quality.ClosedCount} closed, {quality.OpenCount} open)");
        text.AppendLine($"- total length / diagonal: **{N(quality.TotalLengthOverDiagonal, 2)}**");
        text.AppendLine($"- largest contour holds **{N(quality.LargestShare * 100f, 1)}%** of that length (fragmentation)");
        text.AppendLine($"- ridge separates the shell into two substantial pieces: **{quality.RidgeSeparatesSurface}** " +
                        $"(largest regions {N(quality.LargestRegionShare * 100f, 1)}% / {N(quality.SecondRegionShare * 100f, 1)}%)");
        if (quality.GapCount > 0)
            text.AppendLine($"- {quality.GapCount} open endpoints; largest gap / diagonal **{N(quality.LargestGapOverDiagonal, 3)}** " +
                            $"(bridging allowance {N(quality.MaxGapAllowance, 3)})");
        text.AppendLine();

        // --- pass 1 ---
        var s = report.Surface;
        text.AppendLine("## Surface").AppendLine();
        text.AppendLine($"| vertices (source / welded) | {s.SourceVertices} / {s.WeldedVertices} |");
        text.AppendLine("|---|---|");
        text.AppendLine($"| faces | {s.Faces} |");
        text.AppendLine($"| edges (interior / boundary) | {s.Edges} ({s.InteriorEdges} / {s.BoundaryEdges}) |");
        text.AppendLine($"| Euler characteristic / genus | {s.EulerCharacteristic} / **{s.Genus}** |");
        text.AppendLine($"| bbox diagonal | {N(s.Diagonal, 2)} mm |");
        text.AppendLine($"| total area | {N(s.TotalArea, 1)} mm² |");
        text.AppendLine($"| mean edge length | {N(s.MeanEdgeLength, 3)} mm |");
        text.AppendLine();
        text.AppendLine($"fold angle (deg): min {N(s.FoldAngleDegrees.Min, 1)}, p50 {N(s.FoldAngleDegrees.P50, 1)}, " +
                        $"p90 {N(s.FoldAngleDegrees.P90, 1)}, p99 {N(s.FoldAngleDegrees.P99, 1)}, max {N(s.FoldAngleDegrees.Max, 1)}");
        text.AppendLine();
        text.AppendLine($"curvature (1/mm): min {N(s.Curvature.Min)}, p50 {N(s.Curvature.P50)}, " +
                        $"p90 {N(s.Curvature.P90)}, p99 {N(s.Curvature.P99)}, max {N(s.Curvature.Max)}");
        text.AppendLine();

        // --- pass 2 ---
        var t = report.Threshold;
        text.AppendLine("## Threshold (hysteresis)").AppendLine();
        text.AppendLine($"- candidates (cleared grow): **{t.CandidateEdges}** of {s.InteriorEdges} interior edges " +
                        $"({N(s.InteriorEdges > 0 ? 100f * t.CandidateEdges / s.InteriorEdges : 0f, 1)}%)");
        text.AppendLine($"  - by curvature {t.GrowByCurvature}, by angle {t.GrowByAngle}");
        text.AppendLine($"- seeds: **{t.SeedEdges}** (by curvature {t.SeedByCurvature}, by angle {t.SeedByAngle})");
        text.AppendLine($"- minimum run length: {N(t.MinRunLength, 2)} mm");
        text.AppendLine($"- runs: {t.RunCount}; kept {t.Runs.Count(r => r.Verdict == RidgeRunVerdict.Kept)}, " +
                        $"no seed {t.Runs.Count(r => r.Verdict == RidgeRunVerdict.NoSeed)}, " +
                        $"too short {t.Runs.Count(r => r.Verdict == RidgeRunVerdict.TooShort)}");
        text.AppendLine($"- kept edges: {t.KeptEdgesBeforeGuard} ({N(t.KeptEdgeFraction * 100f, 1)}% of all edges)");
        text.AppendLine($"- **percolation guard fired: {t.PercolationGuardFired}**");
        text.AppendLine();

        if (t.Runs.Count > 0)
        {
            text.AppendLine("| run | edges | length mm | len/diag | seeds | verdict |");
            text.AppendLine("|---|---|---|---|---|---|");
            foreach (var run in t.Runs.Take(15))
                text.AppendLine($"| | {run.EdgeCount} | {N(run.Length, 1)} | {N(run.LengthOverDiagonal)} | {run.SeedEdges} | {run.Verdict} |");
            if (t.Runs.Count > 15) text.AppendLine($"| … {t.Runs.Count - 15} more | | | | | |");
            text.AppendLine();
        }

        // --- pass 3 ---
        var b = report.Bridging;
        text.AppendLine("## Bridging").AppendLine();
        if (!b.Ran)
        {
            text.AppendLine($"- did not run: {b.SkipReason}");
        }
        else
        {
            text.AppendLine($"- allowance: {N(b.MaxGap, 2)} mm");
            text.AppendLine($"- ridge edges {b.RidgeEdgesBefore} → {b.RidgeEdgesAfter}");
            text.AppendLine($"- loose ends {b.LooseEndsBefore} → **{b.LooseEndsAfter}**");
            text.AppendLine($"- bridges added: {b.BridgesAdded}" +
                            (b.BridgeLengths.Count > 0
                                ? $"; lengths {string.Join(", ", b.BridgeLengths.Take(10).Select(l => N(l, 1)))}"
                                : ""));
        }
        text.AppendLine();

        // --- pass 4 ---
        var f = report.Fill;
        text.AppendLine("## Region fill").AppendLine();
        text.AppendLine($"- regions: {f.RegionCount}; filled {f.FilledRegions} ({f.FilledFaces} faces, " +
                        $"{N(f.FilledAreaFraction * 100f, 2)}% of area)");
        text.AppendLine($"- limits: area < {N(f.MaxAreaFraction * 100f, 1)}%, mean width < {N(f.MaxWidthFraction, 3)} × diagonal");
        text.AppendLine($"- **band groups: {f.BandGroups}** (one per rim; fewer means two rims' walls touch and " +
                        "a walk cannot tell them apart)");
        text.AppendLine($"- **pockets closed: {f.ClosedHoles}** of {f.Holes.Count} found; band width " +
                        $"{N(f.BandWidth, 2)} mm, so a pocket is closed below {N(f.MaxHoleWidth, 2)} mm wide");
        if (f.Holes.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("| faces | area mm² | perimeter mm | width mm | enclosed | verdict |");
            text.AppendLine("|---|---|---|---|---|---|");
            foreach (var h in f.Holes)
                text.AppendLine($"| {h.Faces} | {N(h.Area, 1)} | {N(h.Perimeter, 1)} | {N(h.Width, 2)} | " +
                                $"{h.Enclosed} | {(h.Closed ? "**closed**" : h.Verdict)} |");
        }
        text.AppendLine();
        if (f.Regions.Count > 0)
        {
            text.AppendLine("| faces | area % | mean width mm | width/diag | filled |");
            text.AppendLine("|---|---|---|---|---|");
            foreach (var region in f.Regions.Take(12))
                text.AppendLine($"| {region.FaceCount} | {N(region.AreaFraction * 100f, 2)} | {N(region.MeanWidth, 2)} | " +
                                $"{N(region.MeanWidthFraction, 4)} | {(region.Filled ? "**yes**" : "no")} |");
            text.AppendLine();
        }

        // --- pass 5 ---
        var tr = report.Trace;
        text.AppendLine("## Contour trace").AppendLine();
        text.AppendLine($"- ridge edges {tr.RidgeEdges}; creases drawn {tr.CreaseEdges}, buried inside fill {tr.BuriedEdges}");
        text.AppendLine($"- crease graph: **{tr.CreaseJunctions} junctions**, **{tr.CreaseLooseEnds} dead ends** " +
                        "(every one of these ends a chain, so this is what fragments a rim)");
        text.AppendLine($"- minimum contour length {N(tr.MinContourLength, 2)} mm; spacing {N(tr.Spacing, 3)} mm; lift {N(tr.Lift, 3)} mm");
        text.AppendLine($"- chains: {tr.ChainCount}; kept {tr.Chains.Count(c => c.Verdict == RidgeChainVerdict.Kept)}, " +
                        $"too short {tr.Chains.Count(c => c.Verdict == RidgeChainVerdict.TooShort)}");
        text.AppendLine();
        if (tr.Chains.Count > 0)
        {
            text.AppendLine("| mesh pts | length mm | len/diag | closed | verdict |");
            text.AppendLine("|---|---|---|---|---|");
            foreach (var chain in tr.Chains.Take(20))
                text.AppendLine($"| {chain.MeshPoints} | {N(chain.TracedLength, 1)} | {N(chain.TracedLengthOverDiagonal)} | " +
                                $"{chain.Closed} | {chain.Verdict} |");
            if (tr.Chains.Count > 20) text.AppendLine($"| … {tr.Chains.Count - 20} more | | | | |");
            text.AppendLine();
        }

        // --- band width profile, measured inside the detector ---
        var profile = diagnosis.BandProfile;
        if (profile.Available)
        {
            text.AppendLine("## Band width, per face").AppendLine();
            text.AppendLine("Distance across the band to one surface plus the distance to the other, " +
                            "measured at every face of the band. Suspect faces are those under half the " +
                            "median width of the band within four widths of them — local, so a shell that " +
                            "genuinely tapers moves its own expectation and is not flagged.");
            text.AppendLine();
            text.AppendLine($"- median width **{N(profile.MedianWidth, 2)} mm** over {profile.BandFaces} band faces");
            text.AppendLine($"- width p50 {N(profile.Width.P50, 2)}, p90 {N(profile.Width.P90, 2)}, " +
                            $"min {N(profile.Width.Min, 2)}, max {N(profile.Width.Max, 2)} mm");
            text.AppendLine($"- **suspect: {profile.SuspectFaces} faces, {N(profile.SuspectArea, 1)} mm² " +
                            $"({N(profile.SuspectAreaFraction * 100f, 1)}% of the band)**");
            text.AppendLine();
        }

        // --- bays ---
        var bays = quality.Bays;
        if (bays.Available)
        {
            text.AppendLine("## Bays in the band").AppendLine();
            text.AppendLine("Inlets of surface reaching into the band. A bay is continuous with the surface " +
                            "it comes from, so it belongs to no region and is enclosed by nothing — every " +
                            "connectivity test looks straight past it. Found instead by closing the band " +
                            "(dilate then erode) and taking the difference, which fills exactly the " +
                            "concavities narrower than the radius and leaves everything else untouched.");
            text.AppendLine();
            text.AppendLine($"- radius: **{N(bays.RadiusMm, 2)} mm** ({bays.RadiusSteps} face steps), " +
                            $"one band width of {N(bays.BandWidth, 2)} mm");
            text.AppendLine($"- band area {N(bays.BandArea, 1)} mm²; **bay area {N(bays.BayArea, 1)} mm² " +
                            $"({N(bays.BayAreaFraction * 100f, 1)}% of the band)**");
            text.AppendLine($"- **{bays.Count} bays**");
            text.AppendLine();

            if (bays.Bays.Count > 0)
            {
                text.AppendLine("| faces | area mm² | mouth mm | depth (area/mouth) mm |");
                text.AppendLine("|---|---|---|---|");
                foreach (var bay in bays.Bays)
                    text.AppendLine($"| {bay.Faces} | {N(bay.Area, 1)} | {N(bay.MouthLength, 1)} | " +
                                    $"{N(bay.Depth, 2)} |");
                text.AppendLine();
                text.AppendLine("A deep bay on a narrow mouth is an intrusion into the wall. One whose mouth " +
                                "is as wide as it is deep is just the band's edge being locally concave, and " +
                                "closing it would be inventing wall that is not there.");
                text.AppendLine();
            }

            if (bays.Sweep.Count > 0)
            {
                text.AppendLine("### Across radii").AppendLine();
                text.AppendLine("| radius (band widths) | mm | steps | bays | bay area mm² | % of band |");
                text.AppendLine("|---|---|---|---|---|---|");
                foreach (var step in bays.Sweep)
                    text.AppendLine(
                        $"| {N(step.WidthFraction, 1)}× | {N(step.RadiusMm, 1)} | {step.Steps} | {step.Count} | " +
                        $"{N(step.BayArea, 1)} | " +
                        $"{N(bays.BandArea > 0 ? step.BayArea / bays.BandArea * 100f : 0f, 1)} |");
                text.AppendLine();
                text.AppendLine("Area climbing gently with the radius is the band's own sawtooth edge being " +
                                "nibbled at. A pocket that appears all at once has a mouth of that width. " +
                                "One that never appears is not a concavity at all — it is the band ending " +
                                "and the surface carrying on, and no closing will recover it.");
                text.AppendLine();
            }

            text.AppendLine("Nothing here has been written back — this is what a closing *would* fill.");
            text.AppendLine();
        }

        // --- band width ---
        var paired = quality.BandWidths.Where(w => w.Paired).ToList();
        if (paired.Count > 0)
        {
            text.AppendLine("## Band width").AppendLine();
            text.AppendLine("Distance from each point of a contour to the contour bounding the other side " +
                            "of the same wall. A rim is a shell of near-constant thickness swept round the " +
                            "piece, so this should barely vary — an excursion is either a hole in the band " +
                            "or an irregularity in the body itself.");
            text.AppendLine();
            text.AppendLine("| contour | partner | median mm | p5 | p95 | min | max | CoV | outliers |");
            text.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var w in paired)
                text.AppendLine($"| {w.ContourIndex} | {w.PartnerIndex} | {N(w.Median, 2)} | {N(w.P5, 2)} | " +
                                $"{N(w.P95, 2)} | {N(w.Minimum, 2)} | {N(w.Maximum, 2)} | " +
                                $"**{N(w.CoefficientOfVariation, 3)}** | {w.OutlierPoints} " +
                                $"({N(w.OutlierFraction * 100f, 1)}%) |");
            text.AppendLine();
            text.AppendLine($"`CoV` is standard deviation over median — 0 is a perfectly even band. " +
                            $"`outliers` counts points outside 0.6×–1.6× the median, measured against the " +
                            "median rather than the mean so one bad stretch cannot hide itself by moving " +
                            "the reference.");
            text.AppendLine();

            text.AppendLine("### Is the variation the body's, or the measurement's?").AppendLine();
            text.AppendLine("The rim wall is the shell's own thickness seen edge-on, so where the shell " +
                            "tapers the band must narrow with it. Width that collapses while the thickness " +
                            "beside it holds steady has nothing in the body to explain it.");
            text.AppendLine();
            text.AppendLine("| contour | samples | corr(width, thickness) | width/thickness | normal w/t | outlier w/t | verdict |");
            text.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var w in paired)
                text.AppendLine($"| {w.ContourIndex} | {w.ThicknessSamples} | {S(w.WidthThicknessCorrelation)} | " +
                                $"{N(w.MedianWidthOverThickness, 2)} | " +
                                $"{N(w.NormalMedianWidth, 2)} / {N(w.NormalMedianThickness, 2)} | " +
                                $"{N(w.OutlierMedianWidth, 2)} / {N(w.OutlierMedianThickness, 2)} | " +
                                $"{(w.OutlierPoints == 0 ? "no outliers" : w.TracksThickness ? "**body is thin there**" : "**unexplained**")} |");
            text.AppendLine();
            text.AppendLine("`w/t` pairs are median band width / median local wall thickness for that group " +
                            "of points. If the outlier group's thickness drops in step with its width, the " +
                            "body is genuinely thin there and the band is right to follow it. If only the " +
                            "width drops, suspect the measurement — on a body with more than one rim the " +
                            "likeliest cause is the contour pairing having jumped to the other rim.");
            text.AppendLine();
        }

        // --- wall thickness, and the ridge judged against it ---
        var wt = quality.Thickness;
        text.AppendLine("## Wall thickness").AppendLine();
        if (!wt.Available)
        {
            text.AppendLine($"- unavailable: `{wt.Error}`").AppendLine();
        }
        else
        {
            text.AppendLine($"- **median {N(wt.Median, 2)} mm**, mean {N(wt.Mean, 2)}, sd {N(wt.StandardDeviation, 2)}");
            text.AppendLine($"- range {N(wt.Minimum, 2)} – {N(wt.Maximum, 2)} mm; p5 {N(wt.FifthPercentile, 2)}, p95 {N(wt.NinetyFifthPercentile, 2)}");
            text.AppendLine($"- measured {wt.MeasuredFaces} of {wt.TotalFaces} faces; " +
                            $"**unmeasured {N(wt.UnmeasuredFraction * 100f, 1)}%** (roughly the rim)");
            text.AppendLine();

            text.AppendLine("### Ridge band vs a rim mask from thickness").AppendLine();
            text.AppendLine("Independent of the ridge: the ridge is a dihedral angle between neighbouring " +
                            "faces, this is a ray fired through the solid. Area-weighted, because face " +
                            "areas here span orders of magnitude.");
            text.AppendLine();
            text.AppendLine("| mask | precision | recall | IoU | band only mm² | thickness only mm² |");
            text.AppendLine("|---|---|---|---|---|---|");
            foreach (var a in wt.Agreements)
                text.AppendLine($"| {a.Mask} | {N(a.Precision, 3)} | {N(a.Recall, 3)} | {N(a.IoU, 3)} | " +
                                $"{N(a.RidgeOnlyArea, 1)} | {N(a.ThicknessOnlyArea, 1)} |");
            text.AppendLine();
            text.AppendLine("*precision* = share of the ridge band that thickness also calls rim. " +
                            "*recall* = share of the thickness rim the band covers.");
            text.AppendLine();

            text.AppendLine($"- unmeasured area **inside** the band: {N(wt.UnmeasuredAreaInsideBand, 1)} mm²");
            text.AppendLine($"- unmeasured area **outside** it: {N(wt.UnmeasuredAreaOutsideBand, 1)} mm² " +
                            "(rim the band did not mark)");
            text.AppendLine();
            text.AppendLine($"measured thickness **inside** the band: p50 {N(wt.InsideBand.P50, 2)}, " +
                            $"p90 {N(wt.InsideBand.P90, 2)}, max {N(wt.InsideBand.Max, 2)} mm ({wt.InsideBand.Count} faces)");
            text.AppendLine();
            text.AppendLine($"measured thickness **outside** it: p50 {N(wt.OutsideBand.P50, 2)}, " +
                            $"p90 {N(wt.OutsideBand.P90, 2)}, max {N(wt.OutsideBand.Max, 2)} mm ({wt.OutsideBand.Count} faces)");
            text.AppendLine();
            text.AppendLine("A correct band reads unmeasured or far from the median inside, and tight " +
                            "around the median outside. Band area sitting at the median is a candidate " +
                            "false positive; unmeasured area outside the band is a candidate miss.");
            text.AppendLine();
            text.AppendLine("**This is not an oracle.** A genuinely thick region reads unmeasured too once " +
                            "it passes the 25 mm probe limit, and a probe grazing a rim exits " +
                            "unpredictably. Disagreement says where to look, not who is wrong.");
            text.AppendLine();
        }

        // --- comparison ---
        text.AppendLine("## Against the ThicknessParting seam").AppendLine();
        if (quality.SeamError is { Length: > 0 })
        {
            text.AppendLine($"- seam unavailable: `{quality.SeamError}`");
        }
        else
        {
            text.AppendLine($"- seam loops: {quality.SeamLoopCount}, total length {N(quality.SeamTotalLength, 1)} mm");
            text.AppendLine($"- ridge → seam: mean {N(quality.SeamMeanDistance, 2)} mm, median {N(quality.SeamMedianDistance, 2)} mm, " +
                            $"p95 {N(quality.SeamP95Distance, 2)} mm");
            text.AppendLine($"- seam → ridge: mean {N(quality.SeamToRidgeMean, 2)} mm");
            text.AppendLine($"- symmetric Hausdorff: {N(quality.SeamHausdorffSymmetric, 2)} mm");
        }
        text.AppendLine();

        if (quality.Contours.Count > 0)
        {
            text.AppendLine("## Per contour").AppendLine();
            text.AppendLine("| # | pts | closed | length mm | worst turn° | self-clearance mm | turning no. | mean dist to seam mm |");
            text.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var c in quality.Contours)
                text.AppendLine($"| {c.Index} | {c.Points} | {c.Closed} | {N(c.Length, 1)} | {N(c.WorstTurnDegrees, 1)} | " +
                                $"{N(c.MinSelfClearance, 2)} | {S(c.TurningNumber)} | {N(c.SeamMeanDistance, 2)} |");
            text.AppendLine();
        }

        if (quality.ContourSeparations.Count > 0)
        {
            text.AppendLine("## Separation by closed contour").AppendLine();
            text.AppendLine("| contour | components | largest % | second % | separates |");
            text.AppendLine("|---|---|---|---|---|");
            foreach (var sep in quality.ContourSeparations)
                text.AppendLine($"| {sep.ContourIndex} | {sep.Components} | {N(sep.LargestShare * 100f, 1)} | " +
                                $"{N(sep.SecondShare * 100f, 1)} | {(sep.Separates ? "yes" : "**no**")} |");
            text.AppendLine();

            if (report.Surface.Genus > 0)
                text.AppendLine($"This body is genus {report.Surface.Genus}, so a closed contour failing to " +
                                "separate is not necessarily a miss — a curve running round a hole divides " +
                                "nothing, and up to " + report.Surface.Genus + " independent cycles can do that.")
                    .AppendLine();
        }

        File.WriteAllText(Path.Combine(directory, "report.md"), text.ToString());

        // A contour with no non-adjacent approach has infinite self-clearance, and a region bounded by
        // no ridge has infinite mean width. Both are meaningful answers, so they are written as named
        // literals rather than clamped to a number that would read as a measurement.
        File.WriteAllText(
            Path.Combine(directory, "report.json"),
            JsonSerializer.Serialize(new { model, quality, report },
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    IncludeFields = true,
                    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
                }));
    }

    public static void WriteSummary(string directory, IReadOnlyList<RidgeSummaryRow> rows)
    {
        if (rows.Count == 0) return;
        Directory.CreateDirectory(directory);

        var text = new StringBuilder();
        text.AppendLine("# Ridge detection across the bolus set").AppendLine();
        text.AppendLine("| model | faces | genus | contours | closed | wall mm | band mm | band CoV | outlier % | unmeas % | recall | ridge↔seam mm |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");

        foreach (var row in rows)
        {
            var q = row.Quality;
            var r = row.Report;
            var wt = q.Thickness;
            var unmeasured = wt.Agreements.FirstOrDefault(a => a.Mask == "unmeasured");
            var paired = q.BandWidths.Where(w => w.Paired).ToList();

            text.AppendLine(
                $"| {row.Model} | {r.Surface.Faces} | {r.Surface.Genus} | {q.ContourCount} | {q.ClosedCount} | " +
                $"{(wt.Available ? N(wt.Median, 2) : "-")} | " +
                $"{(paired.Count == 0 ? "-" : N(paired.Average(w => w.Median), 2))} | " +
                $"{(paired.Count == 0 ? "-" : N(paired.Max(w => w.CoefficientOfVariation), 3))} | " +
                $"{(paired.Count == 0 ? "-" : N(paired.Max(w => w.OutlierFraction) * 100f, 1))} | " +
                $"{(wt.Available ? N(wt.UnmeasuredFraction * 100f, 1) : "-")} | " +
                $"{(unmeasured is null ? "-" : N(unmeasured.Recall, 2))} | " +
                $"{N(q.SeamMeanDistance, 2)} |");
        }

        text.AppendLine();
        text.AppendLine("`band CoV` is the worst contour's width variation (sd/median); `outlier %` is the " +
                        "share of its points outside 0.6×–1.6× the median width. These are the only columns " +
                        "that can see a hole in the band — curvature and thickness both call such a spot " +
                        "surface, and agree with each other while doing it.");
        text.AppendLine();
        text.AppendLine("`unmeas %` is the share of faces whose inward probe never exited — roughly the rim, " +
                        "and an estimate of it that owes nothing to the curvature the ridge is found by.");
        text.AppendLine();
        text.AppendLine("`band∩thick IoU` and `recall` compare the ridge band against that mask, weighted by " +
                        "area. High recall with lower precision is the expected shape: the band is a wall with " +
                        "width, the unmeasured mask is only the part of it that looks along the shell.");

        File.WriteAllText(Path.Combine(directory, "summary.md"), text.ToString());
    }
}
