using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using System.Diagnostics;
using System.Numerics;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// The choice between the two parting meshes: one built around the direction the user picked, one
/// built around the parting line's own plane. It is the only difference between them, so it is what
/// these cover - along with the two runaways that used to stop either from being built at all.
/// </summary>
[Collection("GeometryEngine collection")]
public class PartingMeshAxisTests
{
    private readonly IGeometryEngine _engine;
    private readonly GeometryEngineFixture _fixture;

    public PartingMeshAxisTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _engine = fixture.Engine;
    }

    /// <summary>
    /// A ring in the XZ plane, so its own plane normal is +Y by construction and every claim about
    /// the derived axis is checkable against the shape rather than against the code that found it.
    /// </summary>
    private static PartingLine RingInXZ(float radius = 20f, int segments = 64)
    {
        var points = new Vector3[segments];
        for (int i = 0; i < segments; i++)
        {
            float t = MathF.Tau * i / segments;
            points[i] = new Vector3(radius * MathF.Cos(t), 0f, radius * MathF.Sin(t));
        }
        return new PartingLine(new[] { points });
    }

    [Fact]
    public void PartingMeshParameters_DefaultToThePullDirection()
    {
        // The zero value, so a recipe that predates the choice - an older save file - rebuilds the
        // way it was committed rather than on an axis nothing asked for.
        PartingMeshParameters.Default.AxisSource.Should().Be(PartingMeshAxisSource.PullDirection);
        new PartingMeshParameters().AxisSource.Should().Be(PartingMeshAxisSource.PullDirection);
    }

    [Fact]
    public void ResolveAxis_OnThePullDirection_NormalizesWhatTheCallerGave()
    {
        var resolved = PartingMeshFeature.ResolveAxis(
            RingInXZ(), PartingMeshParameters.Default with { Axis = new Vector3(0f, 0f, 7f) });

        resolved.IsSuccess.Should().BeTrue();
        resolved.Value.Axis.Should().Be(Vector3.UnitZ, "the caller's direction is the axis, whatever the line does");
    }

    [Fact]
    public void ResolveAxis_OnThePullDirection_RefusesAZeroDirection()
    {
        var resolved = PartingMeshFeature.ResolveAxis(
            RingInXZ(), PartingMeshParameters.Default with { Axis = Vector3.Zero });

        resolved.IsFailure.Should().BeTrue();
    }

    /// <summary>
    /// The property the whole line-aligned mesh rests on: the axis comes from the line, so pointing
    /// the gizmo somewhere else cannot move it. Without this the flange is swept in a plane the line
    /// was never traced in.
    /// </summary>
    [Theory]
    [InlineData(0f, 0f, 1f)]
    [InlineData(1f, 0f, 0f)]
    [InlineData(0.3f, 0.6f, -0.7f)]
    public void ResolveAxis_OnThePartingLine_IgnoresThePullDirection(float x, float y, float z)
    {
        var resolved = PartingMeshFeature.ResolveAxis(
            RingInXZ(),
            PartingMeshParameters.Default with
            {
                AxisSource = PartingMeshAxisSource.PartingLine,
                Axis = new Vector3(x, y, z),
            });

        resolved.IsSuccess.Should().BeTrue();

        // The ring lies in XZ, so its plane normal is the Y axis. Only the sign is allowed to follow
        // the caller, and that decides nothing but which half is named Positive.
        MathF.Abs(resolved.Value.Axis.Y).Should().BeApproximately(1f, 1e-3f);
    }

    [Fact]
    public void ResolveAxis_OnThePartingLine_TakesItsSignFromThePullDirection()
    {
        var line = RingInXZ();

        var along = PartingMeshFeature.ResolveAxis(line, PartingMeshParameters.Default with {
            AxisSource = PartingMeshAxisSource.PartingLine, Axis = Vector3.UnitY });
        var against = PartingMeshFeature.ResolveAxis(line, PartingMeshParameters.Default with {
            AxisSource = PartingMeshAxisSource.PartingLine, Axis = -Vector3.UnitY });

        along.IsSuccess.Should().BeTrue();
        against.IsSuccess.Should().BeTrue();
        Vector3.Dot(along.Value.Axis, Vector3.UnitY).Should().BePositive();
        Vector3.Dot(against.Value.Axis, Vector3.UnitY).Should().BeNegative();
    }

    /// <summary>
    /// Resolving is idempotent, which is what lets a caller thread the resolved set through several
    /// stages without tracking whether each one has already had it done. A second pass sees a plain
    /// axis and hands it back unchanged rather than re-deriving against whatever line it was given.
    /// </summary>
    [Fact]
    public void ResolveAxis_IsIdempotent()
    {
        var once = PartingMeshFeature.ResolveAxis(
            RingInXZ(), PartingMeshParameters.Default with {
                AxisSource = PartingMeshAxisSource.PartingLine, Axis = Vector3.UnitZ });
        once.IsSuccess.Should().BeTrue();

        // A different line the second time round: if it re-derived, the axis would move.
        var twice = PartingMeshFeature.ResolveAxis(RingInXZ(radius: 5f), once.Value);

        twice.IsSuccess.Should().BeTrue();
        twice.Value.Axis.Should().Be(once.Value.Axis);
        twice.Value.AxisSource.Should().Be(PartingMeshAxisSource.PullDirection);
    }

    [Fact]
    public void ResolveAxis_OnThePartingLine_RefusesALineWithNoPlane()
    {
        // Doubled back on itself: it encloses no area seen from anywhere, so it implies no plane and
        // there is no axis to be had from it. Saying so beats returning an arbitrary one.
        var degenerate = new PartingLine(new[]
        {
            new[] { Vector3.Zero, Vector3.UnitX, new Vector3(2f, 0f, 0f), Vector3.UnitX },
        });

        PartingMeshFeature.ResolveAxis(
            degenerate,
            PartingMeshParameters.Default with { AxisSource = PartingMeshAxisSource.PartingLine })
            .IsFailure.Should().BeTrue();
    }

    /// <summary>
    /// The regression that motivated all of this: the parting mesh never appeared. The wavefront
    /// offset compounded Clipper's arc points ring over ring, and the remesh that then ran over the
    /// result subdivided without limit, so on a real body the build ran indefinitely with nothing to
    /// cancel it. Both parting lines and both axis sources went the same way. The rings are bounded
    /// now and the remesh is gone entirely - see
    /// <see cref="PartingSolid_OnARealBody_DoesNotSelfIntersect"/> for why it had to go.
    ///
    /// <para>
    /// A time budget is a blunt assertion, but the failure it guards is not marginal: this stage
    /// completes in well under a second per body now, against not completing at all before. Anything
    /// approaching the budget means one of the two bounds has been lost.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("chin_bolus.stl", PartingLineSource.ExtrusionBorder, PartingMeshAxisSource.PartingLine)]
    [InlineData("chin_bolus.stl", PartingLineSource.ExtrusionBorder, PartingMeshAxisSource.PullDirection)]
    [InlineData("chin_bolus.stl", PartingLineSource.Silhouette, PartingMeshAxisSource.PullDirection)]
    [InlineData("scalp_bolus.stl", PartingLineSource.ExtrusionBorder, PartingMeshAxisSource.PartingLine)]
    [InlineData("larynx_bolus.stl", PartingLineSource.ExtrusionBorder, PartingMeshAxisSource.PartingLine)]
    [InlineData("nose_bolus.stl", PartingLineSource.ExtrusionBorder, PartingMeshAxisSource.PartingLine)]
    public void FlangeSurface_OnARealBody_Terminates(
        string file, PartingLineSource lineSource, PartingMeshAxisSource axisSource)
    {
        var mesh = _fixture.LoadStl(file);
        var body = BodyMesh.Create(mesh);
        body.IsSuccess.Should().BeTrue();

        var feature = new PartingMeshFeature(_engine);

        var line = feature.GeneratePartingLineFromBody(
            body.Value, new PartingLineParameters { Source = lineSource, PullDirection = Vector3.UnitY });
        line.IsSuccess.Should().BeTrue(line.IsFailure ? line.Error.Description : "");

        var resolved = PartingMeshFeature.ResolveAxis(
            line.Value, PartingMeshParameters.Default with { AxisSource = axisSource });
        resolved.IsSuccess.Should().BeTrue(resolved.IsFailure ? resolved.Error.Description : "");

        var contour = _engine.PartingTools.GenerateOuterBoxContour(
            mesh, resolved.Value.Axis, resolved.Value.OuterContourMargin);
        contour.IsSuccess.Should().BeTrue(contour.IsFailure ? contour.Error.Description : "");

        var elapsed = Stopwatch.StartNew();
        var flange = feature.GenerateFlangeSurface(line.Value, contour.Value, resolved.Value, body.Value);
        elapsed.Stop();

        flange.IsSuccess.Should().BeTrue(flange.IsFailure ? flange.Error.Description : "");
        flange.Value.Triangles.Length.Should().BeGreaterThan(0);
        elapsed.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(60), "the flange build has to be bounded, not merely finite");
    }

    /// <summary>
    /// The cutter has to be a clean solid, because the mould boolean refuses one that is not: a
    /// self-intersecting parting mesh comes back as "bad contour on N mesh A faces, probably mesh B
    /// has self-intersections", and the mould is left in one piece.
    ///
    /// <para>
    /// What produced them was the flange's own remesh. The wavefront triangulation used to be
    /// sliver-heavy, so a uniform remesh was run over it to regularise it; on every real body that
    /// remesh returned a mesh whose vertices largely coincided - 5,856 of them at 1,473 distinct
    /// positions on chin_bolus - and each coincident pair is a zero-area face. The rings are now
    /// resampled to the step that produced them, so the triangulation is well-shaped to begin with
    /// and there is nothing to repair.
    /// </para>
    ///
    /// <para>
    /// Asserted on the extruded solid rather than the surface, since that is what the boolean is
    /// handed. A zero-area face on the surface becomes one on each of the two sheets.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("chin_bolus.stl")]
    [InlineData("scalp_bolus.stl")]
    [InlineData("nose_bolus.stl")]
    public void PartingSolid_OnARealBody_DoesNotSelfIntersect(string file)
    {
        var mesh = _fixture.LoadStl(file);
        var body = BodyMesh.Create(mesh);
        body.IsSuccess.Should().BeTrue();

        var feature = new PartingMeshFeature(_engine);

        var line = feature.GeneratePartingLineFromBody(body.Value, new PartingLineParameters {
            Source = PartingLineSource.ExtrusionBorder, PullDirection = Vector3.UnitY });
        line.IsSuccess.Should().BeTrue(line.IsFailure ? line.Error.Description : "");

        var parameters = PartingMeshFeature.ResolveAxis(line.Value, PartingMeshParameters.Default with {
            AxisSource = PartingMeshAxisSource.PartingLine });
        parameters.IsSuccess.Should().BeTrue();

        var contour = _engine.PartingTools.GenerateOuterBoxContour(
            mesh, parameters.Value.Axis, parameters.Value.OuterContourMargin);
        contour.IsSuccess.Should().BeTrue();

        var surface = feature.GenerateFlangeSurface(line.Value, contour.Value, parameters.Value, body.Value);
        surface.IsSuccess.Should().BeTrue(surface.IsFailure ? surface.Error.Description : "");

        var solid = feature.ExtrudeFlange(surface.Value, parameters.Value);
        solid.IsSuccess.Should().BeTrue(solid.IsFailure ? solid.Error.Description : "");

        var topology = _engine.Evaluators.ValidateTopology(solid.Value);
        topology.IsSuccess.Should().BeTrue();
        topology.Value.SelfIntersectionCount.Should().Be(
            0, "a self-intersecting cutter is one the mould boolean will not cut with");
        topology.Value.HasDegenerateTriangles.Should().BeFalse();
        topology.Value.IsWatertight.Should().BeTrue("the cutter has to be a closed solid");
    }

    /// <summary>
    /// The flange's inner rim has to end up inside the body, all of it. A rim vertex left outside is
    /// a hairline bridge of mould material that survives the cut, and the parting-mesh preview draws
    /// each one as a red point - so this is both a split that fails and a warning the user is shown.
    ///
    /// <para>
    /// Two things had to change to hold this. The seal used to push every rim vertex not already
    /// <c>marginMm</c> deep, which on a rim sitting a median 0.5mm in is about half of them, and all
    /// that shoving left the flange steep enough to self-intersect once extruded; it now moves only
    /// what is genuinely outside. And it used to move each vertex alone, onto whichever body face was
    /// nearest it, so neighbours were sent different ways and the face between them creased; the push
    /// is now shared along the rim over several rounds, which bends the flange instead.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("chin_bolus.stl")]
    [InlineData("scalp_bolus.stl")]
    [InlineData("nose_bolus.stl")]
    public void FlangeRim_SealsCompletelyAgainstTheBody(string file)
    {
        var mesh = _fixture.LoadStl(file);
        var body = BodyMesh.Create(mesh);
        body.IsSuccess.Should().BeTrue();

        var feature = new PartingMeshFeature(_engine);

        var line = feature.GeneratePartingLineFromBody(body.Value, new PartingLineParameters {
            Source = PartingLineSource.ExtrusionBorder, PullDirection = Vector3.UnitY });
        line.IsSuccess.Should().BeTrue(line.IsFailure ? line.Error.Description : "");

        var parameters = PartingMeshFeature.ResolveAxis(line.Value, PartingMeshParameters.Default with {
            AxisSource = PartingMeshAxisSource.PartingLine });
        parameters.IsSuccess.Should().BeTrue();

        var contour = _engine.PartingTools.GenerateOuterBoxContour(
            mesh, parameters.Value.Axis, parameters.Value.OuterContourMargin);
        contour.IsSuccess.Should().BeTrue();

        var surface = feature.GenerateFlangeSurface(line.Value, contour.Value, parameters.Value, body.Value);
        surface.IsSuccess.Should().BeTrue(surface.IsFailure ? surface.Error.Description : "");

        var seal = feature.InspectFlangeSeal(surface.Value, body.Value, line.Value, parameters.Value);
        seal.IsSuccess.Should().BeTrue();
        seal.Value.Should().NotBeEmpty("the flange has an inner rim to seal");

        seal.Value.Where(point => !point.IsSealed).Should().BeEmpty(
            "every rim point outside the body is a bridge the cut leaves behind");
    }
}
