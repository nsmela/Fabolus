using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Features.Transforms;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// Issue #88: <c>flat_vulva</c> tilted 30 to 60 degrees about X was cut into one whole mould and a
/// handful of specks, which a separated export then wrote out as nine files.
///
/// <para>
/// The cut was not at fault, nor the axis: the extrusion-border line was. On the body's wide flap two
/// of the crease offset's walks turn back and finish beside the crease they set off from, and at those
/// tilts the second of them pulled the line back to the crease and out again. Seen along the axis the
/// line then doubled back on itself, the mould-loft flange folded where it did, and the folded cutter
/// left the mould joined. See <see cref="CreaseOffsetOptions.LeastHeadway"/>, and the
/// <c>TiltedPartingCut</c> diagnostic for the measurements.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
public class TiltedBodySplitTests
{
    private readonly GeometryEngineFixture _fixture;
    private readonly IGeometryEngine _engine;

    public TiltedBodySplitTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _engine = fixture.Engine;
    }

    [Theory]
    [InlineData(30.0)]
    [InlineData(45.0)]
    [InlineData(60.0)]
    public void FlatVulva_TiltedAboutX_CutsIntoTwoHalves(double tiltDeg)
    {
        var workspace = new ImportMesh(_engine).Execute(
            Workspace.CreateEmpty(), _fixture.GetAssetPath("flat_vulva.stl")).Value;
        var id = workspace.GetActiveRecord().Value.Id;

        var rotated = new TransformMesh(_engine).Rotate(
            workspace, id, (float)(tiltDeg * Math.PI / 180.0), Vector3.UnitX);
        rotated.IsSuccess.Should().BeTrue(rotated.IsFailure ? rotated.Error.Description : "");

        var moulded = new GenerateMould(_engine).Execute(rotated.Value, id,
            MouldDefinition.OfShape(MouldShapeType.Concave, 2.5, 5.0) with { TargetMeshId = id });
        moulded.IsSuccess.Should().BeTrue(moulded.IsFailure ? moulded.Error.Description : "");

        double mouldVolume = Volume(moulded.Value.GetMesh(id).Value);

        // The recipe PartingSplitViewModel fixes for new work.
        var cut = new SplitMouldFeature(_engine).ExecuteCut(
            moulded.Value, id,
            new PartingLineParameters
            {
                Source = PartingLineSource.ExtrusionBorder,
                PullDirection = Vector3.UnitY,
                NeutralBand = PartingNeutralBand.FromDegrees(-5.0, 5.0),
            },
            PartingMeshParameters.Default with
            {
                AxisSource = PartingMeshAxisSource.PartingLine,
                Sweep = PartingMeshSweep.MouldLoft,
                SplitMethod = PartingSplitMethod.SeveredComponents,
                Thickening = PartingMeshThickening.Extrude,
                Depth = 0.1,
                FlangeMaxSlopeDeg = 80.0,
                NormalFollowMm = 100.0,
                RawFlange = false,
                NormalSmoothingPasses = 5,
                Axis = Vector3.UnitY,
            },
            PartingResultMode.Separated);
        cut.IsSuccess.Should().BeTrue(cut.IsFailure ? cut.Error.Description : "");

        var piece = cut.Value.Records.Single(r => r.Id != id);
        var pieces = _engine.Evaluators.SeparateComponents(cut.Value.GetMesh(piece.Id).Value).Value
            .Select(Volume)
            .OrderByDescending(v => v)
            .ToList();

        pieces.Should().HaveCount(2, "a severing cut leaves the mould in two pieces and nothing else");
        pieces[1].Should().BeGreaterThan(mouldVolume * 0.3,
            "both pieces are halves of the mould, not one mould and a speck");
    }

    private static double Volume(IMesh mesh)
    {
        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;

        double total = 0;
        for (int i = 0; i + 2 < triangles.Length; i += 3)
            total += vertices[triangles[i]].Dot(vertices[triangles[i + 1]].Cross(vertices[triangles[i + 2]])) / 6.0;

        return Math.Abs(total);
    }
}
