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

        var mesh = updatedWorkspace.GetActiveMesh().Value;
        
        // Ensure topology is validated
        mesh.Metadata.Topology().HasValue.Should().BeTrue();
        mesh.Metadata.Topology().Value.IsWatertight.Should().BeTrue();

        // Ensure centered
        var stats = _fixture.Engine.Evaluators.GetStatistics(mesh).Value;
        (stats.MinX + stats.MaxX).Should().BeApproximately(0, 0.01);
        (stats.MinY + stats.MaxY).Should().BeApproximately(0, 0.01);
        (stats.MinZ + stats.MaxZ).Should().BeApproximately(0, 0.01);
    }

    /// <summary>
    /// The import-time centring is recorded as a TranslateCommand against the pristine
    /// BaseMesh rather than baked into the geometry, so the offset from the authored position
    /// is available to later features and replaying the history reproduces the centred mesh.
    /// </summary>
    [Fact]
    public void ImportMesh_RecordsCentringAsTranslateCommandOverPristineBase()
    {
        var filePath = _fixture.GetAssetPath("sphere.stl");
        var raw = _fixture.Engine.IO.Import(filePath).Value;
        var rawStats = _fixture.Engine.Evaluators.GetStatistics(raw).Value;

        var mesh = _importFeature.Execute(Workspace.CreateEmpty(), filePath).Value.GetActiveMesh().Value;

        var translate = mesh.Metadata.Commands
            .OfType<Fabolus.Core.Features.Transforms.TranslateCommand>().Single();
        translate.Translation.X.Should().BeApproximately(-rawStats.Centre.X, 0.001f);
        translate.Translation.Y.Should().BeApproximately(-rawStats.Centre.Y, 0.001f);
        translate.Translation.Z.Should().BeApproximately(-rawStats.Centre.Z, 0.001f);

        // BaseMesh keeps the authored position; the command is what moves it to the origin.
        var baseStats = _fixture.Engine.Evaluators.GetStatistics(mesh.Metadata.GetBaseMesh().Value).Value;
        baseStats.Centre.X.Should().BeApproximately(rawStats.Centre.X, 0.001f);
        baseStats.Centre.Y.Should().BeApproximately(rawStats.Centre.Y, 0.001f);
        baseStats.Centre.Z.Should().BeApproximately(rawStats.Centre.Z, 0.001f);

        var replayed = Fabolus.Core.Geometry.Metadata.CommandReplay.Apply(
            _fixture.Engine, mesh.Metadata.GetBaseMesh().Value, mesh.Metadata.Commands).Value;
        var replayedStats = _fixture.Engine.Evaluators.GetStatistics(replayed).Value;
        replayedStats.Centre.X.Should().BeApproximately(0, 0.01f);
        replayedStats.Centre.Y.Should().BeApproximately(0, 0.01f);
        replayedStats.Centre.Z.Should().BeApproximately(0, 0.01f);
    }

    /// <summary>
    /// A mesh re-imported from a Fabolus-saved 3mf is already in the frame its BaseMesh
    /// replays into. Centring it a second time would shift the geometry without shifting the
    /// BaseMesh, leaving the smoothing/rotate views drawing the model offset from the viewport.
    /// </summary>
    [Fact]
    public void ImportMesh_MeshWithOwnHistory_StaysAlignedWithItsBaseMesh()
    {
        var filePath = _fixture.GetAssetPath("chin_legacy_smooth.3mf");

        var mesh = _importFeature.Execute(Workspace.CreateEmpty(), filePath).Value.GetActiveMesh().Value;

        // The saved history must survive import untouched - re-centring would append a
        // TranslateCommand, and WithCommand's cascade would drop the mould that depended on it.
        mesh.Metadata.Commands.Should().HaveCount(3);
        mesh.Metadata.Commands.Should().ContainSingle(c => c is Fabolus.Core.Features.Moulds.ConcaveMouldDefinition);

        // Replayed explicitly rather than via GetMeshAtStage, which short-circuits and hands
        // back the input mesh when nothing outranks the requested stage.
        var transformCommands = mesh.Metadata.Commands
            .Where(c => c.Priority <= Fabolus.Core.Geometry.Metadata.CommandPriority.Transform)
            .ToList();
        var baseCopy = _fixture.Engine.CloneMesh(mesh.Metadata.GetBaseMesh().Value).Value;
        var replay = Fabolus.Core.Geometry.Metadata.CommandReplay.Apply(_fixture.Engine, baseCopy, transformCommands);
        replay.IsSuccess.Should().BeTrue();

        var shown = _fixture.Engine.Evaluators.GetStatistics(mesh).Value.Centre;
        var replayed = _fixture.Engine.Evaluators.GetStatistics(replay.Value).Value.Centre;

        replayed.X.Should().BeApproximately(shown.X, 0.5f);
        replayed.Y.Should().BeApproximately(shown.Y, 0.5f);
        replayed.Z.Should().BeApproximately(shown.Z, 0.5f);
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
        var repairedMesh = result.Value.GetActiveMesh().Value;

        repairedMesh.Metadata.Topology().HasValue.Should().BeTrue();
        repairedMesh.Metadata.Topology().Value.IsWatertight.Should().BeTrue();
    }
}
