using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using System.Numerics;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// Making the cutter solid by offsetting the flange rather than extruding it.
///
/// <para>
/// The reason to do it is that extruding preserves whatever crossings the flange surface has and
/// doubles them, and a self-intersecting cutter is one the mould boolean will not cut with. An offset
/// is read off a distance field sampled on a voxel grid, and a field has no memory of the surface
/// having passed through itself - so the result is clean whatever went in. What it costs is
/// resolution: the grid has to resolve the thickness, which is why the cutter is now millimetres
/// thick rather than tenths.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
public class OffsetThickenedCutterTests
{
    private readonly IGeometryEngine _engine;
    private readonly GeometryEngineFixture _fixture;

    public OffsetThickenedCutterTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _engine = fixture.Engine;
    }

    private static PartingLineParameters BorderLine => new()
    {
        Source = PartingLineSource.ExtrusionBorder,
        PullDirection = Vector3.UnitY,
    };

    private (MouldMesh Mould, IMesh Flange, PartingMeshParameters Parameters) Setup(
        string file, PartingMeshThickening thickening, float depth = 2.0f)
    {
        var mesh = _fixture.LoadStl(file);
        var workspace = Workspace.CreateEmpty().AddMesh(mesh).Value;
        var bodyId = workspace.GetActiveMesh().Value.Metadata.Id;

        var mould = new GenerateMould(_engine).Execute(
            workspace, bodyId, new ConvexMouldDefinition(3.0, 3.0, 3.0) { TargetMeshId = bodyId });
        mould.IsSuccess.Should().BeTrue(mould.IsFailure ? mould.Error.Description : "");

        var validated = MouldMesh.Create(mould.Value.GetMesh(bodyId).Value).Value;
        var feature = new PartingMeshFeature(_engine);
        var body = feature.GetBodyMesh(validated).Value;

        var line = feature.GeneratePartingLineFromBody(body, BorderLine);
        line.IsSuccess.Should().BeTrue(line.IsFailure ? line.Error.Description : "");

        var parameters = PartingMeshFeature.ResolveAxis(line.Value, PartingMeshParameters.Default with
        {
            AxisSource = PartingMeshAxisSource.PartingLine,
            Thickening = thickening,
            Depth = depth,
        }).Value;

        var contour = feature.GenerateOuterContour(validated, parameters);
        contour.IsSuccess.Should().BeTrue();

        var flange = feature.GenerateFlangeSurface(line.Value, contour.Value, parameters, body);
        flange.IsSuccess.Should().BeTrue(flange.IsFailure ? flange.Error.Description : "");

        return (validated, flange.Value, parameters);
    }

    [Fact]
    public void PartingMeshParameters_DefaultToExtruding()
    {
        // The zero value, so an older recipe rebuilds the cutter it was committed with. The view
        // opens on offsetting instead - see PartingSplitViewModelTests.
        PartingMeshParameters.Default.Thickening.Should().Be(PartingMeshThickening.Extrude);
    }

    /// <summary>
    /// The property the whole approach rests on. A cutter with crossings, or one that is not closed,
    /// is one the boolean refuses - so this is the precondition for cutting anything at all.
    /// </summary>
    [Theory]
    [InlineData("chin_bolus.stl")]
    [InlineData("scalp_bolus.stl")]
    public void TheOffsetCutterIsCleanAndClosed(string file)
    {
        var (_, flange, parameters) = Setup(file, PartingMeshThickening.Offset);

        var cutter = new PartingMeshFeature(_engine).ExtrudeFlange(flange, parameters);
        cutter.IsSuccess.Should().BeTrue(cutter.IsFailure ? cutter.Error.Description : "");

        var topology = _engine.Evaluators.ValidateTopology(cutter.Value);
        topology.IsSuccess.Should().BeTrue();
        topology.Value.SelfIntersectionCount.Should().Be(0, "a distance field cannot reproduce a crossing");
        topology.Value.IsWatertight.Should().BeTrue("the boolean needs a closed cutter");
    }

    /// <summary>
    /// And it has to actually halve the mould. Two pieces is not enough on its own - a sliver and the
    /// rest is also two pieces - so the sizes are what is checked.
    /// </summary>
    [Theory]
    [InlineData("chin_bolus.stl")]
    [InlineData("scalp_bolus.stl")]
    public void TheOffsetCutterHalvesTheMould(string file)
    {
        var (mould, flange, parameters) = Setup(file, PartingMeshThickening.Offset);

        var cutter = new PartingMeshFeature(_engine).ExtrudeFlange(flange, parameters);
        cutter.IsSuccess.Should().BeTrue();

        var cut = _engine.Booleans.Subtract(mould.Mesh, cutter.Value);
        cut.IsSuccess.Should().BeTrue(cut.IsFailure ? cut.Error.Description : "");

        var pieces = _engine.Evaluators.SeparateComponents(cut.Value);
        pieces.IsSuccess.Should().BeTrue();

        double mouldVolume = Volume(mould.Mesh);
        var shares = pieces.Value.Select(p => Volume(p) / mouldVolume).OrderByDescending(v => v).ToList();

        shares.Should().HaveCountGreaterThanOrEqualTo(2, "the cutter has to sever the mould");
        shares[1].Should().BeGreaterThan(
            0.2, "both halves have to be real - a sliver and the rest is not a split");
    }

    private static double Volume(IMesh mesh)
    {
        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;

        double total = 0;
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            total += Vector3.Dot(
                vertices[triangles[i]],
                Vector3.Cross(vertices[triangles[i + 1]], vertices[triangles[i + 2]])) / 6.0;
        }
        return Math.Abs(total);
    }
}
