using Fabolus.Tests.Fixtures;
using FluentAssertions;
using GeometryMeshLib;
using System.Numerics;
using Xunit;

namespace Fabolus.Tests.MeshLib;

[Collection("GeometryEngine collection")]
public class BooleansTests
{
    private readonly GeometryEngineFixture _fixture;
    private readonly GeometryEngine _engine;

    public BooleansTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _engine = (GeometryEngine)_fixture.Engine;
    }

    [Fact]
    public void Union_ReturnsCombinedWatertightMesh()
    {
        var sphereA = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        var sphereB = _fixture.Engine.Generators.GenerateSphere(new Vector3(5, 0, 0), 10).Value;
        
        var statsA = _engine.Evaluators.GetStatistics(sphereA).Value;
        var statsB = _engine.Evaluators.GetStatistics(sphereB).Value;

        var result = _fixture.Engine.Booleans.Union(sphereA, sphereB);

        result.IsSuccess.Should().BeTrue();
        var union = result.Value;
        
        var unionStats = _engine.Evaluators.GetStatistics(union).Value;
        unionStats.Volume.Should().BeGreaterThanOrEqualTo(statsA.Volume);
        unionStats.Volume.Should().BeLessThanOrEqualTo(statsA.Volume + statsB.Volume);
        
        var validation = _engine.Evaluators.ValidateTopology(union).Value;
        validation.IsWatertight.Should().BeTrue();
        
        union.Metadata.Name.Should().Be($"{sphereA.Metadata.Name} Union {sphereB.Metadata.Name}");
    }

    [Fact]
    public void Intersect_ReturnsIntersectionVolume()
    {
        var sphereA = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        var sphereB = _fixture.Engine.Generators.GenerateSphere(new Vector3(5, 0, 0), 10).Value;
        
        var statsA = _engine.Evaluators.GetStatistics(sphereA).Value;

        var result = _fixture.Engine.Booleans.Intersect(sphereA, sphereB);

        result.IsSuccess.Should().BeTrue();
        var intersect = result.Value;
        
        var intersectStats = _engine.Evaluators.GetStatistics(intersect).Value;
        intersectStats.Volume.Should().BeLessThanOrEqualTo(statsA.Volume);
        intersectStats.Volume.Should().BeGreaterThan(0); // Overlapping
        
        intersect.Metadata.Name.Should().Be($"{sphereA.Metadata.Name} Intersection {sphereB.Metadata.Name}");
    }

    [Fact]
    public void Subtract_ReturnsNonEmptyMeshWithReducedVolume()
    {
        var sphereA = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        var sphereB = _fixture.Engine.Generators.GenerateSphere(new Vector3(5, 0, 0), 10).Value;
        
        var statsA = _engine.Evaluators.GetStatistics(sphereA).Value;

        var result = _fixture.Engine.Booleans.Subtract(sphereA, sphereB);

        result.IsSuccess.Should().BeTrue();
        var subtract = result.Value;
        
        var subtractStats = _engine.Evaluators.GetStatistics(subtract).Value;
        subtractStats.Volume.Should().BeLessThan(statsA.Volume);
        
        subtract.Metadata.Name.Should().Be($"{sphereA.Metadata.Name} DifferenceAB {sphereB.Metadata.Name}");
    }
}
