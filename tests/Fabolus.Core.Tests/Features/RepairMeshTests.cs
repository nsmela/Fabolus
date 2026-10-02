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
    public void Execute_MeasurementsDescribeTheRepairedGeometry()
    {
        var mesh = _fixture.LoadStl("sphere.stl");
        var (workspace, id) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), mesh.Measured(_fixture.Engine));

        var result = _repairFeature.Execute(workspace, id);

        result.IsSuccess.Should().BeTrue();
        var repaired = result.Value.GetActiveMesh().Value;

        // The input was measured first, so anything the engine wrongly carried through a repair
        // would show here as figures for the old surface. They are checked against the same
        // geometry built afresh, which nothing has measured.
        var unmeasured = GeometryEngine.Core.Geometry.ImmutableMesh.Create(repaired.Vertices, repaired.Triangles, MeshMetadata.Named("fresh")).Value;
        var fresh = _fixture.Engine.Evaluators.GetStatistics(unmeasured).Value;

        var stats = repaired.Stats(_fixture.Engine);
        stats.Should().NotBeNull();
        stats!.TriangleCount.Should().Be(fresh.TriangleCount);
        stats.Volume.Should().BeApproximately(fresh.Volume, 1e-9);

        repaired.Topology(_fixture.Engine).Should().Be(_fixture.Engine.Evaluators.ValidateTopology(unmeasured).Value);
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
