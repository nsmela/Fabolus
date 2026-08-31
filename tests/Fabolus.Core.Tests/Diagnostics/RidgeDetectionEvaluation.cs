using System.Collections.Concurrent;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// Owns the output directory for a run and writes the cross-model summary once the last body has been
/// processed. A class fixture rather than static state, so xUnit's own lifetime decides when the
/// summary is written instead of test ordering.
/// </summary>
public sealed class RidgeEvaluationOutput : IDisposable
{
    public string Directory { get; }

    private readonly ConcurrentBag<RidgeSummaryRow> _rows = new();

    public RidgeEvaluationOutput()
    {
        Directory = Environment.GetEnvironmentVariable("FABOLUS_RIDGE_REPORT_DIR") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Path.GetTempPath(), "fabolus-ridge", DateTime.Now.ToString("yyyyMMdd-HHmmss"));

        System.IO.Directory.CreateDirectory(Directory);
    }

    internal void Add(RidgeSummaryRow row) => _rows.Add(row);

    public void Dispose() =>
        RidgeReportWriter.WriteSummary(Directory, _rows.OrderBy(r => r.Model).ToList());
}

/// <summary>
/// Runs ridge detection over the real bolus files and writes images and a report for each.
///
/// <para>
/// This is an instrument, not a gate. It asserts only that the pipeline got as far as producing a
/// body mesh and wrote its output; everything about the <em>quality</em> of the ridge is a number in
/// the report, because there are no agreed thresholds for those yet and inventing some here would
/// bake today's behaviour in as the specification. Excluded from ordinary runs by
/// <c>--filter "Category!=Diagnostics"</c>.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public sealed class RidgeDetectionEvaluation : IClassFixture<RidgeEvaluationOutput>
{
    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;
    private readonly RidgeEvaluationOutput _output;
    private readonly ITestOutputHelper _log;

    public RidgeDetectionEvaluation(
        GeometryEngineFixture assets, RidgeEvaluationOutput output, ITestOutputHelper log)
    {
        _assets = assets;
        _engine = assets.Engine;
        _output = output;
        _log = log;
    }

    /// <summary>
    /// Short ids as the first argument so <c>test bolus standard.3mf</c>'s spaces never have to survive
    /// a <c>--filter</c> expression.
    /// </summary>
    public static TheoryData<string, string> Bodies => new()
    {
        { "chin", "3mf/chin.3mf" },
        { "ear", "3mf/ear.3mf" },
        { "eye", "3mf/eye.3mf" },
        { "larynx-small", "3mf/larynx_small.3mf" },
        { "larynx-large", "3mf/larynx_large.3mf" },
        { "nose", "3mf/nose.3mf" },
        { "scalp", "3mf/scalp.3mf" },
        { "standard", "3mf/test bolus standard.3mf" },
    };

    [Theory]
    [MemberData(nameof(Bodies))]
    public void Evaluate(string id, string asset)
    {
        var imported = _engine.IO.Import(_assets.GetAssetPath(asset));
        imported.IsSuccess.Should().BeTrue(imported.IsFailure ? imported.Error.Description : "");

        var mould = MouldMesh.Create(imported.Value);
        mould.IsSuccess.Should().BeTrue(mould.IsFailure ? mould.Error.Description : "");

        // The same mesh the Parting Split scene runs ridge detection on: the base mesh with the
        // transform-stage commands replayed, not the saved mould geometry.
        var body = new PartingMeshFeature(_engine).GetBodyMesh(mould.Value);
        body.IsSuccess.Should().BeTrue(body.IsFailure ? body.Error.Description : "");
        var surface = body.Value.Mesh;

        var options = RidgeDetectionOptions.Default;
        var diagnosis = RidgeDetection.Diagnose(surface, options);

        var measured = TraceSeam(surface);

        var quality = RidgeMetrics.Evaluate(
            surface, diagnosis, measured.Seam, measured.SeamError, options,
            measured.Thickness, measured.ThicknessError);
        var directory = Path.Combine(_output.Directory, id);

        RidgeImages.WriteAll(
            directory, id, surface, diagnosis, measured.Seam, quality.Thickness, quality.BandWidths,
            quality.Bays);
        RidgeReportWriter.WriteModel(directory, id, diagnosis, quality);
        _output.Add(new RidgeSummaryRow(id, quality, diagnosis.Report));

        File.Exists(Path.Combine(directory, "sheet.png")).Should().BeTrue();
        File.Exists(Path.Combine(directory, "report.md")).Should().BeTrue();

        _log.WriteLine($"{id}: {diagnosis.Contours.Count} contours, " +
                       $"{quality.ClosedCount} closed → {directory}");
    }

    private readonly record struct Measured(
        WallThickness? Thickness, string? ThicknessError, PartingLine? Seam, string? SeamError);

    /// <summary>
    /// The thickness measurement and the comparison line it feeds, from one pass. Measuring is the most
    /// expensive step in the harness, and the ridge is now judged against the same numbers the seam was
    /// traced from rather than a second measurement of the same body.
    ///
    /// <para>
    /// Failure of either is a finding about the body rather than a failure of this test - a
    /// non-manifold body or one with no extrusion border genuinely has no seam to compare against, and
    /// recording which is more useful than refusing to report at all.
    /// </para>
    /// </summary>
    private Measured TraceSeam(IMesh surface)
    {
        var thickness = _engine.Evaluators.MeasureWallThickness(surface, WallThicknessOptions.Default);
        if (thickness.IsFailure) return new Measured(null, thickness.Error.Description, null, thickness.Error.Description);

        var projector = _engine.PartingTools.CreateSurfaceProjector(surface);
        var traced = ThicknessParting.Trace(
            surface, thickness.Value, ThicknessPartingOptions.Default,
            projector.IsSuccess ? projector.Value : null);

        return new Measured(
            thickness.Value, null,
            traced.IsSuccess ? traced.Value : null,
            traced.IsSuccess ? null : traced.Error.Description);
    }
}
