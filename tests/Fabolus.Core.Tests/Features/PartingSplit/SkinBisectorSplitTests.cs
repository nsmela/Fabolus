using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Diagnostics;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// Pins <see cref="PartingMeshSweep.SkinBisector"/>: the parting surface as the points equally far from
/// the two skins the line divides the body into.
///
/// <para>
/// It exists for larynx_large, which no other sweep parts. Its outer line climbs 85mm and crosses
/// itself 19 times seen along its own axis, so every sweep built outward against one axis folds through
/// the body: the mould loft leaves one piece of ~100% plus specks, as do the other three, along both
/// axis sources.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
public class SkinBisectorSplitTests
{
    private const double BodyRadius = 20.0;

    private readonly IGeometryEngine _engine;
    private readonly GeometryEngineFixture _fixture;

    public SkinBisectorSplitTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _engine = fixture.Engine;
    }

    private static PartingMeshParameters Recipe => PartingMeshParameters.Default with
    {
        AxisSource = PartingMeshAxisSource.PartingLine,
        Sweep = PartingMeshSweep.SkinBisector,
        SplitMethod = PartingSplitMethod.SeveredComponents,
        Thickening = PartingMeshThickening.Extrude,
        Depth = 0.1,
    };

    [Fact]
    public void LarynxLarge_PartsIntoTwoSubstantialPieces()
    {
        var feature = new PartingMeshFeature(_engine);
        var mould = SavedMoulds.Mould(SavedMoulds.Open(_engine, _fixture.GetAssetPath("3mf/larynx_large.3mf")).Value).Value;
        var body = feature.GetBodyMesh(mould).Value;
        var line = feature.GeneratePartingLineFromThickness(body);
        line.IsSuccess.Should().BeTrue(line.IsFailure ? line.Error.Description : "");
        line.Value.Loops.Should().HaveCount(2, "the body has a hole through it: the outer rim and the stoma");

        var resolved = PartingMeshFeature.ResolveAxis(line.Value, Recipe).Value;
        var contour = feature.GenerateOuterContour(mould, resolved).Value;
        var surface = feature.GenerateFlangeSurface(line.Value, contour, resolved, body);
        surface.IsSuccess.Should().BeTrue(surface.IsFailure ? surface.Error.Description : "");
        _engine.Evaluators.CountSelfIntersections(surface.Value).Value.Should().Be(0,
            "the surface is the zero set of a field, which has one value at each point and cannot fold");

        var cutter = feature.ExtrudeFlange(surface.Value, resolved);
        cutter.IsSuccess.Should().BeTrue(cutter.IsFailure ? cutter.Error.Description : "");
        var cut = _engine.Booleans.Subtract(mould.Mesh, cutter.Value);
        cut.IsSuccess.Should().BeTrue(cut.IsFailure ? cut.Error.Description : "");

        // Measured at 57/43. Substantial means a fifth of the mould or more, so specks don't count and
        // a split that took only a corner off would fail.
        double whole = Volume(mould.Mesh);
        var shares = _engine.Evaluators.SeparateComponents(cut.Value).Value
            .Select(piece => Volume(piece) / whole)
            .OrderByDescending(share => share)
            .ToList();

        shares.Count(share => share >= 0.2).Should().Be(2, $"the pieces are {string.Join(", ", shares.Take(4).Select(s => s.ToString("P1")))}");
        (shares[0] + shares[1]).Should().BeGreaterThan(0.98, "the two halves are the mould, less only the cut");
    }

    [Fact]
    public void Sphere_PartedAtItsEquator_IsPartedOnTheEquatorialPlane()
    {
        // Both skins are hemispheres, so every point equally far from the two lies on the plane between
        // them - inside the body and out past it.
        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, BodyRadius, 48).Value;
        var surface = SkinBisector.Build(_engine, Equator(BodyRadius), sphere, mould: null, innerBleedMm: BodyRadius);

        surface.IsSuccess.Should().BeTrue(surface.IsFailure ? surface.Error.Description : "");
        var worst = surface.Value.Vertices.OrderByDescending(v => Math.Abs(v.Y)).Take(5).Select(v => $"({v.X:F2},{v.Y:F2},{v.Z:F2})");
        surface.Value.Vertices.Max(v => Math.Abs(v.Y)).Should().BeLessThan(0.5, string.Join(" ", worst));
        surface.Value.Vertices.Max(v => Math.Sqrt((v.X * v.X) + (v.Z * v.Z))).Should().BeGreaterThan(BodyRadius + 3.0,
            "the surface carries on past the body to the edge of the box it is meshed in");
    }

    [Fact]
    public void BuildingTwice_GivesTheSameSurface()
    {
        // The engine's level set used to come back a little different every call - Manifold's parallel
        // meshing numbered its output by thread scheduling - so a replayed split would not cut where the
        // committed one did. GeometryEngine 0df541d ships Manifold's own fix for that.
        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, BodyRadius, 48).Value;
        var first = SkinBisector.Build(_engine, Equator(BodyRadius), sphere, mould: null).Value;
        var second = SkinBisector.Build(_engine, Equator(BodyRadius), sphere, mould: null).Value;

        second.Triangles.Should().Equal(first.Triangles);
        second.Vertices.Should().Equal(first.Vertices);
    }

    [Fact]
    public void LineThatOnlyNicksTheBody_IsRefused()
    {
        // A small circle near the pole cuts off a cap of a fraction of a percent: there is no second skin
        // for the surface to lie between, only a sliver.
        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, BodyRadius, 48).Value;
        double polar = 5.0 * Math.PI / 180.0;
        var cap = new PartingLine([Enumerable.Range(0, 60).Select(i =>
        {
            double a = 2 * Math.PI * i / 60;
            double r = BodyRadius * Math.Sin(polar);
            return new Vector3(r * Math.Cos(a), BodyRadius * Math.Cos(polar), r * Math.Sin(a));
        })]);

        var surface = SkinBisector.Build(_engine, cap, sphere, mould: null);

        surface.IsFailure.Should().BeTrue();
        surface.Error.Should().Be(SkinBisector.LineDoesNotDivideBody);
    }

    private static PartingLine Equator(double radius) =>
        new([Enumerable.Range(0, 120).Select(i =>
        {
            double a = 2 * Math.PI * i / 120;
            return new Vector3(radius * Math.Cos(a), 0.0, radius * Math.Sin(a));
        })]);

    private static double Volume(IMesh mesh)
    {
        var vs = mesh.Vertices;
        var ts = mesh.Triangles;
        double total = 0;
        for (int i = 0; i + 2 < ts.Length; i += 3) total += vs[ts[i]].Dot(vs[ts[i + 1]].Cross(vs[ts[i + 2]])) / 6.0;
        return Math.Abs(total);
    }
}
