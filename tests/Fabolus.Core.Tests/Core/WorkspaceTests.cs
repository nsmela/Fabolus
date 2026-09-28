using Fabolus.Core.Geometry;
using FluentAssertions;
using GeometryEngine.Core.Geometry;
using System;
using Xunit;

namespace Fabolus.Tests.Core;

public class WorkspaceTests
{
    // Geometry is irrelevant to every assertion here - the workspace stores a mesh, it never
    // inspects one - so these use an empty mesh and vary only the record beside it.
    private static IMesh Mesh(string name = "Mock") => ImmutableMesh.Empty.WithMetadata(MeshMetadata.Named(name));

    private static MeshRecord Record(Guid id, string name = "Mock") => new() { Id = id, Name = name };

    [Fact]
    public void CreateEmpty_ReturnsEmptyWorkspace()
    {
        var workspace = Workspace.CreateEmpty();

        workspace.MeshCount.Should().Be(0);
        workspace.ActiveMeshId.Should().Be(Guid.Empty);
        workspace.Records.Should().BeEmpty();
    }

    [Fact]
    public void AddMesh_AddsMeshToWorkspace()
    {
        var id = Guid.NewGuid();

        var result = Workspace.CreateEmpty().AddMesh(Mesh(), Record(id), setActive: false);

        result.IsSuccess.Should().BeTrue();
        result.Value.MeshCount.Should().Be(1);
        result.Value.ContainsMesh(id).Should().BeTrue();
        result.Value.ActiveMeshId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void AddMesh_WithSetActiveTrue_MakesMeshActive()
    {
        var id = Guid.NewGuid();

        var result = Workspace.CreateEmpty().AddMesh(Mesh(), Record(id), setActive: true);

        result.IsSuccess.Should().BeTrue();
        result.Value.ActiveMeshId.Should().Be(id);
    }

    [Fact]
    public void AddMesh_EstablishesABaseMeshSoTheEntryCanReplayFromTheStart()
    {
        var id = Guid.NewGuid();
        var mesh = Mesh();

        var workspace = Workspace.CreateEmpty().AddMesh(mesh, Record(id)).Value;

        workspace.GetRecord(id).Value.BaseMesh.Should().BeSameAs(mesh);
    }

    [Fact]
    public void AddMesh_RejectsAnEmptyIdentity()
    {
        var result = Workspace.CreateEmpty().AddMesh(Mesh(), Record(Guid.Empty));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkspaceErrors.InvalidId);
    }

    [Fact]
    public void AddMesh_RejectsADuplicateIdentity()
    {
        var id = Guid.NewGuid();
        var workspace = Workspace.CreateEmpty().AddMesh(Mesh(), Record(id)).Value;

        var result = workspace.AddMesh(Mesh(), Record(id));

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void GetMesh_ReturnsStoredMesh()
    {
        var id = Guid.NewGuid();
        var workspace = Workspace.CreateEmpty().AddMesh(Mesh(), Record(id)).Value;

        workspace.GetMesh(id).IsSuccess.Should().BeTrue();
        workspace.GetRecord(id).Value.Id.Should().Be(id);
    }

    [Fact]
    public void UpdateMesh_ReplacesGeometryWithoutDisturbingIdentity()
    {
        // The guarantee the whole split exists for: geometry from an operation that knows nothing
        // about this workspace - a boolean result, say - can fill the entry without the entry
        // losing track of who it is.
        var id = Guid.NewGuid();
        var workspace = Workspace.CreateEmpty().AddMesh(Mesh("Original"), Record(id, "Original")).Value;

        var anonymous = ImmutableMesh.Empty.WithMetadata(new MeshMetadata("a subtract b", "GeometryEngine.Booleans"));
        var result = workspace.UpdateMesh(id, anonymous);

        result.IsSuccess.Should().BeTrue();
        result.Value.MeshCount.Should().Be(1);
        result.Value.GetRecord(id).Value.Id.Should().Be(id);
        result.Value.GetRecord(id).Value.Name.Should().Be("Original");
    }

    [Fact]
    public void UpdateMesh_ReplacesTheRecordWhenOneIsGiven()
    {
        var id = Guid.NewGuid();
        var workspace = Workspace.CreateEmpty().AddMesh(Mesh(), Record(id, "Original")).Value;

        var result = workspace.UpdateMesh(id, Mesh(), Record(id, "Original").WithName("Updated"));

        result.IsSuccess.Should().BeTrue();
        result.Value.GetRecord(id).Value.Name.Should().Be("Updated");
    }

    [Fact]
    public void UpdateMesh_RefusesARecordForADifferentEntry()
    {
        var id = Guid.NewGuid();
        var workspace = Workspace.CreateEmpty().AddMesh(Mesh(), Record(id)).Value;

        var result = workspace.UpdateMesh(id, Mesh(), Record(Guid.NewGuid()));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkspaceErrors.InvalidId);
    }

    [Fact]
    public void UpdateMesh_FailsForAnUnknownEntry()
    {
        var result = Workspace.CreateEmpty().UpdateMesh(Guid.NewGuid(), Mesh());

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void UpdateRecord_ChangesTheRecordAndLeavesTheGeometryAlone()
    {
        var id = Guid.NewGuid();
        var mesh = Mesh();
        var workspace = Workspace.CreateEmpty().AddMesh(mesh, Record(id, "Original")).Value;

        var result = workspace.UpdateRecord(Record(id, "Renamed"));

        result.IsSuccess.Should().BeTrue();
        result.Value.GetRecord(id).Value.Name.Should().Be("Renamed");
        result.Value.GetMesh(id).Value.Should().BeSameAs(mesh);
    }

    [Fact]
    public void RemoveMesh_RemovesMeshAndClearsActiveIfMatches()
    {
        var id = Guid.NewGuid();
        var workspace = Workspace.CreateEmpty().AddMesh(Mesh(), Record(id), setActive: true).Value;

        var result = workspace.RemoveMesh(id);

        result.IsSuccess.Should().BeTrue();
        result.Value.MeshCount.Should().Be(0);
        result.Value.ActiveMeshId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void SetActiveMesh_UpdatesActiveMeshId()
    {
        var id = Guid.NewGuid();
        var workspace = Workspace.CreateEmpty().AddMesh(Mesh(), Record(id), setActive: false).Value;

        var result = workspace.SetActiveMesh(id);

        result.IsSuccess.Should().BeTrue();
        result.Value.ActiveMeshId.Should().Be(id);
        result.Value.GetActiveRecord().Value.Id.Should().Be(id);

        var cleared = result.Value.SetActiveMesh(null);
        cleared.IsSuccess.Should().BeTrue();
        cleared.Value.ActiveMeshId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void GetActiveRecord_FailsWhenNothingIsSelected()
    {
        var result = Workspace.CreateEmpty().GetActiveRecord();

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WorkspaceErrors.NoActiveMesh);
    }
}
