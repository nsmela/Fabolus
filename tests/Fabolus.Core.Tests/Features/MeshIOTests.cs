using System.IO;
using Fabolus.Core.Geometry;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Tests.Features;

[Collection("GeometryEngine collection")]
public class MeshIOTests
{
    private readonly GeometryEngineFixture _fixture;
    private readonly ImportMesh _importFeature;
    private readonly ExportMesh _exportFeature;
    private readonly RepairMesh _repairFeature;

    public MeshIOTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _importFeature = new ImportMesh(_fixture.Engine);
        _exportFeature = new ExportMesh(_fixture.Engine);
        _repairFeature = new RepairMesh(_fixture.Engine);
    }

    [Fact]
    public void ImportMesh_ValidFile_ImportsCentersAndAddsToWorkspace()
    {
        var workspace = Workspace.CreateEmpty();
        var filePath = _fixture.GetAssetPath("sphere.stl");

        var result = _importFeature.Execute(workspace, filePath);

        result.IsSuccess.Should().BeTrue();
        var updatedWorkspace = result.Value;

        updatedWorkspace.MeshCount.Should().Be(1);
        updatedWorkspace.ActiveMeshId.Should().NotBe(System.Guid.Empty);

        using var mesh = updatedWorkspace.GetActiveMesh().Value;
        
        // Ensure topology is validated
        mesh.Metadata.Topology().HasValue.Should().BeTrue();
        mesh.Metadata.Topology().Value.IsWatertight.Should().BeTrue();

        // Ensure centered
        var stats = _fixture.Engine.Evaluators.GetStatistics(mesh).Value;
        (stats.MinX + stats.MaxX).Should().BeApproximately(0, 0.01);
        (stats.MinY + stats.MaxY).Should().BeApproximately(0, 0.01);
        (stats.MinZ + stats.MaxZ).Should().BeApproximately(0, 0.01);
    }

    [Fact]
    public void ExportMesh_ValidMesh_ExportsToFile()
    {
        var mesh = _fixture.LoadStl("sphere.stl");
        var tempFile = Path.Combine(Path.GetTempPath(), $"{System.Guid.NewGuid()}.stl");

        try
        {
            var result = _exportFeature.Execute(mesh, tempFile);
            result.IsSuccess.Should().BeTrue();
            File.Exists(tempFile).Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void RepairMesh_ActiveMesh_RepairsAndUpdatesTopology()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.LoadStl("sphere.stl");
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(mesh.Metadata.Id).Value;

        var result = _repairFeature.Execute(workspace, mesh.Metadata.Id, fixSelfIntersections: false);

        result.IsSuccess.Should().BeTrue();
        using var repairedMesh = result.Value.GetActiveMesh().Value;

        repairedMesh.Metadata.Topology().HasValue.Should().BeTrue();
        repairedMesh.Metadata.Topology().Value.IsWatertight.Should().BeTrue();
    }
}
