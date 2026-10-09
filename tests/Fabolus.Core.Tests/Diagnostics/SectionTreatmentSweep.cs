using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// Treating by diagnosis against treating alike, across the whole set.
///
/// <para>
/// Standard alone settles nothing here. It has no pinched stretch, so the one category that has a
/// treatment the blunt pass cannot express - <see cref="PartingLineCondition.Necked"/>, where the wall
/// has no middle and the right thing to do is nothing - never comes up on it. The bodies that decide
/// whether diagnosing is worth its complexity are the ones with a category mix, and there is one.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class SectionTreatmentSweep
{
    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;
    private readonly ITestOutputHelper _log;

    public SectionTreatmentSweep(GeometryEngineFixture assets, ITestOutputHelper log)
    {
        _assets = assets;
        _engine = assets.Engine;
        _log = log;
    }

    [DiagnosticFact]
    public void WhereDiagnosingEarnsItsKeep()
    {
        foreach (var (id, asset) in PartingLineCentringSweep.Models)
        {
            var imported = SavedMoulds.Open(_engine, _assets.GetAssetPath(asset));
            var mould = SavedMoulds.Mould(imported.Value);
            var body = new PartingMeshFeature(_engine).GetBodyMesh(mould.Value).Value.Mesh;

            var thickness = WallThickness.Measure(_engine, body, WallThicknessOptions.Default);
            if (thickness.IsFailure) { _log.WriteLine($"{id}: no thickness"); continue; }

            var projector = new PartingTools(_engine).CreateSurfaceProjector(body);
            var surface = projector.IsSuccess ? projector.Value : null;

            var ridge = RidgeDetection.FindRidge(body, RidgeDetectionOptions.Default);
            var contours = ridge.Contours.Where(c => c.IsClosed).ToList();
            var walls = PartingStrategy.Rims(contours, thickness.Value.Statistics.Median)
                .Where(r => r.Kind == PartingRimKind.Wall).ToList();

            if (walls.Count == 0) { _log.WriteLine($"{id}: no wall rim"); continue; }

            _log.WriteLine($"=== {id}");

            for (int w = 0; w < walls.Count; w++)
            {
                var band = new PartingBand(
                    contours[walls[w].ContourIndices[0]], contours[walls[w].ContourIndices[1]]);

                var traced = CreaseOffsetLine.Trace(body, band, surface);
                if (traced is null) { _log.WriteLine($"  rim {w}: no line"); continue; }

                var line = traced.ToArray();
                var read = PartingLineSections.Analyse(line, band);

                _log.WriteLine(
                    $"  rim {w}: {line.Length} samples  " +
                    $"sound {read.ShareIn(PartingLineCondition.Sound):P0} " +
                    $"detour {read.ShareIn(PartingLineCondition.Detour):P0} " +
                    $"adrift {read.ShareIn(PartingLineCondition.Adrift):P0} " +
                    $"necked {read.ShareIn(PartingLineCondition.Necked):P0} " +
                    $"kinked {read.ShareIn(PartingLineCondition.Kinked):P0}");

                var aimedOnly = PartingLineTreatment.Apply(
                    line, band, out _,
                    PartingLineTreatmentOptions.Default with { PolishPasses = 0 }, surface);
                var aimed = PartingLineTreatment.Apply(line, band, out _, null, surface);

                _log.WriteLine("           nearest  off-mid  across p5  sound   turn p95/max  step   clear");
                _log.WriteLine($"    raw    {Describe(line, band)}");
                _log.WriteLine($"    aimed- {Describe(aimedOnly, band)}");
                _log.WriteLine($"    aimed  {Describe(aimed, band)}");
            }
        }
    }

    private static string Describe(IReadOnlyList<Vector3> loop, PartingBand band)
    {
        var report = PartingLineSections.Analyse(loop, band);
        int n = loop.Count;

        var turns = new double[n];
        var steps = new double[n];
        var across = new double[n];

        for (int i = 0; i < n; i++)
        {
            across[i] = report.Samples[i].Across;
            turns[i] = report.Samples[i].Turn;
            steps[i] = loop[i].DistanceTo(loop[(i + 1) % n]);
        }

        const int Skip = 6;
        double clear = double.PositiveInfinity;
        for (int i = 0; i < n; i++)
            for (int j = i + Skip; j < n; j++)
            {
                if (n - (j - i) < Skip) continue;
                clear = Math.Min(clear, loop[i].DistanceTo(loop[j]));
            }

        Array.Sort(turns);
        var sortedSteps = (double[])steps.Clone();
        Array.Sort(sortedSteps);
        var sortedAcross = (double[])across.Clone();
        Array.Sort(sortedAcross);

        int off = across.Count(v => v < 0.35 || v > 0.65);

        return $"{report.Nearest,+7:0.00}  {(double)off / n,7:P1}  {sortedAcross[(int)(n * 0.05)],+9:0.00}  " +
               $"{report.ShareIn(PartingLineCondition.Sound),6:P1}  " +
               $"{turns[(int)(n * 0.95)],8:F0}/{turns[^1],-4:F0} " +
               $"{sortedSteps[^1] / sortedSteps[n / 2],4:F1}x  {clear,5:F2}";
    }
}
