using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// Runs <see cref="PartingStrategy"/> over the bodies and prints what it decides for each.
///
/// <para>
/// Deliberately thin. The rule lives in the feature because the view has to reach it, and a test that
/// restated it here would be checking a copy against itself - the thing worth seeing is what the real
/// evaluator says about real bodies, which is what this prints.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class PartingStrategySweep
{
    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;
    private readonly ITestOutputHelper _log;

    public PartingStrategySweep(GeometryEngineFixture assets, ITestOutputHelper log)
    {
        _assets = assets;
        _engine = assets.Engine;
        _log = log;
    }

    [Fact]
    public void WhichSourceEachBodyWants()
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

        foreach (var (id, asset) in models)
        {
            var imported = _engine.IO.Import(_assets.GetAssetPath(asset));
            var mould = MouldMesh.Create(imported.Value);
            var body = new PartingMeshFeature(_engine).GetBodyMesh(mould.Value).Value.Mesh;

            // Whether the extrusion border can actually be traced, asked the way the app asks it.
            var thickness = _engine.Evaluators.MeasureWallThickness(body, WallThicknessOptions.Default);
            bool seam = false;
            string? seamError = thickness.IsFailure ? thickness.Error.Description : null;
            if (thickness.IsSuccess)
            {
                var projector = _engine.PartingTools.CreateSurfaceProjector(body);
                var traced = ThicknessParting.Trace(
                    body, thickness.Value, ThicknessPartingOptions.Default,
                    projector.IsSuccess ? projector.Value : null);

                seam = traced.IsSuccess;
                seamError = traced.IsSuccess ? null : traced.Error.Description;
            }

            float wall = thickness.IsSuccess ? Median(thickness.Value) : float.NaN;
            var report = PartingStrategy.Evaluate(
                body, seamAvailable: seam, seamError: seamError, wallThickness: wall);

            _log.WriteLine($"{id}");
            _log.WriteLine($"  shape {report.Shape}, chi {report.EulerCharacteristic}, " +
                           $"genus {report.Genus}, closed {report.IsClosed}");
            _log.WriteLine($"  contours {report.ClosedContours} closed, {report.SeparatingContours} " +
                           $"dividing, {report.NonSeparatingContours} not " +
                           $"(budget {report.NonSeparatingBudget}{(report.OverBudget ? ", OVER" : "")})");
            _log.WriteLine($"  all {report.ClosedContours} together: {report.Combined.SubstantialPieces} of {report.Combined.Components} pieces, " +
                           $"{report.Combined.LargestShare:P1} / {report.Combined.SecondShare:P1} " +
                           $"-> {(report.Combined.Separates ? "PARTS" : "does not part")} " +
                           $"(needs {report.CutsNeeded} cut(s))");
            foreach (var rim in report.Rims)
                _log.WriteLine(
                    $"  rim {rim.Id,3}: {rim.ContourIndices.Count} contour(s) " +
                    $"[{string.Join(",", rim.ContourIndices)}], {rim.Points,4} pts, spacing " +
                    $"{rim.Spacing,7:F2}mm ({rim.Spacing / wall:F2} x wall) -> " +
                    rim.Kind.ToString().ToUpperInvariant() +
                    $", line {rim.Line}");

            // What the scene will actually shade. The report groups contours by rim; this is the same
            // grouping applied to faces, and the two have to agree or the picture and the assessment
            // describe different bodies.
            var surfaces = RidgeDetection.FindRidge(body, RidgeDetectionOptions.Default);
            var perRim = new Dictionary<int, int>();
            for (int f = 0; f < surfaces.Faces.Length; f++)
            {
                if (!surfaces.Faces[f]) continue;

                int rim = surfaces.FaceRims.Length == surfaces.Faces.Length ? surfaces.FaceRims[f] : -1;
                perRim[rim] = perRim.GetValueOrDefault(rim) + 1;
            }

            _log.WriteLine("  shaded faces by rim: " + string.Join(", ", perRim
                .OrderByDescending(p => p.Value)
                .Select(p => $"rim {p.Key} = {p.Value}")));

            _log.WriteLine($"  seam {(report.SeamAvailable ? "traced" : $"failed - {report.SeamError}")}");
            _log.WriteLine($"  -> {report.Recommended?.ToString() ?? "neither"}: {report.Summary}");
            _log.WriteLine("");
        }
    }

    private static float Median(WallThickness thickness)
    {
        var measured = thickness.PerFace
            .Where(t => !float.IsPositiveInfinity(t) && t > 0f)
            .OrderBy(t => t)
            .ToArray();
        return measured.Length == 0 ? float.NaN : measured[measured.Length / 2];
    }
}
