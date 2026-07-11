using Fabolus.Core.Common;
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

        // Act
        var result = _sut.Execute(mesh, origin, normal);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var (top, bottom) = result.Value;

        top.Should().NotBeNull();
        bottom.Should().NotBeNull();

        // Check metadata names
        top.Metadata.Name.Should().Contain("(Top)");
        bottom.Metadata.Name.Should().Contain("(Bottom)");

        // Top should be above Z=0
        var topStats = _engine.Evaluators.GetStatistics(top).Value;
        topStats.MinZ.Should().BeGreaterThanOrEqualTo(-0.1f);

        // Bottom should be below Z=0
        var bottomStats = _engine.Evaluators.GetStatistics(bottom).Value;
        bottomStats.MaxZ.Should().BeLessThanOrEqualTo(0.1f);
    }
}
