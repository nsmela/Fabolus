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
/// The two ways of resolving self-intersections, measured against each other on the surfaces that
/// actually have them.
///
/// <para>
/// Every call in the pipeline had been getting <see cref="SelfIntersectionRepair.Relax"/>, because
/// the settings were constructed bare and that is MeshLib's default. These pin what each one does so
/// the choice is made on numbers rather than on which is described more confidently.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
public class SelfIntersectionRepairTests
{
    private readonly IGeometryEngine _engine;
    private readonly GeometryEngineFixture _fixture;
    private readonly ITestOutputHelper _out;

    public SelfIntersectionRepairTests(GeometryEngineFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _engine = fixture.Engine;
        _out = output;
    }

    /// <summary>
    /// The surface sweep is the one that folds - the planar wavefront comes back with none - so it is
    /// what the repair has to be judged on.
    /// </summary>
    private IMesh SweptFlange(string file)
    {
        var mesh = _fixture.LoadStl(file);
        var workspace = Workspace.CreateEmpty().AddMesh(mesh).Value;
        var bodyId = workspace.GetActiveMesh().Value.Metadata.Id;

        var mould = new GenerateMould(_engine).Execute(
            workspace, bodyId, new ConvexMouldDefinition(3.0, 3.0, 3.0) { TargetMeshId = bodyId });
        var validated = MouldMesh.Create(mould.Value.GetMesh(bodyId).Value).Value;

        var feature = new PartingMeshFeature(_engine);
        var body = feature.GetBodyMesh(validated).Value;
        var line = feature.GeneratePartingLineFromBody(body, new PartingLineParameters
        {
            Source = PartingLineSource.ExtrusionBorder,
            PullDirection = Vector3.UnitY,
        }).Value;

        var parameters = PartingMeshFeature.ResolveAxis(line, PartingMeshParameters.Default with
        {
            AxisSource = PartingMeshAxisSource.PartingLine,
            Sweep = PartingMeshSweep.SurfaceSweep,
        }).Value;

        var contour = feature.GenerateOuterContour(validated, parameters).Value;
        var flange = feature.GenerateFlangeSurface(line, contour, parameters, body);
        flange.IsSuccess.Should().BeTrue(flange.IsFailure ? flange.Error.Description : "");
        return flange.Value;
    }

    [Theory]
    [InlineData("chin_bolus.stl")]
    [InlineData("scalp_bolus.stl")]
    [InlineData("larynx_bolus.stl")]
    public void BothMethodsMeasured(string file)
    {
        var flange = SweptFlange(file);

        var before = _engine.Evaluators.ValidateTopology(flange);
        before.IsSuccess.Should().BeTrue();
        _out.WriteLine($"{file}: before  selfInt={before.Value.SelfIntersectionCount} " +
                       $"tris={flange.Triangles.Length / 3} watertight={before.Value.IsWatertight}");

        foreach (var method in new[] { SelfIntersectionRepair.Relax, SelfIntersectionRepair.CutAndFill })
        {
            var repaired = _engine.Modifiers.RepairSelfIntersections(flange, method);
            if (repaired.IsFailure)
            {
                _out.WriteLine($"{file}: {method,-11} FAILED {repaired.Error.Description}");
                continue;
            }

            var after = _engine.Evaluators.ValidateTopology(repaired.Value);
            _out.WriteLine(
                $"{file}: {method,-11} selfInt={after.Value.SelfIntersectionCount} " +
                $"tris={repaired.Value.Triangles.Length / 3} watertight={after.Value.IsWatertight}");
        }
    }

    /// <summary>
    /// The case that now matters: the thin extruded cutter, which is what the mould is broken with.
    ///
    /// <para>
    /// Cutting is destructive where it acts, and this is a sheet a fraction of a millimetre thick, so
    /// the question is whether excising a crossing takes out both faces and leaves a hole. A cutter
    /// with a hole in it is not watertight, and the boolean will not sever a mould with it - so that
    /// is what is measured here rather than the self-intersection count alone.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("chin_bolus.stl")]
    [InlineData("larynx_bolus.stl")]
    public void OnTheThinCutterTheRepairMustNotPerforate(string file)
    {
        var flange = SweptFlange(file);

        var cutter = _engine.PartingTools.ExtrudeFlange(flange, Vector3.UnitY, ThinCutterMm);
        cutter.IsSuccess.Should().BeTrue(cutter.IsFailure ? cutter.Error.Description : "");

        var before = _engine.Evaluators.ValidateTopology(cutter.Value);
        _out.WriteLine($"{file}: cutter before  selfInt={before.Value.SelfIntersectionCount} " +
                       $"tris={cutter.Value.Triangles.Length / 3} watertight={before.Value.IsWatertight}");

        foreach (var method in new[] { SelfIntersectionRepair.Relax, SelfIntersectionRepair.CutAndFill })
        {
            var repaired = _engine.Modifiers.RepairSelfIntersections(cutter.Value, method);
            if (repaired.IsFailure)
            {
                _out.WriteLine($"{file}: cutter {method,-11} FAILED {repaired.Error.Description}");
                continue;
            }

            var after = _engine.Evaluators.ValidateTopology(repaired.Value);
            _out.WriteLine(
                $"{file}: cutter {method,-11} selfInt={after.Value.SelfIntersectionCount} " +
                $"tris={repaired.Value.Triangles.Length / 3} watertight={after.Value.IsWatertight}");
        }
    }

    /// <summary>The thin cutter's wall thickness, matching what the view is set to.</summary>
    private const float ThinCutterMm = 0.1f;

    /// <summary>
    /// Relax is the default, and it has to stay the default: it is what every existing caller has
    /// been getting, so a change here silently re-repairs every parting mesh in the pipeline.
    /// </summary>
    [Fact]
    public void RelaxIsTheDefault()
    {
        default(SelfIntersectionRepair).Should().Be(SelfIntersectionRepair.Relax);
    }
}
