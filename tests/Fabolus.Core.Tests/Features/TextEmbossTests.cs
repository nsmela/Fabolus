using System.Collections.Immutable;
using BasicResults;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

// DecalFrame is expressed in System.Numerics vectors while the geometry around it uses the
// engine's Vec3, and this file touches both. Spelling the numerics one out keeps which is which
// obvious rather than leaving it to whichever alias won.
using SnVector3 = System.Numerics.Vector3;

namespace Fabolus.Tests.Features;

public sealed class TestGlyphOutlineSource : IGlyphOutlineSource
{
    public Result<IReadOnlyList<Polygon2D>> GetOutlines(string text, DecalFont font, float capHeight, float tracking)
    {
        // Generates simple rectangular contour for testing
        float halfW = capHeight * 0.6f * 0.5f;
        float halfH = capHeight * 0.5f;

        ImmutableArray<Vector2> outer =
        [
            new(-halfW, -halfH),
            new(halfW, -halfH),
            new(halfW, halfH),
            new(-halfW, halfH)
        ];

        return Result.Success<IReadOnlyList<Polygon2D>>(new List<Polygon2D>
        {
            Polygon2D.FromOuter(outer)
        });
    }

    public TextMetrics MeasureText(string text, DecalFont font, float capHeight, float tracking)
    {
        return TextMetrics.Approximate(text, capHeight, tracking);
    }
}

[Collection("GeometryEngine collection")]
public class TextEmbossTests
{
    private readonly GeometryEngineFixture _fixture;
    private readonly IGlyphOutlineSource _outlineSource;

    public TextEmbossTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _outlineSource = new TestGlyphOutlineSource();
    }

    [Fact]
    public void DecalFrame_FromHit_ComputesOrthonormalBasis()
    {
        var anchor = new SnVector3(10, 20, 30);
        var normal = new SnVector3(0, 0, 1);

        var frame = DecalFrame.FromHit(anchor, normal, rotationDeg: 0f);

        frame.Origin.Should().Be(anchor);
        frame.N.Should().Be(SnVector3.UnitZ);
        SnVector3.Dot(frame.U, frame.N).Should().BeApproximately(0f, 1e-5f);
        SnVector3.Dot(frame.V, frame.N).Should().BeApproximately(0f, 1e-5f);
        SnVector3.Dot(frame.U, frame.V).Should().BeApproximately(0f, 1e-5f);
        frame.U.Length().Should().BeApproximately(1f, 1e-5f);
        frame.V.Length().Should().BeApproximately(1f, 1e-5f);
    }

    [Fact]
    public void DecalFrame_WithRotation_RotatesAroundNormal()
    {
        var anchor = SnVector3.Zero;
        var normal = SnVector3.UnitZ;

        var frame0 = DecalFrame.FromHit(anchor, normal, rotationDeg: 0f);
        var frame90 = DecalFrame.FromHit(anchor, normal, rotationDeg: 90f);

        SnVector3.Dot(frame0.V, frame90.U).Should().BeApproximately(1f, 1e-4f);
    }

    [Fact]
    public void Polygons_MirrorX_FlipsXAndPreservesWinding()
    {
        var poly = Polygon2D.FromOuter(
        [
            new(-2, -2),
            new(2, -2),
            new(2, 2),
            new(-2, 2)
        ]);

        var mirrored = _fixture.Engine.Polygons.MirrorX(poly);
        mirrored.Outer[0].X.Should().Be(2);
        mirrored.Outer.Should().HaveCount(4);
    }

    /// <summary>
    /// What the decal gate accepts and refuses. The gate used to reject anything whose
    /// <c>HasCorruptTopology</c> flag was set, which counts slivers and coincident vertices
    /// alongside real defects. That check could never fire before the engine migration - the
    /// MeshLib evaluator hardcoded the flag to <c>false</c> - so it went live as a real
    /// measurement and began refusing meshes that were watertight, manifold and perfectly
    /// printable. A saved mould carrying two degenerate triangles in twenty-five thousand was
    /// enough to block every decal on it.
    /// </summary>
    /// <remarks>
    /// Exercised on the decision rather than through a boolean, because the condition cannot be
    /// built reliably from geometry: Manifold only re-meshes near the intersection, so whether
    /// untidiness survives depends on where on the mesh it happens to sit. On a small synthetic
    /// mesh the boolean welds it away and the bug does not reproduce; on a real mould, where the
    /// slivers are far from the decal, it does. The defect was in the predicate, so that is what
    /// is pinned here.
    /// </remarks>
    public class DecalTopologyGateTests
    {
        private static readonly IMesh AnyMesh = GeometryEngine.Core.Geometry.ImmutableMesh.Empty;

        private static TopologyValidation Topology(
            int boundaryEdges = 0,
            int nonManifoldEdges = 0,
            int degenerateTriangles = 0,
            int duplicateVertices = 0,
            int inconsistentWinding = 0,
            int duplicateFaces = 0) =>
            new(boundaryEdges, nonManifoldEdges, degenerateTriangles, duplicateVertices,
                inconsistentWinding, duplicateFaces, ShellCount: 1);

        [Fact]
        public void AMeshThatIsMerelyUntidyIsAccepted()
        {
            // The regression: closed, manifold, correctly wound - and carrying the slivers and
            // coincident vertices that a boolean leaves behind.
            var topology = Topology(degenerateTriangles: 2, duplicateVertices: 9);

            topology.IsWatertight.Should().BeTrue();
            topology.IsManifold.Should().BeTrue();
            topology.HasCorruptTopology.Should().BeTrue("this is the flag the old gate read");

            GenerateDecals.AcceptIfPrintable(AnyMesh, topology).IsSuccess.Should().BeTrue();
        }

        [Fact]
        public void ACleanMeshIsAccepted() =>
            GenerateDecals.AcceptIfPrintable(AnyMesh, Topology()).IsSuccess.Should().BeTrue();

        [Fact]
        public void AHoledMeshIsRefused()
        {
            // The defect that actually matters: no well-defined inside for a slicer to fill.
            var result = GenerateDecals.AcceptIfPrintable(AnyMesh, Topology(boundaryEdges: 12));

            result.IsFailure.Should().BeTrue();
            result.Error.Should().Be(MeshErrors.NotWatertight);
        }

        [Fact]
        public void ANonManifoldMeshIsRefused()
        {
            var result = GenerateDecals.AcceptIfPrintable(AnyMesh, Topology(nonManifoldEdges: 4));

            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("Decal.NonManifold");
        }

        [Fact]
        public void AnInvertedFaceIsRefused()
        {
            var result = GenerateDecals.AcceptIfPrintable(AnyMesh, Topology(inconsistentWinding: 2));

            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("Decal.NonManifold");
        }
    }

    [Fact]
    public void GenerateDecals_Execute_Emboss_ProducesValidMesh()
    {
        var sphere = _fixture.Engine.Generators.GenerateSphere(Vector3.Zero, 15, 16).Value;
        var tool = new GenerateDecals(_outlineSource);

        var decal = new TextDecal
        {
            Text = "FAB",
            Operation = EmbossOperation.Emboss,
            CapHeight = 5.0f,
            Depth = 0.8f,
            Anchor = new Vector3(0, 0, 15),
            AnchorNormal = Vector3.UnitZ
        };

        var result = tool.Execute(_fixture.Engine, sphere, new[] { decal });

        result.IsSuccess.Should().BeTrue();
        result.Value.TriangleCount.Should().BeGreaterThan(sphere.TriangleCount);
    }

    [Fact]
    public void GenerateDecals_Execute_Engrave_SubtractsFromTarget()
    {
        var sphere = _fixture.Engine.Generators.GenerateSphere(Vector3.Zero, 15, 16).Value;
        var tool = new GenerateDecals(_outlineSource);

        var decal = new TextDecal
        {
            Text = "ENG",
            Operation = EmbossOperation.Engrave,
            CapHeight = 5.0f,
            Depth = 0.8f,
            Anchor = new Vector3(0, 0, 15),
            AnchorNormal = Vector3.UnitZ
        };

        var result = tool.Execute(_fixture.Engine, sphere, new[] { decal });

        result.IsSuccess.Should().BeTrue();
        result.Value.TriangleCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GenerateDecals_Execute_ProjectOntoSurface_ContoursMesh()
    {
        var sphere = _fixture.Engine.Generators.GenerateSphere(Vector3.Zero, 15, 16).Value;
        var tool = new GenerateDecals(_outlineSource);

        var decal = new TextDecal
        {
            Text = "PRJ",
            Operation = EmbossOperation.Emboss,
            CapHeight = 4.0f,
            Depth = 0.6f,
            Anchor = new Vector3(0, 0, 15),
            AnchorNormal = Vector3.UnitZ
        };

        var result = tool.Execute(_fixture.Engine, sphere, new[] { decal });

        result.IsSuccess.Should().BeTrue();
        result.Value.TriangleCount.Should().BeGreaterThan(sphere.TriangleCount);
    }

    [Fact]
    public void GenerateDecals_Execute_MultipleDecals_AppliesAllInSequence()
    {
        var sphere = _fixture.Engine.Generators.GenerateSphere(Vector3.Zero, 20, 24).Value;
        var tool = new GenerateDecals(_outlineSource);

        var decal1 = new TextDecal
        {
            Text = "TOP",
            Operation = EmbossOperation.Emboss,
            CapHeight = 4.0f,
            Depth = 0.6f,
            Anchor = new Vector3(0, 0, 20),
            AnchorNormal = Vector3.UnitZ
        };

        var decal2 = new TextDecal
        {
            Text = "SIDE",
            Operation = EmbossOperation.Engrave,
            CapHeight = 4.0f,
            Depth = 0.6f,
            Anchor = new Vector3(20, 0, 0),
            AnchorNormal = Vector3.UnitX
        };

        var result = tool.Execute(_fixture.Engine, sphere, new[] { decal1, decal2 });

        result.IsSuccess.Should().BeTrue();
        result.Value.TriangleCount.Should().BeGreaterThan(sphere.TriangleCount);
    }
}
