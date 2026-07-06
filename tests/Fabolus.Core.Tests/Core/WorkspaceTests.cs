using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using FluentAssertions;
using System;
using Xunit;

namespace Fabolus.Tests.Core;

public class WorkspaceTests
{
    private class MockMesh : IMesh
    {
        public MeshMetadata Metadata { get; }
        public int VertexCount => 0;
        public int TriangleCount => 0;
        public bool IsEmpty => true;
        public System.Numerics.Vector3[] Vertices => Array.Empty<System.Numerics.Vector3>();
        public int[] Triangles => Array.Empty<int>();

        public MockMesh(Guid id)
        {
            Metadata = new MeshMetadata().WithId(id).WithName("Mock");
        }

        public MockMesh(MeshMetadata metadata)
        {
            Metadata = metadata;
        }

        public IMesh Clone() => new MockMesh(Metadata);
        public IMesh WithMetadata(MeshMetadata metadata) => new MockMesh(metadata);

        public void Dispose() { }
    }

    [Fact]
    public void CreateEmpty_ReturnsEmptyWorkspace()
    {
        var workspace = Workspace.CreateEmpty();

        workspace.MeshCount.Should().Be(0);
        workspace.ActiveMeshId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void AddMesh_AddsMeshToWorkspace()
    {
        var workspace = Workspace.CreateEmpty();
        var id = Guid.NewGuid();
        var mesh = new MockMesh(id);

        var result = workspace.AddMesh(mesh, setActive: false);

        result.IsSuccess.Should().BeTrue();
        var newWorkspace = result.Value;
        newWorkspace.MeshCount.Should().Be(1);
        newWorkspace.ContainsMesh(id).Should().BeTrue();
    }

    [Fact]
    public void AddMesh_WithSetActiveTrue_MakesMeshActive()
    {
        var workspace = Workspace.CreateEmpty();
        var id = Guid.NewGuid();
        var mesh = new MockMesh(id);

        var result = workspace.AddMesh(mesh, setActive: true);

        result.IsSuccess.Should().BeTrue();
        // This is expected to fail due to a known bug in Workspace.cs
        result.Value.ActiveMeshId.Should().Be(id);
    }

    [Fact]
    public void GetMesh_ReturnsStoredMesh()
    {
        var workspace = Workspace.CreateEmpty();
        var id = Guid.NewGuid();
        var mesh = new MockMesh(id);
        workspace = workspace.AddMesh(mesh).Value;

        var result = workspace.GetMesh(id);

        result.IsSuccess.Should().BeTrue();
        // Not BeSameAs(mesh): GetMesh returns an owned copy the caller must dispose.
        result.Value.Metadata.Id.Should().Be(id);
        result.Value.Metadata.HasBaseMesh.Should().BeTrue();
    }

    [Fact]
    public void UpdateMesh_ReplacesMeshById()
    {
        var workspace = Workspace.CreateEmpty();
        var id = Guid.NewGuid();
        var originalMesh = new MockMesh(id);
        workspace = workspace.AddMesh(originalMesh).Value;

        var updatedMesh = new MockMesh(originalMesh.Metadata.WithName("Updated"));
        var result = workspace.UpdateMesh(updatedMesh);

        result.IsSuccess.Should().BeTrue();
        var newWorkspace = result.Value;
        newWorkspace.MeshCount.Should().Be(1);
        newWorkspace.GetMesh(id).Value.Metadata.Name.Should().Be("Updated");
    }

    [Fact]
    public void RemoveMesh_RemovesMeshAndClearsActiveIfMatches()
    {
        var workspace = Workspace.CreateEmpty();
        var id = Guid.NewGuid();
        var mesh = new MockMesh(id);
        workspace = workspace.AddMesh(mesh, setActive: true).Value;

        var result = workspace.RemoveMesh(id);

        result.IsSuccess.Should().BeTrue();
        var newWorkspace = result.Value;
        newWorkspace.MeshCount.Should().Be(0);
        newWorkspace.ActiveMeshId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void SetActiveMesh_UpdatesActiveMeshId()
    {
        var workspace = Workspace.CreateEmpty();
        var id = Guid.NewGuid();
        var mesh = new MockMesh(id);
        workspace = workspace.AddMesh(mesh, setActive: false).Value;

        var result = workspace.SetActiveMesh(id);

        result.IsSuccess.Should().BeTrue();
        result.Value.ActiveMeshId.Should().Be(id);
        // Not BeSameAs(mesh): GetActiveMesh returns an owned copy the caller must dispose.
        result.Value.GetActiveMesh().Value.Metadata.Id.Should().Be(id);

        var clearedResult = result.Value.SetActiveMesh(null);
        clearedResult.IsSuccess.Should().BeTrue();
        clearedResult.Value.ActiveMeshId.Should().Be(Guid.Empty);
    }

}
