using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// The parting line has to lie on the body, whichever tracer produced it.
///
/// <para>
/// This is not cosmetic. The flange's inner rim is placed from these points and has to seat against
/// the mould cavity, so a line floating off the body seats the halves against nothing; and the line
/// is what the user approves in the viewport, where a point off the surface reads as the line
/// jumping a gap.
/// </para>
///
/// <para>
/// The silhouette tracer has always held this, by smoothing against the surface. The border tracer
/// did not: it relaxed its loop free of the body, and relaxation moves each point toward the
/// midpoint of its neighbours - a chord - so the loop cut across every concavity it ran through, by
/// up to 1.28mm on larynx_bolus.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
public class PartingLineOnSurfaceTests
{
    private readonly IGeometryEngine _engine;
    private readonly GeometryEngineFixture _fixture;

    public PartingLineOnSurfaceTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _engine = fixture.Engine;
    }

    /// <summary>
    /// Tolerance, in mm, for "on the surface". Not zero because the points are floats and the
    /// distance is computed independently of the projection that placed them, but far under the
    /// drift being guarded against - a hundredth of a millimetre against the 0.5-1.3mm excursions
    /// that made the line visibly leave the body.
    /// </summary>
    private const double OnSurfaceToleranceMm = 0.01;

    [Theory]
    [InlineData("chin_bolus.stl", PartingLineSource.ExtrusionBorder)]
    [InlineData("chin_bolus.stl", PartingLineSource.Silhouette)]
    [InlineData("scalp_bolus.stl", PartingLineSource.ExtrusionBorder)]
    [InlineData("nose_bolus.stl", PartingLineSource.ExtrusionBorder)]
    [InlineData("nose_bolus.stl", PartingLineSource.Silhouette)]
    [InlineData("larynx_bolus.stl", PartingLineSource.ExtrusionBorder)]
    [InlineData("larynx_bolus.stl", PartingLineSource.Silhouette)]
    public void EveryPointOfTheLineLiesOnTheBody(string file, PartingLineSource source)
    {
        var mesh = _fixture.LoadStl(file);
        var body = BodyMesh.Create(mesh);
        body.IsSuccess.Should().BeTrue();

        var line = new PartingMeshFeature(_engine).GeneratePartingLineFromBody(
            body.Value, new PartingLineParameters { Source = source, PullDirection = Vector3.UnitY });
        line.IsSuccess.Should().BeTrue(line.IsFailure ? line.Error.Description : "");

        var worst = line.Value.Loops
            .SelectMany(loop => loop)
            .Max(point => DistanceToMesh(point, mesh));

        worst.Should().BeLessThan(OnSurfaceToleranceMm,
            "a parting line off the body seats the flange against nothing, and reads as the line jumping a gap");
    }

    /// <summary>
    /// The complementary failure: a line can sit on the surface at every point and still jump, if it
    /// steps between two places that are far apart. Every step is between neighbouring points of a
    /// resampled loop, so one far longer than the rest is a chord drawn across the body.
    /// </summary>
    [Theory]
    [InlineData("chin_bolus.stl")]
    [InlineData("scalp_bolus.stl")]
    [InlineData("nose_bolus.stl")]
    [InlineData("larynx_bolus.stl")]
    public void TheLineStepsEvenlyRoundTheBorder(string file)
    {
        var mesh = _fixture.LoadStl(file);
        var body = BodyMesh.Create(mesh);
        body.IsSuccess.Should().BeTrue();

        var line = new PartingMeshFeature(_engine).GeneratePartingLineFromBody(
            body.Value,
            new PartingLineParameters {
                Source = PartingLineSource.ExtrusionBorder, PullDirection = Vector3.UnitY });
        line.IsSuccess.Should().BeTrue(line.IsFailure ? line.Error.Description : "");

        foreach (var loop in line.Value.Loops)
        {
            var steps = new List<double>(loop.Count);
            for (int i = 0; i < loop.Count; i++)
                steps.Add(loop[i].DistanceTo(loop[(i + 1) % loop.Count]));
            steps.Sort();

            double median = steps[steps.Count / 2];
            steps[^1].Should().BeLessThan(median * 3.0, "no step should be a chord across the body");
        }
    }

    /// <summary>
    /// Smoothing still has to do something. Holding the line on the surface would be trivially
    /// satisfied by not smoothing at all, which would leave the staircase of triangle edges the
    /// trace arrives as - so this pins down that the loop is both on the body and relaxed.
    /// </summary>
    [Fact]
    public void HoldingTheLineOnTheBodyDoesNotDisableTheSmoothing()
    {
        var mesh = _fixture.LoadStl("chin_bolus.stl");
        var body = BodyMesh.Create(mesh).Value;
        var feature = new PartingMeshFeature(_engine);

        var smoothed = feature.GeneratePartingLineFromThickness(body);
        var raw = feature.GeneratePartingLineFromThickness(
            body, ThicknessPartingOptions.Default with { SmoothingPasses = 0 });

        smoothed.IsSuccess.Should().BeTrue();
        raw.IsSuccess.Should().BeTrue();

        // Turn angle is what smoothing acts on: the trace zig-zags from vertex to vertex, and
        // relaxing it should measurably straighten that out.
        MedianTurnDegrees(smoothed.Value.Loops[0])
            .Should().BeLessThan(MedianTurnDegrees(raw.Value.Loops[0]),
                "the relaxation should still be relaxing, not just projecting");
    }

    private static double MedianTurnDegrees(IReadOnlyList<Vector3> loop)
    {
        var turns = new List<double>(loop.Count);
        for (int i = 0; i < loop.Count; i++)
        {
            var incoming = loop[i] - loop[(i - 1 + loop.Count) % loop.Count];
            var outgoing = loop[(i + 1) % loop.Count] - loop[i];
            if (incoming.Length < 1e-6 || outgoing.Length < 1e-6) continue;

            double cos = Math.Clamp(
                incoming.Normalize().Dot(outgoing.Normalize()), -1.0, 1.0);
            turns.Add(Math.Acos(cos) * 180.0 / Math.PI);
        }

        turns.Sort();
        return turns[turns.Count / 2];
    }

    /// <summary>Unsigned distance from a point to the nearest triangle. Brute force; fine at this size.</summary>
    private static double DistanceToMesh(Vector3 point, IMesh mesh)
    {
        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;

        double best = double.MaxValue;
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            best = Math.Min(best, SquaredDistanceToTriangle(
                point, vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]]));
        }
        return Math.Sqrt(best);
    }

    /// <summary>Squared distance from a point to a triangle (Ericson, Real-Time Collision Detection).</summary>
    private static double SquaredDistanceToTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;

        double d1 = ab.Dot(p - a), d2 = ac.Dot(p - a);
        if (d1 <= 0.0 && d2 <= 0.0) return (p - a).LengthSquared;

        double d3 = ab.Dot(p - b), d4 = ac.Dot(p - b);
        if (d3 >= 0.0 && d4 <= d3) return (p - b).LengthSquared;

        double vc = (d1 * d4) - (d3 * d2);
        if (vc <= 0.0 && d1 >= 0.0 && d3 <= 0.0)
            return (p - (a + (ab * (d1 / (d1 - d3))))).LengthSquared;

        double d5 = ab.Dot(p - c), d6 = ac.Dot(p - c);
        if (d6 >= 0.0 && d5 <= d6) return (p - c).LengthSquared;

        double vb = (d5 * d2) - (d1 * d6);
        if (vb <= 0.0 && d2 >= 0.0 && d6 <= 0.0)
            return (p - (a + (ac * (d2 / (d2 - d6))))).LengthSquared;

        double va = (d3 * d6) - (d5 * d4);
        if (va <= 0.0 && (d4 - d3) >= 0.0 && (d5 - d6) >= 0.0)
            return (p - (b + ((c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)))))).LengthSquared;

        double denominator = 1.0 / (va + vb + vc);
        return (p - (a + (ab * (vb * denominator)) + (ac * (vc * denominator)))).LengthSquared;
    }
}
