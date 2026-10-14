using System.Collections.Immutable;
using Fabolus.Core.Features.AirChannels;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Tests.Features;

[Collection("GeometryEngine collection")]
public class DetectAirPocketsTests
{
    private static readonly AirPocketSettings Defaults = new(MinimumDepth: 1.0, MinimumSpacing: 6.0);

    private readonly GeometryEngineFixture _fixture;
    private readonly DetectAirPockets _feature;

    public DetectAirPocketsTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _feature = new DetectAirPockets(fixture.Engine);
    }

    [Fact]
    public void Sphere_HasOnePocket_AtItsTop()
    {
        var sphere = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10, 32).Value;

        var pockets = _feature.Execute(sphere, Defaults, []).Value;

        pockets.Should().ContainSingle();
        pockets[0].Point.Z.Should().BeApproximately(10, 0.1);
        pockets[0].Normal.Z.Should().BeGreaterThan(0.95);
        pockets[0].Depth.Should().Be(double.PositiveInfinity);
    }

    [Fact]
    public void TwoTowers_EachTrapsAPocket_WithTheLowerOneSpillingAtTheBridge()
    {
        var mesh = TwoTowers(bridgeTop: 5);

        var pockets = _feature.Execute(mesh, Defaults, []).Value;

        pockets.Should().HaveCount(2);

        // Highest first, each centred on its flat top rather than on a corner of it.
        AssertNear(pockets[0].Point, new Vector3(5, 5, 20));
        AssertNear(pockets[1].Point, new Vector3(25, 5, 15));
        pockets[1].Depth.Should().BeApproximately(10, 0.01); // 15 down to the bridge at 5
    }

    [Fact]
    public void PocketShallowerThanTheMinimumDepth_IsLeftAlone()
    {
        // The lower tower spills over a bridge only 1 mm beneath its top.
        var mesh = TwoTowers(bridgeTop: 14);

        var pockets = _feature.Execute(mesh, Defaults with { MinimumDepth = 2.0 }, []).Value;

        pockets.Should().ContainSingle();
        AssertNear(pockets[0].Point, new Vector3(5, 5, 20));
    }

    [Fact]
    public void PocketAlreadyVented_IsSkipped()
    {
        var mesh = TwoTowers(bridgeTop: 5);

        var pockets = _feature.Execute(mesh, Defaults, [new Vector3(27, 7, 15)]).Value;

        pockets.Should().ContainSingle();
        AssertNear(pockets[0].Point, new Vector3(5, 5, 20));
    }

    [Fact]
    public void ChannelLowDownInAPocket_DoesNotVentIt()
    {
        // Once the silicone rises past this channel's tip, the air above it in the tower is sealed in.
        var mesh = TwoTowers(bridgeTop: 5);

        var pockets = _feature.Execute(mesh, Defaults, [new Vector3(30, 5, 8)]).Value;

        pockets.Should().HaveCount(2);
    }

    [Fact]
    public void PocketsCloserThanTheSpacing_KeepOnlyTheHigher()
    {
        var mesh = TwoTowers(bridgeTop: 5);

        var pockets = _feature.Execute(mesh, Defaults with { MinimumSpacing = 25 }, []).Value;

        pockets.Should().ContainSingle();
        AssertNear(pockets[0].Point, new Vector3(5, 5, 20));
    }

    [Fact]
    public void UnweldedTriangleSoup_IsTreatedAsOneSurface()
    {
        // An STL repeats every corner once per triangle; unwelded, each triangle would be its own peak.
        var sphere = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10, 24).Value;
        var soup = _fixture.Engine.CreateMesh(
            [.. sphere.Triangles.Select(i => sphere.Vertices[i])],
            [.. Enumerable.Range(0, sphere.Triangles.Length)],
            sphere.Metadata).Value;

        var pockets = _feature.Execute(soup, Defaults, []).Value;

        pockets.Should().ContainSingle();
        pockets[0].Point.Z.Should().BeApproximately(10, 0.1);
    }

    [Fact]
    public void Map_AnswersAgainAsTheChannelsChange()
    {
        var map = _feature.Analyze(TwoTowers(bridgeTop: 5)).Value;

        map.Unvented(Defaults, []).Value.Should().HaveCount(2);
        map.Unvented(Defaults, [new Vector3(5, 5, 20)]).Value.Should().ContainSingle();
        map.Unvented(Defaults, [new Vector3(5, 5, 20), new Vector3(25, 5, 15)]).Value.Should().BeEmpty();
    }

    // A tall tower and a shorter one, joined low down by a bridge: the shorter tower's top is a
    // pocket that spills over the bridge into the taller one.
    private IMesh TwoTowers(double bridgeTop)
    {
        var engine = _fixture.Engine;
        var tall = engine.Generators.GenerateBox(new Vector3(0, 0, 0), new Vector3(10, 10, 20)).Value;
        var shorter = engine.Generators.GenerateBox(new Vector3(20, 0, 0), new Vector3(30, 10, 15)).Value;
        var bridge = engine.Generators.GenerateBox(new Vector3(8, 2, 0), new Vector3(22, 8, bridgeTop)).Value;

        var joined = engine.Booleans.Union(tall, bridge).Value;
        return engine.Booleans.Union(joined, shorter).Value;
    }

    private static void AssertNear(Vector3 actual, Vector3 expected, double tolerance = 0.5) =>
        actual.DistanceTo(expected).Should().BeLessThan(tolerance, $"expected near {expected} but was {actual}");
}
