using Fabolus.Tests.Fixtures;
using FluentAssertions;
using GeometryMeshLib;
using Xunit;

namespace Fabolus.Tests.MeshLib;

[Collection("GeometryEngine collection")]
public class GeometryEngineTests
{
    private readonly GeometryEngineFixture _fixture;

    public GeometryEngineTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void CreateMesh_FromValidData_ReturnsSuccessWithCorrectCounts()
    {
        var mesh = _fixture.UnitCube();

        mesh.VertexCount.Should().Be(8);
        mesh.TriangleCount.Should().Be(12);
        mesh.Metadata.Name.Should().Be("Generated Mesh");
    }

    [Fact]
    public void CloneMesh_ReturnsIndependentInstanceWithSameCounts()
    {
        var original = _fixture.UnitCube();
        
        var result = _fixture.Engine.CloneMesh(original);

        result.IsSuccess.Should().BeTrue();
        var clone = result.Value;
        clone.Should().NotBeSameAs(original);
        clone.VertexCount.Should().Be(original.VertexCount);
        clone.TriangleCount.Should().Be(original.TriangleCount);
    }

    [Fact]
    public void ValidateTopology_OnClosedSphere_ReturnsWatertightAndManifold()
    {
        var sphere = _fixture.LoadStl("sphere.stl");
        var engine = (GeometryEngine)_fixture.Engine; // Cast required if not on interface

        var result = engine.ValidateTopology(sphere);

        result.IsSuccess.Should().BeTrue();
        var topology = result.Value;
        topology.IsWatertight.Should().BeTrue();
        topology.IsManifold.Should().BeTrue();
        topology.SelfIntersectionCount.Should().Be(0);
        topology.VertexCount.Should().BeGreaterThan(0);
        topology.TriangleCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetStatistics_OnSphere_ReturnsValidStatistics()
    {
        var sphere = _fixture.LoadStl("sphere.stl");
        var engine = (GeometryEngine)_fixture.Engine;

        var result = engine.GetStatistics(sphere);

        result.IsSuccess.Should().BeTrue();
        var stats = result.Value;
        stats.Volume.Should().BeGreaterThan(0);
        stats.SurfaceArea.Should().BeGreaterThan(0);
        stats.MaxX.Should().BeGreaterThan(stats.MinX);
        stats.MaxY.Should().BeGreaterThan(stats.MinY);
        stats.MaxZ.Should().BeGreaterThan(stats.MinZ);
        stats.EdgeCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetRenderData_ReturnsCorrectSizedArrays()
    {
        var sphere = _fixture.LoadStl("sphere.stl");
        var engine = (GeometryEngine)_fixture.Engine;

        var result = engine.GetRenderData(sphere);

        result.IsSuccess.Should().BeTrue();
        var data = result.Value;
        
        data.Vertices.Length.Should().Be(sphere.VertexCount * 3);
        data.Normals.Length.Should().Be(data.Vertices.Length);
        data.Triangles.Length.Should().Be(sphere.TriangleCount * 3);
        
        foreach (var index in data.Triangles)
        {
            index.Should().BeLessThan(sphere.VertexCount);
        }
    }

    [Fact]
    public void CalculateDeviationColors_WithCurrentEqualsOriginal_ReturnsNearWhite()
    {
        var sphere = _fixture.LoadStl("sphere.stl");
        var engine = (GeometryEngine)_fixture.Engine;

        var result = engine.CalculateDeviationColors(sphere, sphere);

        result.IsSuccess.Should().BeTrue();
        var colors = result.Value;
        
        colors.Length.Should().Be(sphere.VertexCount * 3);
        
        // They should all be exactly 1.0 (white) for zero deviation
        foreach (var color in colors)
        {
            color.Should().BeApproximately(1.0, 1e-3);
        }
    }
}
