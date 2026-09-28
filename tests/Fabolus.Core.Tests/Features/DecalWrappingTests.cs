using System.Collections.Immutable;
using BasicResults;
using Fabolus.Core.Features.Decal;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using GeometryEngine.Core.Geometry;
using Xunit;

using SnVector3 = System.Numerics.Vector3;

namespace Fabolus.Tests.Features;

/// <summary>
/// A decal prism draped onto a real clinical surface.
/// </summary>
/// <remarks>
/// GeometryEngine tests its own prism builder thoroughly, but against generated primitives -
/// spheres, cylinders, a frame's own plane. What it cannot cover from inside the library is the
/// kind of surface Fabolus actually puts text on: a scanned bolus, irregular and, in the case of
/// this fixture, not even closed. A prism has to come back a sound solid regardless, because it
/// is about to be an operand in a boolean.
/// </remarks>
[Collection("GeometryEngine collection")]
public class DecalWrappingTests
{
    private readonly GeometryEngineFixture _fixture;

    public DecalWrappingTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
    }

    private static SurfaceFrame FrameAt(Vector3 origin)
    {
        // Built through DecalFrame so the basis is the one the decal feature actually uses.
        var frame = DecalFrame.FromHit(
            new SnVector3((float)origin.X, (float)origin.Y, (float)origin.Z),
            SnVector3.UnitZ,
            rotationDeg: 0f);

        return new SurfaceFrame(
            new Vector3(frame.Origin.X, frame.Origin.Y, frame.Origin.Z),
            new Vector3(frame.U.X, frame.U.Y, frame.U.Z),
            new Vector3(frame.V.X, frame.V.Y, frame.V.Z),
            new Vector3(frame.N.X, frame.N.Y, frame.N.Z));
    }

    private static ImmutableArray<Polygon2D> Bar(double halfWidth, double halfHeight) =>
    [
        Polygon2D.FromOuter(
        [
            new(-halfWidth, -halfHeight),
            new(halfWidth, -halfHeight),
            new(halfWidth, halfHeight),
            new(-halfWidth, halfHeight),
        ]),
    ];

    [Fact]
    public void APrismDrapedOnAScannedBolus_IsASoundSolid()
    {
        // ear_bolus.stl is a real scan and is not watertight. The prism still has to be, because
        // the next thing that happens to it is a boolean against the model.
        var bolus = _fixture.LoadStl("ear_bolus.stl");
        var stats = _fixture.Engine.Evaluators.GetStatistics(bolus).Value;

        var spec = new DecalPrismSpec(
            Bar(12, 2.5),
            FrameAt(new Vector3(
                (stats.BoundsMin.X + stats.BoundsMax.X) * 0.5,
                (stats.BoundsMin.Y + stats.BoundsMax.Y) * 0.5,
                stats.BoundsMax.Z)),
            Depth: 0.8,
            Sink: -0.2,
            Overshoot: 0.2,
            MaxEdgeLength: 1.5,
            Surface: Maybe<IMesh>.Some(bolus));

        var prism = _fixture.Engine.Decals.BuildPrism(spec);

        prism.IsSuccess.Should().BeTrue(prism.IsFailure ? prism.Error.Description : string.Empty);

        var topology = _fixture.Engine.Evaluators.ValidateTopology(prism.Value).Value;
        topology.IsWatertight.Should().BeTrue("a prism about to be a boolean operand must enclose a volume");
        topology.IsManifold.Should().BeTrue();
    }

    [Fact]
    public void APrismDrapedOnASmoothedBolus_IsASoundSolid()
    {
        // The other shape a decal lands on in practice: a bolus that has been through smoothing,
        // so it is dense and closed where the raw scan was neither.
        var bolus = _fixture.LoadStl("ear_bolus_smoothed.stl");
        var stats = _fixture.Engine.Evaluators.GetStatistics(bolus).Value;

        var spec = new DecalPrismSpec(
            Bar(8, 2),
            FrameAt(new Vector3(
                (stats.BoundsMin.X + stats.BoundsMax.X) * 0.5,
                (stats.BoundsMin.Y + stats.BoundsMax.Y) * 0.5,
                stats.BoundsMax.Z)),
            Depth: 0.8,
            Sink: -0.2,
            Overshoot: 0.2,
            MaxEdgeLength: 1.5,
            Surface: Maybe<IMesh>.Some(bolus));

        var prism = _fixture.Engine.Decals.BuildPrism(spec);

        prism.IsSuccess.Should().BeTrue(prism.IsFailure ? prism.Error.Description : string.Empty);

        var topology = _fixture.Engine.Evaluators.ValidateTopology(prism.Value).Value;
        topology.IsWatertight.Should().BeTrue();
        topology.IsManifold.Should().BeTrue();
    }

    [Fact]
    public void APrismDrapedOnASurface_FollowsItRatherThanFloatingAboveIt()
    {
        // The point of draping: the underside tracks the surface instead of sitting on the
        // frame's flat plane, or the text would lift off a curved bolus at its edges.
        var bolus = _fixture.LoadStl("ear_bolus_smoothed.stl");
        var stats = _fixture.Engine.Evaluators.GetStatistics(bolus).Value;

        var frame = FrameAt(new Vector3(
            (stats.BoundsMin.X + stats.BoundsMax.X) * 0.5,
            (stats.BoundsMin.Y + stats.BoundsMax.Y) * 0.5,
            stats.BoundsMax.Z));

        DecalPrismSpec Spec(Maybe<IMesh> surface) => new(
            Bar(8, 2), frame, Depth: 0.8, Sink: -0.2, Overshoot: 0.2, MaxEdgeLength: 1.5, Surface: surface);

        var draped = _fixture.Engine.Decals.BuildPrism(Spec(Maybe<IMesh>.Some(bolus)));
        var flat = _fixture.Engine.Decals.BuildPrism(Spec(Maybe<IMesh>.None()));

        draped.IsSuccess.Should().BeTrue();
        flat.IsSuccess.Should().BeTrue();

        // A prism that ignored the surface would be the flat one, so the two must differ.
        var drapedStats = _fixture.Engine.Evaluators.GetStatistics(draped.Value).Value;
        var flatStats = _fixture.Engine.Evaluators.GetStatistics(flat.Value).Value;

        drapedStats.BoundsMin.Z.Should().NotBe(flatStats.BoundsMin.Z);
    }
}
