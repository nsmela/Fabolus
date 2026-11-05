using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Tests.Features;

/// <summary>
/// What closing the app would lose: entries whose geometry no longer matches the file it was
/// imported from or last exported to.
/// </summary>
[Collection("GeometryEngine collection")]
public class UnsavedWorkTests
{
    private readonly IGeometryEngine _engine;

    public UnsavedWorkTests(GeometryEngineFixture fixture) => _engine = fixture.Engine;

    private IMesh Box(double size) =>
        _engine.Generators.GenerateBox(new Vector3(0, 0, 0), new Vector3(size, size, size)).Value;

    [Fact]
    public void AnEmptyWorkspace_HasNothingUnsaved()
    {
        new UnsavedWork().Unsaved(Workspace.CreateEmpty()).Should().BeEmpty();
    }

    [Fact]
    public void AMeshJustImported_IsNotUnsaved()
    {
        var (workspace, id) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), Box(10));
        var work = new UnsavedWork();

        work.MarkSaved(workspace, [id]);

        work.Unsaved(workspace).Should().BeEmpty();
    }

    [Fact]
    public void AMeshChangedSinceItWasSaved_IsUnsaved()
    {
        var (workspace, id) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), Box(10), "bolus");
        var work = new UnsavedWork();
        work.MarkSaved(workspace, [id]);

        var edited = workspace.UpdateMesh(id, Box(12), workspace.GetRecord(id).Value).Value;

        work.Unsaved(edited).Should().ContainSingle().Which.Name.Should().Be("bolus");
    }

    [Fact]
    public void AChangedMesh_ExportedAgain_IsNotUnsaved()
    {
        var (workspace, id) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), Box(10));
        var work = new UnsavedWork();
        work.MarkSaved(workspace, [id]);

        var edited = workspace.UpdateMesh(id, Box(12), workspace.GetRecord(id).Value).Value;
        work.MarkSaved(edited, [id]);

        work.Unsaved(edited).Should().BeEmpty();
    }

    /// <summary>A split piece, say: made in the app and never written anywhere.</summary>
    [Fact]
    public void AMeshMadeInTheApp_IsUnsaved()
    {
        var (workspace, imported) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), Box(10), "bolus");
        var work = new UnsavedWork();
        work.MarkSaved(workspace, [imported]);

        var (withPiece, _) = GeometryEngineFixture.AddActive(workspace, Box(5), "piece");

        work.Unsaved(withPiece).Should().ContainSingle().Which.Name.Should().Be("piece");
    }

    [Fact]
    public void ADeletedMesh_IsNotUnsaved()
    {
        var (workspace, id) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), Box(10));
        var work = new UnsavedWork();

        var removed = workspace.RemoveMesh(id).Value;

        work.Unsaved(removed).Should().BeEmpty();
    }
}
