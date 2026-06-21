using System.Numerics;
using Fabolus.Core.Geometry;
using Fabolus.Core.Features.AirChannels;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Tests.Features;

[Collection("GeometryEngine collection")]
public class AirChannelsTests
{
    private readonly GeometryEngineFixture _fixture;

    public AirChannelsTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void StraightAirChannel_Generate_FullMode_ReturnsWatertightTube()
    {
        var channel = new StraightAirChannel(new Vector3(0, 0, 10), 5.0f, 20.0f, 2.0f, 5.0f);
        
        var result = channel.Generate(_fixture.Engine, AirChannelRenderMode.Full);

        result.IsSuccess.Should().BeTrue();
        var mesh = result.Value;

        var stats = _fixture.Engine.Evaluators.GetStatistics(mesh).Value;
        stats.MaxZ.Should().BeApproximately(30.0, 0.1); // 10 + 20
        stats.MinZ.Should().BeApproximately(9.0, 0.1); // 10 - 1.0 (cone penetration)
    }

    [Fact]
    public void AngledAirChannel_Generate_FullMode_ReturnsCurvedTube()
    {
        var channel = new AngledAirChannel(
            StartPoint: new Vector3(0, 0, 0),
            Normal: new Vector3(1, 0, 0), // Pointing in X
            TipLength: 5.0f,
            TotalLength: 20.0f,
            TipDiameter: 2.0f,
            Radius: 5.0f);

        var result = channel.Generate(_fixture.Engine, AirChannelRenderMode.Full);

        result.IsSuccess.Should().BeTrue();
        var mesh = result.Value;

        var stats = _fixture.Engine.Evaluators.GetStatistics(mesh).Value;
        // The normal is (1,0,0) and target Z is TotalLength (20)
        // Starts at X=-1, goes to X=5, then arcs to Z
        stats.MaxX.Should().BeGreaterThan(4.0);
        stats.MaxZ.Should().BeGreaterThan(19.0);
    }

    [Fact]
    public void PaintedAirChannel_Generate_FullMode_ExtrudesSolid()
    {
        var path = new[] {
            new Vector3(0, 0, 5),
            new Vector3(10, 0, 5),
            new Vector3(20, 0, 5)
        };

        var channel = new PaintedAirChannel(path, 2.0f, 10.0f, -2.0f);

        var result = channel.Generate(_fixture.Engine, AirChannelRenderMode.Full);

        result.IsSuccess.Should().BeTrue();
        var mesh = result.Value;

        var stats = _fixture.Engine.Evaluators.GetStatistics(mesh).Value;
        stats.MinZ.Should().BeApproximately(7.0, 0.1); // path Z (5) - PenetrationDepth (-2) -> goes upward
        stats.MaxZ.Should().BeApproximately(15.0, 0.1); // path[0].Z + TotalLength (5 + 10)
    }
}
