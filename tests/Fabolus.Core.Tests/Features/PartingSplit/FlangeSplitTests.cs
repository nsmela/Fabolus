using System.Numerics;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// Covers the split the Parting Split view actually performs: it cuts with the flange the user has
/// been looking at (and set the depth of). The view can't be driven headlessly, so these walk the
/// same calls in the same order.
///
/// The fixture is a real mould rather than a bare solid, because the flange only spans outward from
/// the parting line - it relies on the mould's cavity to reach the middle. A solid body leaves an
/// uncut column down its centre and stays in one piece.
/// </summary>
[Collection("GeometryEngine collection")]
public class FlangeSplitTests
{
    private readonly IGeometryEngine _engine;

    public FlangeSplitTests(GeometryEngineFixture fixture) => _engine = fixture.Engine;

    /// <summary>A convex mould around a sphere, plus the body it was built from.</summary>
    private (IMesh Body, MouldMesh Mould) BuildMould()
    {
        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, 10.0, 32);
        sphere.IsSuccess.Should().BeTrue();

        var workspace = Workspace.CreateEmpty();
        var added = workspace.AddMesh(sphere.Value);
        added.IsSuccess.Should().BeTrue();
        workspace = added.Value;

        var bodyId = workspace.GetActiveMesh().Value.Metadata.Id;
        var definition = new ConvexMouldDefinition(OffsetXY: 3.0, OffsetBottom: 3.0, OffsetTop: 3.0)
        {
            TargetMeshId = bodyId
        };

        var mould = new GenerateMould(_engine).Execute(workspace, bodyId, definition);
        mould.IsSuccess.Should().BeTrue(mould.IsFailure ? mould.Error.Description : "");

        var mouldMesh = MouldMesh.Create(mould.Value.GetMesh(bodyId).Value);
        mouldMesh.IsSuccess.Should().BeTrue(mouldMesh.IsFailure ? mouldMesh.Error.Description : "");

        return (sphere.Value, mouldMesh.Value);
    }

    /// <summary>Mirrors PartingSplitViewModel: parting line -> outer contour -> flange -> extrude.</summary>
    private (PartingLine Line, IMesh Surface, IMesh Tool) BuildPartingTool(IMesh body, MouldMesh mould, float depth)
    {
        var feature = new PartingMeshFeature(_engine);

        // The line comes from the body, the contour from the mould that encloses it - same as the view.
        var bodyMesh = BodyMesh.Create(body);
        bodyMesh.IsSuccess.Should().BeTrue();

        var lineResult = feature.GeneratePartingLineFromBody(
            bodyMesh.Value, new PartingLineParameters { Source = PartingLineSource.Silhouette, PullDirection = Vector3.UnitY });
        lineResult.IsSuccess.Should().BeTrue(lineResult.IsFailure ? lineResult.Error.Description : "");

        var parameters = PartingMeshParameters.Default with { Depth = depth };

        var contour = feature.GenerateOuterContour(mould, parameters);
        contour.IsSuccess.Should().BeTrue(contour.IsFailure ? contour.Error.Description : "");

        // The body is what the flange's inner rim has to seal against.
        var surface = feature.GenerateFlangeSurface(
            lineResult.Value, contour.Value, parameters, bodyMesh.Value);
        surface.IsSuccess.Should().BeTrue(surface.IsFailure ? surface.Error.Description : "");

        var solid = feature.ExtrudeFlange(surface.Value, parameters);
        solid.IsSuccess.Should().BeTrue(solid.IsFailure ? solid.Error.Description : "");

        return (lineResult.Value, surface.Value, solid.Value);
    }

    [Fact]
    public void FlangeSplit_Mould_ProducesBothHalves()
    {
        var (body, mould) = BuildMould();
        var (line, _, tool) = BuildPartingTool(body, mould, depth: 0.1f);

        var result = new PartingMeshFeature(_engine)
            .SplitMould(mould, tool, line, Vector3.UnitY);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : "");

        var (positive, negative) = result.Value;
        positive.Should().NotBeNull();
        negative.Should().NotBeNull("the split must return the negative half, not just the positive one");

        // Positive is the larger piece, not the one on a particular side of the axis. It used to mean
        // the latter, decided by sampling each piece's vertices against the nearest parting point -
        // a majority vote that could put both pieces on the same side and then report the split as
        // having produced a single piece. That is what it did on scalp, whose subtraction gives two
        // clean halves at 56% and 43%. Ranking by size instead is unambiguous, and stable across runs
        // in a way the component walk's own ordering is not.
        var positiveVolume = Volume(positive);
        var negativeVolume = Volume(negative);
        positiveVolume.Should().BeGreaterThan(negativeVolume, "positive is the larger of the two pieces");

        foreach (var (name, half) in new[] { ("positive", positive), ("negative", negative) })
        {
            var topo = _engine.Evaluators.ValidateTopology(half);
            topo.IsSuccess.Should().BeTrue(topo.IsFailure ? topo.Error.Description : "");
            topo.Value.IsWatertight.Should().BeTrue($"the {name} half should be a printable, closed solid");
        }
    }

    [Fact]
    public void FlangeSplit_WithZeroThicknessSurface_ReportsSinglePiece()
    {
        var (body, mould) = BuildMould();
        var (line, surface, _) = BuildPartingTool(body, mould, depth: 0.1f);

        // Cutting with the un-extruded surface removes no volume, so the mould stays whole.
        //
        // It no longer reports that. The check that did also fired whenever the side test failed to
        // tell the two halves apart, which is a different thing entirely and far more common - on
        // scalp it rejected a subtraction that had cleanly produced 56% and 43% pieces. Removing it
        // is what made scalp split; the cost is that a genuinely unsevered mould now comes back as
        // one piece returned for both halves rather than as a message saying so.
        var result = new PartingMeshFeature(_engine)
            .SplitMould(mould, surface, line, Vector3.UnitY);

        result.IsSuccess.Should().BeTrue("an unsevered mould is no longer reported as a failure");
        result.Value.Positive.Should().BeSameAs(result.Value.Negative,
            "with nothing severed there is one piece, and it is returned as both halves");
    }

    /// <summary>Enclosed volume, for ranking the two halves.</summary>
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
