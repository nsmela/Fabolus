using BasicResults;
using Fabolus.Core.Features.CutSplit;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Geometry;
using FluentAssertions;
using System.Numerics;
using Xunit;
using Fabolus.Tests.Fixtures;

namespace Fabolus.Core.Tests.Features.CutSplit;

[Collection("GeometryEngine collection")]
public class CutMeshFeatureTests
{
    private readonly IGeometryEngine _engine;
    private readonly CutMeshFeature _sut;

    public CutMeshFeatureTests(GeometryEngineFixture fixture)
    {
        _engine = fixture.Engine;
        _sut = new CutMeshFeature(_engine);
    }

    [Fact]
    public void Execute_WithValidMesh_ReturnsTopAndBottom()
    {
        // Arrange
        var sphereResult = _engine.Generators.GenerateSphere(Vector3.Zero, 10.0, 32);
        sphereResult.IsSuccess.Should().BeTrue();
        var mesh = sphereResult.Value;

        var origin = Vector3.Zero;
        var normal = Vector3.UnitZ;

        var record = MeshRecord.ForImport("sphere");

        // Act
        var result = _sut.Execute(mesh, record, origin, normal);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var (top, bottom) = result.Value;

        top.Mesh.Should().NotBeNull();
        bottom.Mesh.Should().NotBeNull();

        // Each half is a new entry named after the one it was cut from...
        top.Record.Name.Should().Contain("(Top)");
        bottom.Record.Name.Should().Contain("(Bottom)");

        // ...with its own identity, rather than inheriting the original's.
        top.Record.Id.Should().NotBe(record.Id);
        bottom.Record.Id.Should().NotBe(record.Id);
        top.Record.Id.Should().NotBe(bottom.Record.Id);

        // Top should be above Z=0
        var topStats = _engine.Evaluators.GetStatistics(top.Mesh).Value;
        topStats.BoundsMin.Z.Should().BeGreaterThanOrEqualTo(-0.1f);

        // Bottom should be below Z=0
        var bottomStats = _engine.Evaluators.GetStatistics(bottom.Mesh).Value;
        bottomStats.BoundsMax.Z.Should().BeLessThanOrEqualTo(0.1f);
    }
}
