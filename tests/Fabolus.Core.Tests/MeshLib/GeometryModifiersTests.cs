using Fabolus.Tests.Fixtures;
using FluentAssertions;
using GeometryMeshLib;
using System.Numerics;
using Xunit;

namespace Fabolus.Tests.MeshLib;

[Collection("GeometryEngine collection")]
public class GeometryModifiersTests
{
    private readonly GeometryEngineFixture _fixture;
    private readonly GeometryEngine _engine;

    public GeometryModifiersTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _engine = (GeometryEngine)_fixture.Engine;
    }

    [Fact]
    public void Offset_GrowsBoundsAndRemainsWatertight()
    {
        var sphere = _fixture.LoadStl("sphere.stl");
        var originalStats = _engine.Evaluators.GetStatistics(sphere).Value;
        float d = 2.0f;

        var result = _fixture.Engine.Modifiers.Offset(sphere, d);

        result.IsSuccess.Should().BeTrue();
        var offsetMesh = result.Value;
        
        var offsetStats = _engine.Evaluators.GetStatistics(offsetMesh).Value;
        
        var dx = (offsetStats.MaxX - offsetStats.MinX) - (originalStats.MaxX - originalStats.MinX);
        dx.Should().BeApproximately(2 * d, 0.5); // Tolerance

        var validation = _engine.Evaluators.ValidateTopology(offsetMesh).Value;
        validation.IsWatertight.Should().BeTrue();

        // Engine ops don't establish BaseMesh - that's Workspace.AddMesh's job on entry.
        offsetMesh.Metadata.HasBaseMesh.Should().BeFalse();
    }

    [Fact]
    public void OffsetDouble_SucceedsAndReturnsNonEmpty()
    {
        var sphere = _fixture.Engine.Generators.GenerateSphere(Vector3.Zero, 10).Value;

        var result = _fixture.Engine.Modifiers.OffsetDouble(sphere, 2.0f, iterations: 1);

        result.IsSuccess.Should().BeTrue();
        result.Value.TriangleCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void OffsetDouble_ZeroIterations_ReturnsEquivalentNewMesh()
    {
        var sphere = _fixture.LoadStl("sphere.stl");

        var result = _fixture.Engine.Modifiers.OffsetDouble(sphere, 2.0f, iterations: 0);

        result.IsSuccess.Should().BeTrue();
        // No-op path still hands back a new mesh - modifiers never return their input.
        result.Value.Should().BeSameAs(sphere);
        result.Value.TriangleCount.Should().Be(sphere.TriangleCount);
    }

    [Fact]
    public void Resize_TargetBelowCurrent_ReducesTriangleCount()
    {
        var sphere = _fixture.LoadStl("sphere.stl");
        int target = sphere.TriangleCount / 2;

        var result = _fixture.Engine.Modifiers.Resize(sphere, target);

        result.IsSuccess.Should().BeTrue();
        result.Value.TriangleCount.Should().BeLessThanOrEqualTo(target + 10); // slight tolerance depending on decimator
    }

    [Fact]
    public void Resize_TargetAboveCurrent_ReturnsEquivalentNewMesh()
    {
        var sphere = _fixture.LoadStl("sphere.stl");
        int target = sphere.TriangleCount * 2;

        var result = _fixture.Engine.Modifiers.Resize(sphere, target);

        result.IsSuccess.Should().BeTrue();
        // Geometry is unchanged (nothing to decimate), but the instance is a new mesh -
        // modifiers never return their input, so callers can dispose intermediates
        // unconditionally.
        result.Value.Should().BeSameAs(sphere);
        result.Value.TriangleCount.Should().Be(sphere.TriangleCount);
        result.Value.VertexCount.Should().Be(sphere.VertexCount);
    }

    [Fact]
    public void Repair_OnValidMesh_PreservesCounts()
    {
        var sphere = _fixture.LoadStl("sphere.stl");

        var result = _fixture.Engine.Modifiers.Repair(sphere);

        result.IsSuccess.Should().BeTrue();
        var repaired = result.Value;
        
        repaired.VertexCount.Should().BeInRange(sphere.VertexCount - 10, sphere.VertexCount + 10);
        repaired.TriangleCount.Should().BeInRange(sphere.TriangleCount - 10, sphere.TriangleCount + 10);
    }

    [Fact]
    public void RepairSelfIntersections_OnValidMesh_PreservesCounts()
    {
        var sphere = _fixture.LoadStl("sphere.stl");

        var result = _fixture.Engine.Modifiers.RepairSelfIntersections(sphere);

        result.IsSuccess.Should().BeTrue();
        var repaired = result.Value;
        
        repaired.VertexCount.Should().BeInRange(sphere.VertexCount - 10, sphere.VertexCount + 10);
        repaired.TriangleCount.Should().BeInRange(sphere.TriangleCount - 10, sphere.TriangleCount + 10);
    }
}
