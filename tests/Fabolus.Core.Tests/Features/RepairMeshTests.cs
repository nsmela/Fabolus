using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Tests.Features;

[Collection("GeometryEngine collection")]
public class RepairMeshTests
{
    private readonly GeometryEngineFixture _fixture;
    private readonly RepairMesh _repairFeature;

    public RepairMeshTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _repairFeature = new RepairMesh(_fixture.Engine);
    }

    [Fact]
    public void Execute_RefreshesCachedStatsAndTopology()
    {
        var mesh = _fixture.LoadStl("sphere.stl");
        var (workspace, id) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), mesh);

        var result = _repairFeature.Execute(workspace, id);

        result.IsSuccess.Should().BeTrue();
        var repaired = result.Value.GetActiveMesh().Value;

        // Repair rebuilds geometry, so the engine drops the annotations on the way through and
        // the feature has to measure again - UI consumers (hover paths, info panels) read these
        // instead of re-deriving them.
        var cachedStats = repaired.Stats();
        cachedStats.Should().NotBeNull();

        var freshStats = _fixture.Engine.Evaluators.GetStatistics(repaired).Value;
        cachedStats!.TriangleCount.Should().Be(freshStats.TriangleCount);
        cachedStats.Volume.Should().BeApproximately(freshStats.Volume, 1e-3);

        repaired.Topology().Should().NotBeNull();
    }

    [Fact]
    public void Execute_PreservesBaseMeshAndId()
    {
        var mesh = _fixture.LoadStl("sphere.stl");
        var (workspace, id) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), mesh);

        var result = _repairFeature.Execute(workspace, id);

        result.IsSuccess.Should().BeTrue();
        var record = result.Value.GetActiveRecord().Value;
        record.Id.Should().Be(id);
        record.BaseMesh.Should().NotBeNull();
    }
}
