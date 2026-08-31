using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using System.Numerics;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// The normals the parting view draws along the line as pink arrows. They are read by eye, so what
/// matters is that they point out of the body and that they turn smoothly from one point of the line
/// to the next - a fan of normals jumping face to face would say nothing about the shape.
/// </summary>
[Collection("GeometryEngine collection")]
public class SurfaceNormalSamplingTests
{
    private readonly IGeometryEngine _engine;
    private readonly GeometryEngineFixture _fixture;

    public SurfaceNormalSamplingTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _engine = fixture.Engine;
    }

    [Fact]
    public void OnASphere_TheNormalPointsStraightOutward()
    {
        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, 20.0, 48);
        sphere.IsSuccess.Should().BeTrue();

        // Points on the surface, so the outward normal is the direction from the centre - known
        // exactly, independent of how the sphere happens to be tessellated.
        var points = new List<Vector3>();
        for (int i = 0; i < 24; i++)
        {
            float a = MathF.Tau * i / 24f;
            points.Add(new Vector3(20f * MathF.Cos(a), 0f, 20f * MathF.Sin(a)));
        }

        var normals = _engine.PartingTools.SampleSurfaceNormals(sphere.Value, points);
        normals.IsSuccess.Should().BeTrue();

        for (int i = 0; i < points.Count; i++)
        {
            float off = MathF.Acos(Math.Clamp(
                Vector3.Dot(normals.Value[i], Vector3.Normalize(points[i])), -1f, 1f)) * 180f / MathF.PI;
            off.Should().BeLessThan(8f, "the averaged normal on a sphere is the radius direction");
        }
    }

    /// <summary>
    /// The property that makes them worth drawing: consecutive points of a real parting line get
    /// normals that turn gradually. Taking the normal of whichever single face a point lands on does
    /// not give this - neighbouring points straddle triangle edges and jump.
    /// </summary>
    [Theory]
    [InlineData("chin_bolus.stl")]
    [InlineData("scalp_bolus.stl")]
    public void AlongAPartingLine_TheNormalsTurnSmoothly(string file)
    {
        var mesh = _fixture.LoadStl(file);
        var body = BodyMesh.Create(mesh);
        body.IsSuccess.Should().BeTrue();

        var feature = new PartingMeshFeature(_engine);
        var line = feature.GeneratePartingLineFromThickness(body.Value);
        line.IsSuccess.Should().BeTrue(line.IsFailure ? line.Error.Description : "");

        var loop = line.Value.Loops[0];
        var normals = feature.SampleSurfaceNormals(body.Value, loop);
        normals.IsSuccess.Should().BeTrue();

        var turns = new List<float>();
        for (int i = 0; i < loop.Count; i++)
        {
            var a = normals.Value[i];
            var b = normals.Value[(i + 1) % loop.Count];
            if (a.LengthSquared() < 1e-12f || b.LengthSquared() < 1e-12f) continue;

            turns.Add(MathF.Acos(Math.Clamp(Vector3.Dot(a, b), -1f, 1f)) * 180f / MathF.PI);
        }

        turns.Should().NotBeEmpty();
        turns.Max().Should().BeLessThan(
            45f, "a normal that jumps from one point of the line to the next is reading a face, not the shape");
    }

    [Fact]
    public void NullOrEmptyInputs_Fail()
    {
        _engine.PartingTools.SampleSurfaceNormals(null!, new[] { Vector3.Zero }).IsFailure.Should().BeTrue();
        new PartingMeshFeature(_engine).SampleSurfaceNormals(null!, new[] { Vector3.Zero })
            .IsFailure.Should().BeTrue();
    }
}
