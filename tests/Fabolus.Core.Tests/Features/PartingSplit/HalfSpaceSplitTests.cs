using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using System.Numerics;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// Dividing the mould by taking each half straight from a boolean, rather than cutting it and then
/// working out which piece is which.
///
/// <para>
/// The older way had two ways to fail and both were routine: a cutter that did not sever left the
/// mould in one piece, and the side test was a majority vote that could put both pieces on the same
/// side. Here the operation that produced a half is what makes it that half, so neither exists. What
/// has to be pinned down instead is that the two halves really are complementary and really are
/// apart - see MeshLib discussion 4933 for why the gap comes from shifting the tool rather than
/// thickening it.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
public class HalfSpaceSplitTests
{
    private readonly IGeometryEngine _engine;
    private readonly GeometryEngineFixture _fixture;

    public HalfSpaceSplitTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _engine = fixture.Engine;
    }

    private static PartingLineParameters BorderLine => new()
    {
        Source = PartingLineSource.ExtrusionBorder,
        PullDirection = Vector3.UnitY,
    };

    // 2mm, matching what the view opens on. The grid resolving the rounding is sized absolutely
    // rather than off this, so the depth is free to be whatever suits the mould.
    private (MouldMesh Mould, PartingMeshParameters Parameters) Setup(string file, float depth = 2.0f)
    {
        var mesh = _fixture.LoadStl(file);
        var workspace = Workspace.CreateEmpty().AddMesh(mesh).Value;
        var bodyId = workspace.GetActiveMesh().Value.Metadata.Id;

        var mould = new GenerateMould(_engine).Execute(
            workspace, bodyId, new ConvexMouldDefinition(3.0, 3.0, 3.0) { TargetMeshId = bodyId });
        mould.IsSuccess.Should().BeTrue(mould.IsFailure ? mould.Error.Description : "");

        var validated = MouldMesh.Create(mould.Value.GetMesh(bodyId).Value);
        validated.IsSuccess.Should().BeTrue();

        var feature = new PartingMeshFeature(_engine);
        var line = feature.GeneratePartingLineFromBody(
            feature.GetBodyMesh(validated.Value).Value, BorderLine);
        line.IsSuccess.Should().BeTrue(line.IsFailure ? line.Error.Description : "");

        var parameters = PartingMeshFeature.ResolveAxis(line.Value, PartingMeshParameters.Default with
        {
            AxisSource = PartingMeshAxisSource.PartingLine,
            SplitMethod = PartingSplitMethod.ShiftedHalfSpaces,
            Depth = depth,
        });
        parameters.IsSuccess.Should().BeTrue();

        return (validated.Value, parameters.Value);
    }

    [Fact]
    public void PartingMeshParameters_DefaultToTheOlderSplit()
    {
        // The zero value, so a recipe committed before this existed replays the way it was cut. The
        // view opens on the newer one instead - see PartingSplitViewModelTests.
        PartingMeshParameters.Default.SplitMethod.Should().Be(PartingSplitMethod.SeveredComponents);
        new PartingMeshParameters().SplitMethod.Should().Be(PartingSplitMethod.SeveredComponents);
    }

    /// <summary>
    /// scalp is the case that motivated this. At the default cutter depth the older path reports
    /// "did not separate the mould into two halves"; there is no severing step here to fail.
    /// </summary>
    [Theory]
    [InlineData("chin_bolus.stl")]
    [InlineData("scalp_bolus.stl")]
    [InlineData("nose_bolus.stl")]
    public void BothHalvesComeBackAsRealSolids(string file)
    {
        var (mould, parameters) = Setup(file);

        var split = new PartingMeshFeature(_engine).SplitMould(mould, BorderLine, parameters);
        split.IsSuccess.Should().BeTrue(split.IsFailure ? split.Error.Description : "");

        foreach (var half in new[] { split.Value.Positive, split.Value.Negative })
        {
            half.IsEmpty.Should().BeFalse();

            var topology = _engine.Evaluators.ValidateTopology(half);
            topology.IsSuccess.Should().BeTrue();
            topology.Value.IsWatertight.Should().BeTrue("each half has to be a printable closed solid");
        }
    }

    /// <summary>
    /// The halves must be complementary, not overlapping copies. Taking one from an intersection and
    /// the other from a difference makes that true by construction - but only if the tool really does
    /// cover one whole side, so this is what would catch a tool that fell short.
    /// </summary>
    [Theory]
    [InlineData("chin_bolus.stl")]
    [InlineData("nose_bolus.stl")]
    public void TheHalvesTogetherNearlyFillTheMouldAndNoMore(string file)
    {
        var (mould, parameters) = Setup(file);

        var split = new PartingMeshFeature(_engine)
            .SplitMouldByHalfSpaces(mould, FlangeFor(mould, parameters), parameters);
        split.IsSuccess.Should().BeTrue(split.IsFailure ? split.Error.Description : "");

        double mouldVolume = Volume(mould.Mesh);
        double halves = Volume(split.Value.Positive) + Volume(split.Value.Negative);

        // The halves are the mould less the gap the shift opened, so they land just under it - and
        // nowhere near double it, which is what two overlapping copies would give.
        halves.Should().BeLessThan(mouldVolume, "overlapping halves would exceed it");
        halves.Should().BeGreaterThan(mouldVolume * 0.90,
            "only the gap should be missing - and the gap is millimetres now, not tenths, so it " +
            "accounts for a few percent of the mould rather than a rounding error");
    }

    /// <summary>
    /// A wider gap has to remove more. This is what shows the gap comes from the shift at all: were
    /// the shift being dropped, depth would change nothing.
    /// </summary>
    [Fact]
    public void AWiderGapRemovesMoreOfTheMould()
    {
        double VolumeAtDepth(float depth)
        {
            var (mould, parameters) = Setup("chin_bolus.stl", depth);
            var split = new PartingMeshFeature(_engine)
                .SplitMouldByHalfSpaces(mould, FlangeFor(mould, parameters), parameters);
            split.IsSuccess.Should().BeTrue(split.IsFailure ? split.Error.Description : "");
            return Volume(split.Value.Positive) + Volume(split.Value.Negative);
        }

        VolumeAtDepth(4.0f).Should().BeLessThan(VolumeAtDepth(1.0f));
    }

    private IMesh FlangeFor(MouldMesh mould, PartingMeshParameters parameters)
    {
        var feature = new PartingMeshFeature(_engine);

        var body = feature.GetBodyMesh(mould);
        body.IsSuccess.Should().BeTrue();

        var line = feature.GeneratePartingLineFromBody(body.Value, BorderLine);
        line.IsSuccess.Should().BeTrue();

        var contour = feature.GenerateOuterContour(mould, parameters);
        contour.IsSuccess.Should().BeTrue();

        var flange = feature.GenerateFlangeSurface(line.Value, contour.Value, parameters, body.Value);
        flange.IsSuccess.Should().BeTrue(flange.IsFailure ? flange.Error.Description : "");
        return flange.Value;
    }

    /// <summary>Signed volume by the divergence theorem - no engine call needed.</summary>
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
