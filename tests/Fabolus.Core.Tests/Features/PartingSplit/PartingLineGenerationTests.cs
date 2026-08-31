using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using FluentAssertions;
using System.Numerics;
using Xunit;
using Fabolus.Tests.Fixtures;

namespace Fabolus.Core.Tests.Features.PartingSplit;

[Collection("GeometryEngine collection")]
public class PartingLineGenerationTests
{
    private readonly IGeometryEngine _engine;
    private readonly PartingMeshFeature _sut;

    public PartingLineGenerationTests(GeometryEngineFixture fixture)
    {
        _engine = fixture.Engine;
        _sut = new PartingMeshFeature(_engine);
    }

    [Fact]
    public void Execute_Sphere_ReturnsOneLoop()
    {
        var sphereResult = _engine.Generators.GenerateSphere(Vector3.Zero, 10.0, 32);
        sphereResult.IsSuccess.Should().BeTrue();

        var result = _sut.GeneratePartingLineFromBody(
            Body(sphereResult.Value), new PartingLineParameters { Source = PartingLineSource.Silhouette, PullDirection = Vector3.UnitY });

        result.IsSuccess.Should().BeTrue();
        result.Value.Loops.Should().HaveCount(1);
        result.Value.InternalHoleCount.Should().Be(0);

        var loop = result.Value.Loops[0];
        var averageY = loop.Average(p => p.Y);
        Math.Abs(averageY).Should().BeLessThan(0.5f);
    }

    [Fact]
    public void Execute_TorusThroughHole_ReturnsOuterAndInnerLoop()
    {
        var torusResult = TorusMesh.Create(_engine, majorRadius: 10, minorRadius: 4, majorSegments: 64, minorSegments: 32);
        torusResult.IsSuccess.Should().BeTrue();

        // Pull along X, through the hole in the middle of the torus.
        var result = _sut.GeneratePartingLineFromBody(
            Body(torusResult.Value), new PartingLineParameters { Source = PartingLineSource.Silhouette, PullDirection = Vector3.UnitX });

        result.IsSuccess.Should().BeTrue();
        result.Value.Loops.Should().HaveCount(2, "the torus has an outer perimeter and an internal hole along the pull direction");
        result.Value.InternalHoleCount.Should().Be(1);
    }

    [Fact]
    public void Execute_ZeroDirection_Fails()
    {
        var sphereResult = _engine.Generators.GenerateSphere(Vector3.Zero, 10.0, 16);

        var result = _sut.GeneratePartingLineFromBody(
            Body(sphereResult.Value), new PartingLineParameters { Source = PartingLineSource.Silhouette, PullDirection = Vector3.Zero });

        result.IsFailure.Should().BeTrue();
    }

    /// <summary>These trace on a bare generated solid, so the body has no mould behind it.</summary>
    private static BodyMesh Body(IMesh mesh)
    {
        var body = BodyMesh.Create(mesh);
        body.IsSuccess.Should().BeTrue();
        return body.Value;
    }
}
