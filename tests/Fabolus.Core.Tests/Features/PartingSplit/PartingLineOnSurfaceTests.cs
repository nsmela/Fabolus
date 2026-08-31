using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using System.Numerics;
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
    private const float OnSurfaceToleranceMm = 0.01f;

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
            var steps = new List<float>(loop.Count);
            for (int i = 0; i < loop.Count; i++)
                steps.Add(Vector3.Distance(loop[i], loop[(i + 1) % loop.Count]));
            steps.Sort();

            float median = steps[steps.Count / 2];
            steps[^1].Should().BeLessThan(median * 3f, "no step should be a chord across the body");
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

    private static float MedianTurnDegrees(IReadOnlyList<Vector3> loop)
    {
        var turns = new List<float>(loop.Count);
        for (int i = 0; i < loop.Count; i++)
        {
            var incoming = loop[i] - loop[(i - 1 + loop.Count) % loop.Count];
            var outgoing = loop[(i + 1) % loop.Count] - loop[i];
            if (incoming.Length() < 1e-6f || outgoing.Length() < 1e-6f) continue;

            float cos = Math.Clamp(
                Vector3.Dot(Vector3.Normalize(incoming), Vector3.Normalize(outgoing)), -1f, 1f);
            turns.Add(MathF.Acos(cos) * 180f / MathF.PI);
        }

        turns.Sort();
        return turns[turns.Count / 2];
    }

    /// <summary>Unsigned distance from a point to the nearest triangle. Brute force; fine at this size.</summary>
    private static float DistanceToMesh(Vector3 point, IMesh mesh)
    {
        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;

        float best = float.MaxValue;
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            best = MathF.Min(best, SquaredDistanceToTriangle(
                point, vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]]));
        }
        return MathF.Sqrt(best);
    }

    /// <summary>Squared distance from a point to a triangle (Ericson, Real-Time Collision Detection).</summary>
    private static float SquaredDistanceToTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;

        float d1 = Vector3.Dot(ab, p - a), d2 = Vector3.Dot(ac, p - a);
        if (d1 <= 0f && d2 <= 0f) return (p - a).LengthSquared();

        float d3 = Vector3.Dot(ab, p - b), d4 = Vector3.Dot(ac, p - b);
        if (d3 >= 0f && d4 <= d3) return (p - b).LengthSquared();

        float vc = (d1 * d4) - (d3 * d2);
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            return (p - (a + (ab * (d1 / (d1 - d3))))).LengthSquared();

        float d5 = Vector3.Dot(ab, p - c), d6 = Vector3.Dot(ac, p - c);
        if (d6 >= 0f && d5 <= d6) return (p - c).LengthSquared();

        float vb = (d5 * d2) - (d1 * d6);
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            return (p - (a + (ac * (d2 / (d2 - d6))))).LengthSquared();

        float va = (d3 * d6) - (d5 * d4);
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            return (p - (b + ((c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)))))).LengthSquared();

        float denominator = 1f / (va + vb + vc);
        return (p - (a + (ab * (vb * denominator)) + (ac * (vc * denominator)))).LengthSquared();
    }
}
