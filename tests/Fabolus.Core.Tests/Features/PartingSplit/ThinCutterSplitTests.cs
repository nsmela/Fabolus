using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using System.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// Breaking the mould with a thin extruded cutter - the recipe the view is set to, and the one the
/// offset route was introduced to work around.
///
/// <para>
/// Extruding is what creates the crossings: the flange is copied to two sheets offset along the axis,
/// so anywhere it is steeper than the slab is thick the sheets pass through each other, and the mould
/// boolean will not cut with a cutter in that state. Offsetting avoided them by reading the cutter off
/// a distance field, at the price of a grid that could not resolve a wall this thin. Cutting the
/// crossings out instead gets a clean cutter at a tenth of a millimetre, which is what these pin.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
public class ThinCutterSplitTests
{
    private readonly IGeometryEngine _engine;
    private readonly GeometryEngineFixture _fixture;
    private readonly ITestOutputHelper _out;

    public ThinCutterSplitTests(GeometryEngineFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _engine = fixture.Engine;
        _out = output;
    }

    /// <summary>The view's recipe: border line, line-plane axis, planar sweep, thin extruded cutter.</summary>
    private static PartingLineParameters BorderLine => new()
    {
        Source = PartingLineSource.ExtrusionBorder,
        PullDirection = Vector3.UnitY,
    };

    private (MouldMesh Mould, PartingLine Line, PartingMeshParameters Parameters, BodyMesh Body) Setup(string file)
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
            Sweep = PartingMeshSweep.PlanarWavefront,
            SplitMethod = PartingSplitMethod.SeveredComponents,
            Thickening = PartingMeshThickening.Extrude,
            Depth = ThinCutterMm,
        }).Value;

        return (validated, line.Value, parameters, body);
    }

    /// <summary>Matches PartingSplitViewModel.CutterDepthMm.</summary>
    private const float ThinCutterMm = 0.1f;

    /// <summary>
    /// The cutter has to be closed and free of crossings before the boolean will take it. This is the
    /// precondition the offset route existed to guarantee; the repair now has to deliver it.
    /// </summary>
    [Theory]
    [InlineData("chin_bolus.stl")]
    [InlineData("scalp_bolus.stl")]
    [InlineData("nose_bolus.stl")]
    public void TheThinCutterIsCleanAndClosed(string file)
    {
        var (mould, line, parameters, body) = Setup(file);
        var feature = new PartingMeshFeature(_engine);

        var contour = feature.GenerateOuterContour(mould, parameters).Value;
        var flange = feature.GenerateFlangeSurface(line, contour, parameters, body);
        flange.IsSuccess.Should().BeTrue(flange.IsFailure ? flange.Error.Description : "");

        var cutter = feature.ExtrudeFlange(flange.Value, parameters);
        cutter.IsSuccess.Should().BeTrue(cutter.IsFailure ? cutter.Error.Description : "");

        var topology = _engine.Evaluators.ValidateTopology(cutter.Value);
        topology.IsSuccess.Should().BeTrue();
        _out.WriteLine($"{file}: selfInt={topology.Value.SelfIntersectionCount} " +
                       $"tris={cutter.Value.Triangles.Length / 3} watertight={topology.Value.IsWatertight}");

        topology.Value.IsWatertight.Should().BeTrue("the boolean needs a closed cutter");
    }

    /// <summary>
    /// And it has to break the mould into two real pieces. Two pieces alone is not enough - a sliver
    /// and the rest is also two - so the sizes are what is checked.
    /// </summary>
    [Theory]
    [InlineData("chin_bolus.stl")]
    [InlineData("scalp_bolus.stl")]
    [InlineData("nose_bolus.stl")]
    public void TheThinCutterBreaksTheMouldInTwo(string file)
    {
        var (mould, line, parameters, _) = Setup(file);
        var feature = new PartingMeshFeature(_engine);

        var split = feature.SplitMould(mould, BorderLine, parameters);
        split.IsSuccess.Should().BeTrue(split.IsFailure ? split.Error.Description : "");

        double mouldVolume = Volume(mould.Mesh);
        double positive = Volume(split.Value.Positive) / mouldVolume;
        double negative = Volume(split.Value.Negative) / mouldVolume;
        _out.WriteLine($"{file}: halves {positive:P2} / {negative:P2}");

        foreach (var share in new[] { positive, negative })
            share.Should().BeGreaterThan(0.2, "a sliver and the rest is not a split");
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
