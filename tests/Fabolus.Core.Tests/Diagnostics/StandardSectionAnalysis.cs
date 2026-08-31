using System.Numerics;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// What <see cref="PartingLineSections"/> makes of <c>standard</c>'s parting line, before anything is
/// done about it.
///
/// <para>
/// Read this before changing any treatment. The whole case for diagnosing first is that the faults are
/// not all the same fault, and that only shows up as a breakdown - a single "9.3% off centre" says
/// nothing about whether one repair or three are wanted.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class StandardSectionAnalysis
{
    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;
    private readonly ITestOutputHelper _log;

    public StandardSectionAnalysis(GeometryEngineFixture assets, ITestOutputHelper log)
    {
        _assets = assets;
        _engine = assets.Engine;
        _log = log;
    }

    [Fact]
    public void WhatTheLineIsMadeOf()
    {
        var (body, band, surface) = Load();
        var line = CreaseOffsetLine.Trace(body, band, surface)!.ToArray();

        var report = PartingLineSections.Analyse(line, band);

        _log.WriteLine($"standard: {report.Samples.Count} samples, nearest {report.Nearest:F2}");
        foreach (var condition in Enum.GetValues<PartingLineCondition>())
            _log.WriteLine(
                $"  {condition,-8} {report.SamplesIn(condition),4} samples  {report.ShareIn(condition),6:P1}");

        _log.WriteLine("  sections, in order round the rim:");
        foreach (var section in report.Sections)
        {
            if (section.Condition == PartingLineCondition.Sound && section.Count > 8)
            {
                _log.WriteLine($"    {section.Condition,-8} {section.Count,4} samples at {section.Start}");
                continue;
            }

            _log.WriteLine(
                $"    {section.Condition,-8} {section.Count,4} samples at {section.Start,4}  " +
                $"worst {section.Worst:F2}");
        }

        // The samples inside each faulty stretch, so the classification can be checked against the
        // readings behind it rather than taken on trust.
        foreach (var section in report.Sections)
        {
            if (section.Condition is PartingLineCondition.Sound) continue;

            _log.WriteLine($"  {section.Condition} run at {section.Start}:");
            for (int k = 0; k < section.Count; k++)
            {
                var sample = report.Samples[(section.Start + k) % report.Samples.Count];
                _log.WriteLine(
                    $"    across {sample.Across,+6:0.00}  clear {sample.Clearance,5:F2}  " +
                    $"width {sample.Width,5:F2}x  bulge {sample.Bulge,5:F2}  " +
                    $"turn {sample.Turn,5:F1}  step {sample.Step,4:F1}x");
            }
        }
    }

    /// <summary>
    /// The diagnosis-led treatment against the one it would replace, on the same line.
    /// </summary>
    [Fact]
    public void TreatingByDiagnosisAgainstTreatingAlike()
    {
        var (body, band, surface) = Load();
        var line = CreaseOffsetLine.Trace(body, band, surface)!.ToArray();

        // The aimed treatments alone, and the same with the finishing flow behind them, because the
        // difference between those two is the whole case for keeping a pass that runs everywhere.
        var aimedOnly = PartingLineTreatment.Apply(
            line, band, out _, PartingLineTreatmentOptions.Default with { PolishPasses = 0 }, surface);
        var aimed = PartingLineTreatment.Apply(line, band, out var aimedReport, null, surface);

        _log.WriteLine(
            "                          nearest  off-mid  across p5  sound   turn p95/max  step   clear   length");
        _log.WriteLine($"  untreated             {Describe(line, band)}");
        _log.WriteLine($"  diagnosis, no polish  {Describe(aimedOnly, band)}");
        _log.WriteLine($"  diagnosis + polish    {Describe(aimed, band)}");

        _log.WriteLine(
            $"  diagnosis: {aimedReport.Rounds} round(s), {aimedReport.Bridged} bridged, " +
            $"{aimedReport.Shifted} shifted, {aimedReport.Eased} eased, {aimedReport.Refused} refused");

        foreach (var condition in Enum.GetValues<PartingLineCondition>())
            _log.WriteLine(
                $"    {condition,-8} {aimedReport.Before.ShareIn(condition),6:P1} -> " +
                $"{aimedReport.After.ShareIn(condition),6:P1}");
    }

    private static string Describe(IReadOnlyList<Vector3> loop, PartingBand band)
    {
        var report = PartingLineSections.Analyse(loop, band);
        int n = loop.Count;

        var turns = new float[n];
        var steps = new float[n];
        float length = 0f;
        var across = new float[n];

        for (int i = 0; i < n; i++)
        {
            across[i] = report.Samples[i].Across;
            turns[i] = report.Samples[i].Turn;
            steps[i] = Vector3.Distance(loop[i], loop[(i + 1) % n]);
            length += steps[i];
        }

        const int Skip = 6;
        float clear = float.PositiveInfinity;
        for (int i = 0; i < n; i++)
            for (int j = i + Skip; j < n; j++)
            {
                if (n - (j - i) < Skip) continue;
                clear = MathF.Min(clear, Vector3.Distance(loop[i], loop[j]));
            }

        var sortedTurns = (float[])turns.Clone();
        Array.Sort(sortedTurns);
        var sortedSteps = (float[])steps.Clone();
        Array.Sort(sortedSteps);
        var sortedAcross = (float[])across.Clone();
        Array.Sort(sortedAcross);

        int off = across.Count(v => v < 0.35f || v > 0.65f);

        return $"{report.Nearest,+7:0.00}  {(float)off / n,7:P1}  {sortedAcross[(int)(n * 0.05f)],+9:0.00}  " +
               $"{report.ShareIn(PartingLineCondition.Sound),6:P1}  " +
               $"{sortedTurns[(int)(n * 0.95f)],8:F0}/{sortedTurns[^1],-4:F0} " +
               $"{sortedSteps[^1] / sortedSteps[n / 2],4:F1}x  {clear,5:F2}  {length,7:F1}";
    }

    private (IMesh Body, PartingBand Band, ISurfaceProjector? Surface) Load()
    {
        var imported = _engine.IO.Import(_assets.GetAssetPath("3mf/test bolus standard.3mf"));
        var mould = MouldMesh.Create(imported.Value);
        var body = new PartingMeshFeature(_engine).GetBodyMesh(mould.Value).Value.Mesh;

        var thickness = _engine.Evaluators.MeasureWallThickness(body, WallThicknessOptions.Default);
        var projector = _engine.PartingTools.CreateSurfaceProjector(body);
        var surface = projector.IsSuccess ? projector.Value : null;

        var ridge = RidgeDetection.FindRidge(body, RidgeDetectionOptions.Default);
        var contours = ridge.Contours.Where(c => c.IsClosed).ToList();
        var rim = PartingStrategy.Rims(contours, thickness.Value.Statistics.Median)
            .Single(r => r.Kind == PartingRimKind.Wall);

        return (body, new PartingBand(contours[rim.ContourIndices[0]], contours[rim.ContourIndices[1]]),
            surface);
    }
}
