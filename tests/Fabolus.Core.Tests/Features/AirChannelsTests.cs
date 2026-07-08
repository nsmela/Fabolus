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

    [Fact]
    public void PaintedAirChannel_Generate_FullMode_SinglePoint_ProducesRoundChannel()
    {
        // A click without a drag leaves a single-point path; Full mode must still
        // produce a small round vertical channel rather than fail.
        var channel = new PaintedAirChannel(new[] { new Vector3(5, 5, 5) }, 2.0f, 10.0f, 1.0f);

        var result = channel.Generate(_fixture.Engine, AirChannelRenderMode.Full);

        result.IsSuccess.Should().BeTrue();
        var stats = _fixture.Engine.Evaluators.GetStatistics(result.Value).Value;

        stats.MinZ.Should().BeApproximately(4.0, 0.1); // path Z (5) - PenetrationDepth (1)
        stats.MaxZ.Should().BeApproximately(15.0, 0.1); // path Z (5) + TotalLength (10)
        (stats.MaxX - stats.MinX).Should().BeApproximately(4.0, 0.3); // ~2 x Radius disc
        (stats.MaxY - stats.MinY).Should().BeApproximately(4.0, 0.3);
    }

    [Fact]
    public void PaintedAirChannel_Generate_FullMode_WithTargetMesh_SnapsBottomToSurface()
    {
        var sphere = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10, 32).Value;

        // A stroke across the top of the sphere; the buffered contour extends past the
        // painted points, so without the raycast the bottom would sit at the interpolated
        // path Z instead of following the surface curving away below it.
        var path = new[] {
            new Vector3(-3, 0, 9.5f),
            new Vector3(0, 0, 10f),
            new Vector3(3, 0, 9.5f)
        };

        var channel = new PaintedAirChannel(path, 2.0f, 10.0f, 1.0f);

        var result = channel.Generate(_fixture.Engine, AirChannelRenderMode.Full, sphere);

        result.IsSuccess.Should().BeTrue();
        var stats = _fixture.Engine.Evaluators.GetStatistics(result.Value).Value;

        // The contour reaches x = +/-5, where the sphere surface is at z = sqrt(100-25) ~ 8.66;
        // snapped bottom = surface - penetration (1) ~ 7.66, well below the painted path's
        // lowest Z (9.5) that pure interpolation would give.
        stats.MinZ.Should().BeLessThan(9.0);
        stats.MinZ.Should().BeGreaterThan(6.5);
        stats.MaxZ.Should().BeApproximately(19.5, 0.1); // path[0].Z (9.5) + TotalLength (10)
    }

    [Fact]
    public void ResampleOpenPath_SmoothsJitter_PreservesEndpoints()
    {
        // A zig-zag stroke: y alternates +/-0.5 along x.
        var path = new List<Vector3>();
        for (var i = 0; i <= 20; i++)
            path.Add(new Vector3(i, i % 2 == 0 ? 0.5f : -0.5f, 3.0f));

        var result = _fixture.Engine.Generators.ResampleOpenPath(path, targetSpacing: 2.0f);

        result.IsSuccess.Should().BeTrue();
        var resampled = result.Value;

        resampled.Count.Should().BeGreaterThanOrEqualTo(2);
        resampled[0].Should().Be(path[0]);
        resampled[^1].Should().Be(path[^1]);

        // Smoothing must have reduced the zig-zag amplitude on interior points.
        var maxInteriorY = resampled.Skip(1).Take(resampled.Count - 2).Max(p => Math.Abs(p.Y));
        maxInteriorY.Should().BeLessThan(0.4f);
    }

    [Fact]
    public void ResampleOpenPath_ShortPath_ReturnsUnchanged()
    {
        var path = new[] { new Vector3(0, 0, 0), new Vector3(5, 0, 0) };

        var result = _fixture.Engine.Generators.ResampleOpenPath(path, targetSpacing: 2.0f);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(path);
    }
}
