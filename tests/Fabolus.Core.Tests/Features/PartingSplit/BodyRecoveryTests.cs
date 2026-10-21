using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Diagnostics;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// The body a mould is parted on is its history replayed to the Transform stage, and that history
/// smooths. The engine's smoothing is not repeatable - its level-set offset meshes in parallel and
/// comes back a little different every call, sometimes with a different vertex count - so each
/// recovery used to hand back a different body. On larynx_small the rim analysis then found one wall
/// rim, then none, then one; on larynx_large the cut went from 11 pieces to 50.
/// </summary>
[Collection("GeometryEngine collection")]
public class BodyRecoveryTests
{
    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;

    public BodyRecoveryTests(GeometryEngineFixture assets)
    {
        _assets = assets;
        _engine = assets.Engine;
    }

    [Fact]
    public void RecoveringTheBodyTwice_GivesTheSameBodyAndTheSameRims()
    {
        var feature = new PartingMeshFeature(_engine);
        var opened = SavedMoulds.Open(_engine, _assets.GetAssetPath("3mf/larynx_small.3mf"));
        opened.IsSuccess.Should().BeTrue();

        // Two moulds over the one entry, as two visits to the parting split would make.
        var first = feature.GetBodyMesh(SavedMoulds.Mould(opened.Value).Value).Value;
        var second = feature.GetBodyMesh(SavedMoulds.Mould(opened.Value).Value).Value;

        second.Mesh.Should().BeSameAs(first.Mesh);
        Rims(second.Mesh).Should().Equal(Rims(first.Mesh));
    }

    /// <summary>
    /// larynx_small is a ring: an outer rim and a rim round the hole. Below the hole it narrows to
    /// little more than its wall, and the fill used to take a stretch of each skin there as band, which
    /// joined the two walls into one rim whose contours each ran half round the outside and half round
    /// the hole. Each skin should be an annulus, so there are two wall rims and two parting loops.
    /// </summary>
    [Fact]
    public void Ring_NarrowBelowTheHole_HasTwoWallRimsAndTwoPartingLoops()
    {
        var feature = new PartingMeshFeature(_engine);
        var opened = SavedMoulds.Open(_engine, _assets.GetAssetPath("3mf/larynx_small.3mf"));
        opened.IsSuccess.Should().BeTrue();
        var body = feature.GetBodyMesh(SavedMoulds.Mould(opened.Value).Value).Value;

        var closed = RidgeDetection.FindRidgeContours(body.Mesh, RidgeDetectionOptions.Default)
            .Where(c => c.IsClosed).ToList();
        var rims = PartingStrategy.Rims(closed, WallThickness.Measure(_engine, body.Mesh).Value.Median);

        rims.Should().HaveCount(2).And.OnlyContain(r => r.Kind == PartingRimKind.Wall,
            "the outer rim and the hole's rim are separate walls, each bounded by two contours");

        var line = feature.GeneratePartingLineFromThickness(body);
        line.IsSuccess.Should().BeTrue();
        line.Value.Loops.Should().HaveCount(2, "one parting loop per rim");
    }

    private IReadOnlyList<string> Rims(IMesh body)
    {
        var thickness = WallThickness.Measure(_engine, body).Value;
        var closed = RidgeDetection.FindRidgeContours(body, RidgeDetectionOptions.Default).Where(c => c.IsClosed).ToList();
        return PartingStrategy.Rims(closed, thickness.Median)
            .Select(r => $"{r.Kind} {string.Join(",", r.ContourIndices.Select(i => closed[i].Points.Count))}")
            .ToList();
    }
}
