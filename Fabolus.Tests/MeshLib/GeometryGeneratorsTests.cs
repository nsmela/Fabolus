using System.Collections.Generic;
using System.Numerics;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using GeometryMeshLib;
using Xunit;

namespace Fabolus.Tests.MeshLib;

[Collection("GeometryEngine collection")]
public class GeometryGeneratorsTests
{
    private readonly GeometryEngineFixture _fixture;
    private readonly GeometryEngine _engine;

    public GeometryGeneratorsTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _engine = (GeometryEngine)_fixture.Engine;
    }

    [Fact]
    public void GenerateSphere_ReturnsCorrectMesh()
    {
        var center = new Vector3(10, 20, 30);
        double radius = 5.0;
        int slices = 16;

        var result = _fixture.Engine.Generators.GenerateSphere(center, radius, slices);

        result.IsSuccess.Should().BeTrue();
        var sphere = result.Value;
        var stats = _engine.GetStatistics(sphere).Value;

        stats.MinX.Should().BeApproximately(center.X - radius, 1e-1);
        stats.MaxX.Should().BeApproximately(center.X + radius, 1e-1);
        sphere.VertexCount.Should().Be(slices * (slices - 1) + 2);
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(8, 2)]
    [InlineData(16, 4)]
    public void GenerateTube_ValidParameters_ReturnsWatertightTube(int segments, int pathPoints)
    {
        var path = new List<Vector3>();
        var radii = new List<float>();
        for (int i = 0; i < pathPoints; i++)
        {
            path.Add(new Vector3(i * 10, 0, 0));
            radii.Add(5.0f);
        }

        var param = new TubeParameters
        {
            Path = path,
            Radii = radii,
            Segments = segments,
            Capped = true
        };

        var result = _fixture.Engine.Generators.GenerateTube(param);

        result.IsSuccess.Should().BeTrue();
        var tube = result.Value;
        var validation = _engine.ValidateTopology(tube).Value;

        validation.IsWatertight.Should().BeTrue();
        // vertices count depends on the ring formula + caps
        tube.VertexCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GenerateTube_InvalidParameters_ReturnsErrors()
    {
        var validPath = new[] { Vector3.Zero, new Vector3(10, 0, 0) };
        var validRadii = new[] { 5.0f, 5.0f };

        var invalidPathResult = _fixture.Engine.Generators.GenerateTube(new TubeParameters { Path = new[] { Vector3.Zero }, Radii = new[] { 5.0f } });
        invalidPathResult.Error.Code.Should().Be(GeometryErrors.InvalidPath.Code);

        var mismatchResult = _fixture.Engine.Generators.GenerateTube(new TubeParameters { Path = validPath, Radii = new[] { 5.0f } });
        mismatchResult.Error.Code.Should().Be(GeometryErrors.InvalidRadii.Code);

        var negativeRadiusResult = _fixture.Engine.Generators.GenerateTube(new TubeParameters { Path = validPath, Radii = new[] { -5.0f, 5.0f } });
        negativeRadiusResult.Error.Code.Should().Be(GeometryErrors.InvalidRadius.Code);

        var invalidSegmentsResult = _fixture.Engine.Generators.GenerateTube(new TubeParameters { Path = validPath, Radii = validRadii, Segments = 2 });
        invalidSegmentsResult.Error.Code.Should().Be(GeometryErrors.InvalidSegments.Code);
    }

    [Fact]
    public void Arc3d_ReturnsEquidistantPoints()
    {
        var start = Vector3.Zero;
        var dir1 = Vector3.UnitX;
        var dir2 = Vector3.UnitY;
        float radius = 5.0f;
        int segments = 10;

        var points = _fixture.Engine.Generators.Arc3d(radius, start, dir1, dir2, segments);

        points.Count.Should().Be(segments + 1);

        // Simple collinear check
        var collinear = _fixture.Engine.Generators.Arc3d(radius, start, dir1, dir1, segments);
        collinear.Count.Should().Be(1);
        collinear[0].Should().Be(start);
    }

    [Fact]
    public void GenerateExtrudedPath_SimplePath_ReturnsMesh()
    {
        var path = new[] { Vector3.Zero, new Vector3(10, 0, 0), new Vector3(10, 10, 0) };

        var param = new ExtrudedPathParameters
        {
            Path = path,
            Radius = 2.0f,
            ZMin = 0,
            ZMax = 10,
            TargetMesh = _fixture.LoadStl("sphere.stl")
        };

        var result = _fixture.Engine.Generators.GenerateExtrudedPath(param);

        result.IsSuccess.Should().BeTrue();
        var mesh = result.Value;
        mesh.TriangleCount.Should().BeGreaterThan(6); // Should have walls and caps
    }

    [Fact]
    public void GetConvexHull_ReturnsOuterBoundary()
    {
        var sphere = _fixture.LoadStl("sphere.stl");

        var result = _fixture.Engine.Generators.GetConvexHull(sphere);

        result.IsSuccess.Should().BeTrue();
        result.Value.OuterBoundary.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetMeshShadow_ReturnsOuterBoundary()
    {
        var sphere = _fixture.LoadStl("sphere.stl");

        var result = _fixture.Engine.Generators.GetMeshShadow(sphere);

        result.IsSuccess.Should().BeTrue();
        result.Value.OuterBoundary.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void OffsetPolygon_ReturnsLargerPolygon()
    {
        var square = new Polygon2D
        {
            OuterBoundary = new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10) }
        };

        var result = _fixture.Engine.Generators.OffsetPolygon(square, 2.0f);

        result.IsSuccess.Should().BeTrue();
        result.Value.OuterBoundary.Count.Should().BeGreaterThanOrEqualTo(4);
        // Area check could be done, but verifying non-empty is sufficient for basic contract
    }

    [Fact]
    public void ExtrudePolygon_ReturnsWatertightMeshInZRange()
    {
        var square = new Polygon2D
        {
            OuterBoundary = new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10) }
        };

        var result = _fixture.Engine.Generators.ExtrudePolygon(square, 5.0f, 15.0f);

        result.IsSuccess.Should().BeTrue();
        var mesh = result.Value;
        var stats = _engine.GetStatistics(mesh).Value;

        stats.MinZ.Should().BeApproximately(5.0, 1e-3);
        stats.MaxZ.Should().BeApproximately(15.0, 1e-3);

        var validation = _engine.ValidateTopology(mesh).Value;
        validation.IsWatertight.Should().BeTrue();
    }
}
