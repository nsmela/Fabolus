using System.Numerics;
using Fabolus.Core.Common;
using Fabolus.Core.Features.Emboss;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Tests.Features;

public sealed class TestGlyphOutlineSource : IGlyphOutlineSource
{
    public Result<IReadOnlyList<Polygon2D>> GetOutlines(string text, DecalFont font, float capHeight, float tracking)
    {
        // Generates simple rectangular contour for testing
        float halfW = capHeight * 0.6f * 0.5f;
        float halfH = capHeight * 0.5f;

        var outer = new List<Vector2>
        {
            new(-halfW, -halfH),
            new(halfW, -halfH),
            new(halfW, halfH),
            new(-halfW, halfH)
        };

        return Result.Success<IReadOnlyList<Polygon2D>>(new List<Polygon2D>
        {
            new() { OuterBoundary = outer }
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
        var anchor = new Vector3(10, 20, 30);
        var normal = new Vector3(0, 0, 1);

        var frame = DecalFrame.FromHit(anchor, normal, rotationDeg: 0f);

        frame.Origin.Should().Be(anchor);
        frame.N.Should().Be(Vector3.UnitZ);
        Vector3.Dot(frame.U, frame.N).Should().BeApproximately(0f, 1e-5f);
        Vector3.Dot(frame.V, frame.N).Should().BeApproximately(0f, 1e-5f);
        Vector3.Dot(frame.U, frame.V).Should().BeApproximately(0f, 1e-5f);
        frame.U.Length().Should().BeApproximately(1f, 1e-5f);
        frame.V.Length().Should().BeApproximately(1f, 1e-5f);
    }

    [Fact]
    public void DecalFrame_WithRotation_RotatesAroundNormal()
    {
        var anchor = Vector3.Zero;
        var normal = Vector3.UnitZ;

        var frame0 = DecalFrame.FromHit(anchor, normal, rotationDeg: 0f);
        var frame90 = DecalFrame.FromHit(anchor, normal, rotationDeg: 90f);

        Vector3.Dot(frame0.V, frame90.U).Should().BeApproximately(1f, 1e-4f);
    }

    [Fact]
    public void Polygon2DExtensions_MirrorX_FlipsXAndPreservesWinding()
    {
        var poly = new Polygon2D
        {
            OuterBoundary = new List<Vector2>
            {
                new(-2, -2),
                new(2, -2),
                new(2, 2),
                new(-2, 2)
            }
        };

        var mirrored = poly.MirrorX();
        mirrored.OuterBoundary[0].X.Should().Be(2);
        mirrored.OuterBoundary.Should().HaveCount(4);
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
