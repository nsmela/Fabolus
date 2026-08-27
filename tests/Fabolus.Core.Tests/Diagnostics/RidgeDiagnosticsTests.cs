using System.Numerics;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// The diagnostics hook threads a collector through the private passes, so its whole claim is that it
/// changes nothing. These pin that down: the reported answer must equal the answer the public API
/// gives, on shapes that exercise every bail-out path, and the report must be internally consistent
/// with itself.
/// </summary>
[Collection("GeometryEngine collection")]
public class RidgeDiagnosticsTests
{
    private readonly IGeometryEngine _engine;

    public RidgeDiagnosticsTests(GeometryEngineFixture fixture) => _engine = fixture.Engine;

    public static TheoryData<string> Shapes => new() { "puck", "sphere", "sheet", "cube" };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Diagnose_ReturnsTheSameAnswerAsThePublicApi(string shape)
    {
        var mesh = Shape(shape);
        var options = RidgeDetectionOptions.Default;

        var diagnosis = RidgeDetection.Diagnose(mesh, options);

        diagnosis.RidgeFaces.Should().Equal(RidgeDetection.FindRidgeFaces(mesh, options));

        var expected = RidgeDetection.FindRidgeContours(mesh, options);
        diagnosis.Contours.Select(c => (c.Points.Count, c.IsClosed))
            .Should().Equal(expected.Select(c => (c.Points.Count, c.IsClosed)));
    }

    /// <summary>
    /// The combined call exists so a caller showing the ridge as shaded facets with its curve over the
    /// top runs the analysis once. Its whole value depends on giving the same answer as the pair.
    /// </summary>
    [Theory]
    [MemberData(nameof(Shapes))]
    public void FindRidge_AgreesWithTheSeparateCalls(string shape)
    {
        var mesh = Shape(shape);
        var options = RidgeDetectionOptions.Default;

        var combined = RidgeDetection.FindRidge(mesh, options);

        combined.Faces.Should().Equal(RidgeDetection.FindRidgeFaces(mesh, options));
        combined.Contours.Select(c => (c.Points.Count, c.IsClosed))
            .Should().Equal(RidgeDetection.FindRidgeContours(mesh, options)
                .Select(c => (c.Points.Count, c.IsClosed)));
    }

    [Fact]
    public void FindRidge_OnAnEmptyMesh_ComesBackEmptyRatherThanThrowing()
    {
        var combined = RidgeDetection.FindRidge(null!, RidgeDetectionOptions.Default);

        combined.Faces.Should().BeEmpty();
        combined.Contours.Should().BeEmpty();
    }

    /// <summary>
    /// A sphere has no ridge, so the analysis bails out before it returns anything. That is precisely
    /// the case a return-value design could not report on, so the report has to survive it.
    /// </summary>
    [Fact]
    public void Diagnose_StillReportsWhenThereIsNoRidgeToFind()
    {
        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, 30.0, 48).Value;

        var diagnosis = RidgeDetection.Diagnose(sphere, RidgeDetectionOptions.Default);

        diagnosis.Contours.Should().BeEmpty();
        diagnosis.RidgeFaces.Should().OnlyContain(f => !f);

        var report = diagnosis.Report;
        report.Surface.Faces.Should().Be(sphere.Triangles.Length / 3);
        report.Surface.Edges.Should().BeGreaterThan(0);
        report.Threshold.Runs.Should().OnlyContain(r => r.Verdict != RidgeRunVerdict.Kept,
            "nothing survived, and the report must say which test each run failed");
    }

    [Fact]
    public void Diagnose_RegionAreasAccountForTheWholeSurface()
    {
        var puck = Puck();

        var report = RidgeDetection.Diagnose(puck, RidgeDetectionOptions.Default).Report;

        report.Fill.RegionCount.Should().BeGreaterThan(1, "a rim divides the surface into regions");
        report.Fill.Regions.Sum(r => r.AreaFraction).Should().BeApproximately(1f, 0.01f);
        report.Fill.FilledRegions.Should().BeGreaterThan(0, "the rim band of a puck fills");
    }

    [Fact]
    public void Diagnose_KeptRunEdgesAddUpToTheRidgeItThresholded()
    {
        var puck = Puck();

        var report = RidgeDetection.Diagnose(puck, RidgeDetectionOptions.Default).Report;

        report.Threshold.PercolationGuardFired.Should().BeFalse();
        report.Threshold.Runs.Where(r => r.Verdict == RidgeRunVerdict.Kept).Sum(r => r.EdgeCount)
            .Should().Be(report.Threshold.KeptEdgesBeforeGuard);
        report.Bridging.RidgeEdgesBefore.Should().Be(report.Threshold.KeptEdges);
    }

    private IMesh Shape(string name) => name switch
    {
        "puck" => Puck(),
        "sphere" => _engine.Generators.GenerateSphere(Vector3.Zero, 30.0, 48).Value,
        "sheet" => Sheet(),
        _ => new GeometryEngineFixture().UnitCube(),
    };

    /// <summary>A disc given thickness - a bolus shell in miniature, with a 90 degree rim all round.</summary>
    private IMesh Puck(float radius = 150f, float thickness = 20f, int steps = 96, int rings = 4)
    {
        var vertices = new List<double>();
        var index = new int[steps, (2 * rings) + 2];

        int bottomCentre = Add(vertices, new Vector3(0, -thickness / 2f, 0));
        int topCentre = Add(vertices, new Vector3(0, thickness / 2f, 0));

        for (int s = 0; s < steps; s++)
        {
            float angle = 2f * MathF.PI * s / steps;
            float cos = MathF.Cos(angle), sin = MathF.Sin(angle);

            int p = 0;
            index[s, p++] = bottomCentre;
            for (int m = 1; m <= rings; m++)
                index[s, p++] = Add(vertices, new Vector3(radius * m / rings * cos, -thickness / 2f, radius * m / rings * sin));
            for (int m = rings; m >= 1; m--)
                index[s, p++] = Add(vertices, new Vector3(radius * m / rings * cos, thickness / 2f, radius * m / rings * sin));
            index[s, p] = topCentre;
        }

        var triangles = new List<int>();
        int profile = (2 * rings) + 2;
        for (int s = 0; s < steps; s++)
        {
            int next = (s + 1) % steps;
            for (int p = 0; p < profile - 1; p++)
            {
                int a = index[s, p], b = index[s, p + 1];
                int c = index[next, p], d = index[next, p + 1];
                if (a != c) triangles.AddRange(new[] { a, b, c });
                if (b != d) triangles.AddRange(new[] { b, d, c });
            }
        }

        return _engine.CreateMesh(vertices.ToArray(), triangles.ToArray()).Value;
    }

    /// <summary>A flat square. No fold anywhere, so the candidate set comes back empty.</summary>
    private IMesh Sheet(int n = 12, float size = 100f)
    {
        var vertices = new List<double>();
        for (int y = 0; y <= n; y++)
            for (int x = 0; x <= n; x++)
                Add(vertices, new Vector3(size * x / n, 0f, size * y / n));

        var triangles = new List<int>();
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int a = (y * (n + 1)) + x;
                triangles.AddRange(new[] { a, a + 1, a + n + 1, a + 1, a + n + 2, a + n + 1 });
            }

        return _engine.CreateMesh(vertices.ToArray(), triangles.ToArray()).Value;
    }

    private static int Add(List<double> into, Vector3 v)
    {
        into.Add(v.X); into.Add(v.Y); into.Add(v.Z);
        return (into.Count / 3) - 1;
    }
}
