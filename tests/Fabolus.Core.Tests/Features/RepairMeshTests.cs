using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Geometry;
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
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.LoadStl("sphere.stl");
        var id = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(id).Value;

        var result = _repairFeature.Execute(workspace, id);

        result.IsSuccess.Should().BeTrue();
        using var repaired = result.Value.GetActiveMesh().Value;

        // Repair changes geometry, so the cached Stats/Topology must be recomputed - UI
        // consumers (hover paths, info panels) read these instead of re-deriving.
        var cachedStats = repaired.Metadata.MeshStats();
        cachedStats.HasValue.Should().BeTrue();
        var freshStats = _fixture.Engine.Evaluators.GetStatistics(repaired).Value;
        cachedStats.Value.TriangleCount.Should().Be(freshStats.TriangleCount);
        cachedStats.Value.Volume.Should().BeApproximately(freshStats.Volume, 1e-3);

        repaired.Metadata.Topology().HasValue.Should().BeTrue();
    }

    [Fact]
    public void Execute_PreservesBaseMeshAndId()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.LoadStl("sphere.stl");
        var id = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(id).Value;

        var result = _repairFeature.Execute(workspace, id);

        result.IsSuccess.Should().BeTrue();
        var metadata = result.Value.GetActiveMeshMetadata().Value;
        metadata.Id.Should().Be(id);
        metadata.HasBaseMesh.Should().BeTrue();
    }
}
