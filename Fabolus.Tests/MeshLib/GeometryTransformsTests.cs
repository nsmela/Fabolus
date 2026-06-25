using Fabolus.Tests.Fixtures;
using FluentAssertions;
using GeometryMeshLib;
using System;
using Xunit;

namespace Fabolus.Tests.MeshLib;

[Collection("GeometryEngine collection")]
public class GeometryTransformsTests
{
    private readonly GeometryEngineFixture _fixture;
    private readonly GeometryEngine _engine;

    public GeometryTransformsTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _engine = (GeometryEngine)_fixture.Engine;
    }

    [Fact]
    public void Translate_ShiftsBoundsByDeltaAndPreservesVolume()
    {
        var original = _fixture.UnitCube(); // volume 1, bounds [-0.5, 0.5]
        var originalStats = _engine.GetStatistics(original).Value;

        var result = _fixture.Engine.Transforms.Translate(original, 10, -5, 2);

        result.IsSuccess.Should().BeTrue();
        var transformedStats = _engine.GetStatistics(result.Value).Value;

        transformedStats.MinX.Should().BeApproximately(originalStats.MinX + 10, 1e-3);
        transformedStats.MinY.Should().BeApproximately(originalStats.MinY - 5, 1e-3);
        transformedStats.MinZ.Should().BeApproximately(originalStats.MinZ + 2, 1e-3);
        
        transformedStats.Volume.Should().BeApproximately(originalStats.Volume, 1e-3);
    }

    [Fact]
    public void Scale_ScalesBoundsAndVolumeCorrectly()
    {
        var original = _fixture.UnitCube();
        var originalStats = _engine.GetStatistics(original).Value;
        double f = 2.0;

        var result = _fixture.Engine.Transforms.Scale(original, f);

        result.IsSuccess.Should().BeTrue();
        var transformedStats = _engine.GetStatistics(result.Value).Value;

        var newWidth = transformedStats.MaxX - transformedStats.MinX;
        var oldWidth = originalStats.MaxX - originalStats.MinX;
        newWidth.Should().BeApproximately(oldWidth * f, 1e-3);

        transformedStats.Volume.Should().BeApproximately(originalStats.Volume * Math.Pow(f, 3), 1e-3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Scale_ZeroOrNegative_ReturnsInvalidScale(double factor)
    {
        var original = _fixture.UnitCube();
        
        var result = _fixture.Engine.Transforms.Scale(original, factor);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Geometry.InvalidScale");
    }

    [Fact]
    public void Rotate_PreservesVolumeAndVertexCount()
    {
        var original = _fixture.UnitCube();
        var originalStats = _engine.GetStatistics(original).Value;

        var result = _fixture.Engine.Transforms.Rotate(original, Math.PI / 2, 0, 0, 1);

        result.IsSuccess.Should().BeTrue();
        var transformedStats = _engine.GetStatistics(result.Value).Value;

        result.Value.VertexCount.Should().Be(original.VertexCount);
        transformedStats.Volume.Should().BeApproximately(originalStats.Volume, 1e-3);
    }

    [Fact]
    public void Transforms_PropagateOriginalMesh()
    {
        var original = _fixture.UnitCube();
        // create a derived mesh
        var derived = _fixture.Engine.Modifiers.Offset(original, 0.1f).Value;

        var transformed = _fixture.Engine.Transforms.Translate(derived, 1, 1, 1).Value;

        transformed.OriginalMesh.Should().NotBeNull();
        transformed.OriginalMesh.Should().NotBeSameAs(original); // Should be a translated copy of original
        
        var originalOfTransformedStats = _engine.GetStatistics(transformed.OriginalMesh).Value;
        var originalStats = _engine.GetStatistics(original).Value;

        originalOfTransformedStats.MinX.Should().BeApproximately(originalStats.MinX + 1, 1e-3);
    }
}
