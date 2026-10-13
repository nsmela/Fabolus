using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// Runs ridge detection over every body at a range of grow thresholds and reports what each one does
/// to the band.
///
/// <para>
/// Kept because it is the only thing that can justify touching a threshold. Tuning one on the body
/// that is misbehaving is how the other seven get broken quietly: lowering the grow pair to 0.15/20
/// repairs <c>standard</c>'s band completely and wrecks <c>nose</c>'s in the same step, and going to
/// 0.10/15 trips the percolation guard on <c>eye</c> and <c>larynx-small</c> so they report no ridge
/// at all. None of that is visible from one model.
/// </para>
///
/// <para>
/// Prints rather than asserts. There is no right answer to bake in here - the numbers are the finding,
/// and a threshold change is a judgement made against the whole table.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class RidgeThresholdSensitivity
{
    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;
    private readonly ITestOutputHelper _log;

    public RidgeThresholdSensitivity(GeometryEngineFixture assets, ITestOutputHelper log)
    {
        _assets = assets;
        _engine = assets.Engine;
        _log = log;
    }

    [DiagnosticFact]
    public void ThresholdSensitivity()
    {
        var models = new (string Id, string Asset)[]
        {
            ("chin", "3mf/chin.3mf"),
            ("ear", "3mf/ear.3mf"),
            ("eye", "3mf/eye.3mf"),
            ("larynx-large", "3mf/larynx_large.3mf"),
            ("larynx-small", "3mf/larynx_small.3mf"),
            ("nose", "3mf/nose.3mf"),
            ("scalp", "3mf/scalp.3mf"),
            ("standard", "3mf/test bolus standard.3mf"),
        };

        var cases = new (string Name, RidgeDetectionOptions Options)[]
        {
            ("0.20/25 default", RidgeDetectionOptions.Default),
            ("0.15/20", RidgeDetectionOptions.Default with { GrowCurvature = 0.15, GrowAngleDegrees = 20.0 }),
            ("0.10/15", RidgeDetectionOptions.Default with { GrowCurvature = 0.10, GrowAngleDegrees = 15.0 }),
        };

        foreach (var (id, asset) in models)
        {
            var imported = SavedMoulds.Open(_engine, _assets.GetAssetPath(asset));
            var mould = SavedMoulds.Mould(imported.Value);
            var body = new PartingMeshFeature(_engine).GetBodyMesh(mould.Value).Value.Mesh;

            foreach (var (name, options) in cases)
            {
                var d = RidgeDetection.Diagnose(body, options);
                var t = d.Report.Threshold;

                var (median, cov, min, outliers) = Width(d.Contours);
                _log.WriteLine(
                    $"ROW {id,-13} {name,-16} {d.Contours.Count,3} {d.Contours.Count(c => c.IsClosed),3} " +
                    $"{median,7:F2} {cov,7:F3} {min,7:F2} {outliers,6:P0} " +
                    $"{t.KeptEdges,6} {(t.PercolationGuardFired ? "GUARD" : "-"),6}");
            }
        }
    }

    /// <summary>Width from contour 0 to its nearest partner, as the harness measures it.</summary>
    private static (double Median, double Cov, double Min, double Outliers) Width(IReadOnlyList<RidgeContour> contours)
    {
        if (contours.Count < 2) return (0, 0, 0, 0);

        int partner = -1;
        double closest = double.MaxValue;
        for (int j = 1; j < contours.Count; j++)
        {
            double mean = contours[0].Points.Average(p => Nearest(p, contours[j]));
            if (mean >= closest) continue;
            closest = mean;
            partner = j;
        }

        var w = contours[0].Points.Select(p => Nearest(p, contours[partner])).ToArray();
        var sorted = (double[])w.Clone();
        Array.Sort(sorted);

        double median = sorted[sorted.Length / 2];
        double mean2 = w.Average();
        double sd = Math.Sqrt(w.Sum(x => (x - mean2) * (x - mean2)) / w.Length);
        int outliers = w.Count(x => x < median * 0.6 || x > median * 1.6);

        return (median, median > 1e-6 ? sd / median : 0.0, sorted[0], (double)outliers / w.Length);
    }

    private static double Nearest(Vector3 point, RidgeContour contour)
    {
        var pts = contour.Points;
        int spans = contour.IsClosed ? pts.Count : pts.Count - 1;

        double best = double.MaxValue;
        for (int i = 0; i < spans; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            var ab = b - a;
            double len2 = ab.LengthSquared;
            double t = len2 < 1e-12 ? 0.0 : Math.Clamp((point - a).Dot(ab) / len2, 0.0, 1.0);
            best = Math.Min(best, point.DistanceTo(a + (ab * t)));
        }
        return best;
    }
}
