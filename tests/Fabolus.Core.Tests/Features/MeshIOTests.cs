using System.IO;
using System.Linq;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Features.Transforms;
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

    private static Vector3 Centre(MeshStatistics stats) => (stats.BoundsMin + stats.BoundsMax) / 2.0;

    [Fact]
    public void ImportMesh_ValidFile_ImportsCentersAndAddsToWorkspace()
    {
        var workspace = Workspace.CreateEmpty();
        var filePath = _fixture.GetAssetPath("sphere.stl");

        var result = _importFeature.Execute(workspace, filePath);

        result.IsSuccess.Should().BeTrue();
        var updatedWorkspace = result.Value;

        updatedWorkspace.MeshCount.Should().Be(1);

        // The entry has an identity. This is the regression that started the whole refactor:
        // nothing set one, and AddMesh threw on the first file opened.
        updatedWorkspace.ActiveMeshId.Should().NotBe(System.Guid.Empty);

        var record = updatedWorkspace.GetActiveRecord().Value;
        record.Name.Should().Be("sphere");
        record.BaseMesh.Should().NotBeNull();

        var mesh = updatedWorkspace.GetActiveMesh().Value;

        // Ensure topology is validated
        mesh.Topology().Should().NotBeNull();
        mesh.Topology()!.IsWatertight.Should().BeTrue();

        // Ensure centered
        var stats = _fixture.Engine.Evaluators.GetStatistics(mesh).Value;
        (stats.BoundsMin.X + stats.BoundsMax.X).Should().BeApproximately(0, 0.01);
        (stats.BoundsMin.Y + stats.BoundsMax.Y).Should().BeApproximately(0, 0.01);
        (stats.BoundsMin.Z + stats.BoundsMax.Z).Should().BeApproximately(0, 0.01);
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
        var rawCentre = Centre(_fixture.Engine.Evaluators.GetStatistics(raw).Value);

        var workspace = _importFeature.Execute(Workspace.CreateEmpty(), filePath).Value;
        var record = workspace.GetActiveRecord().Value;

        var translate = record.Commands.OfType<TranslateCommand>().Single();
        translate.Translation.X.Should().BeApproximately(-(float)rawCentre.X, 0.001f);
        translate.Translation.Y.Should().BeApproximately(-(float)rawCentre.Y, 0.001f);
        translate.Translation.Z.Should().BeApproximately(-(float)rawCentre.Z, 0.001f);

        // BaseMesh keeps the authored position; the command is what moves it to the origin.
        var baseCentre = Centre(_fixture.Engine.Evaluators.GetStatistics(record.BaseMesh!).Value);
        baseCentre.X.Should().BeApproximately(rawCentre.X, 0.001);
        baseCentre.Y.Should().BeApproximately(rawCentre.Y, 0.001);
        baseCentre.Z.Should().BeApproximately(rawCentre.Z, 0.001);

        // The base mesh also carries the stats measured for the centring, so the Smoothing
        // panel's "Original Mesh" figures have something to read without measuring again.
        record.BaseMesh!.Stats().Should().NotBeNull();

        var replayed = CommandReplay.Apply(_fixture.Engine, record.BaseMesh!, record.Commands).Value;
        var replayedCentre = Centre(_fixture.Engine.Evaluators.GetStatistics(replayed).Value);
        replayedCentre.X.Should().BeApproximately(0, 0.01);
        replayedCentre.Y.Should().BeApproximately(0, 0.01);
        replayedCentre.Z.Should().BeApproximately(0, 0.01);
    }

    /// <summary>
    /// A mesh re-imported from a Fabolus-saved 3mf arrives with its own command history, already
    /// in the frame its BaseMesh replays into: centring it a second time would shift the geometry
    /// without shifting the BaseMesh, leaving the smoothing/rotate views drawing the model offset
    /// from the viewport.
    /// </summary>
    [Fact]
    public void ImportMesh_MeshWithOwnHistory_StaysAlignedWithItsBaseMesh()
    {
        var filePath = _fixture.GetAssetPath("chin_legacy_smooth.3mf");

        var workspace = _importFeature.Execute(Workspace.CreateEmpty(), filePath).Value;
        var record = workspace.GetActiveRecord().Value;
        var mesh = workspace.GetActiveMesh().Value;

        // The saved history must survive import untouched - re-centring would append a
        // TranslateCommand, and WithCommand's cascade would drop the mould that depended on it.
        record.Commands.Should().HaveCount(3);
        record.Commands.Should().ContainSingle(c => c is Fabolus.Core.Features.Moulds.ConcaveMouldDefinition);

        // Replayed explicitly rather than via GetMeshAtStage, which short-circuits and hands
        // back the input mesh when nothing outranks the requested stage.
        var transformCommands = record.Commands
            .Where(c => c.Priority <= CommandPriority.Transform)
            .ToList();
        var replay = CommandReplay.Apply(_fixture.Engine, record.BaseMesh!, transformCommands);
        replay.IsSuccess.Should().BeTrue();

        var shown = Centre(_fixture.Engine.Evaluators.GetStatistics(mesh).Value);
        var replayed = Centre(_fixture.Engine.Evaluators.GetStatistics(replay.Value).Value);

        replayed.X.Should().BeApproximately(shown.X, 0.5);
        replayed.Y.Should().BeApproximately(shown.Y, 0.5);
        replayed.Z.Should().BeApproximately(shown.Z, 0.5);
    }

    [Fact]
    public void ExportMesh_ValidMesh_ExportsToFile()
    {
        var mesh = _fixture.LoadStl("sphere.stl");
        var record = MeshRecord.ForImport("sphere");
        var tempFile = Path.Combine(Path.GetTempPath(), $"{System.Guid.NewGuid()}.stl");

        try
        {
            var result = _exportFeature.Execute(mesh, record, tempFile);
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
        var mesh = _fixture.LoadStl("sphere.stl");
        var (workspace, id) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), mesh);

        var result = _repairFeature.Execute(workspace, id, fixSelfIntersections: false);

        result.IsSuccess.Should().BeTrue();
        var repairedMesh = result.Value.GetActiveMesh().Value;

        repairedMesh.Topology().Should().NotBeNull();
        repairedMesh.Topology()!.IsWatertight.Should().BeTrue();
    }
}
