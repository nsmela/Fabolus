using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Core.Features.Transforms;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;
using System.Numerics;

namespace Fabolus.Tests.Features;

[Collection("GeometryEngine collection")]
public class TransformMeshTests
{
    private readonly GeometryEngineFixture _fixture;
    private readonly TransformMesh _transformFeature;

    public TransformMeshTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _transformFeature = new TransformMesh(_fixture.Engine);
    }

    [Fact]
    public void Translate_ValidMesh_ForksAndTranslates()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.LoadStl("sphere.stl");
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        var result = _transformFeature.Translate(workspace, baseId, 10, 20, 30);

        result.IsSuccess.Should().BeTrue();
        var updatedWorkspace = result.Value;

        // Base mesh + forked mesh
        updatedWorkspace.Meshes.Count.Should().Be(2);
        updatedWorkspace.ActiveMeshId.Should().NotBe(baseId);

        var forkedMesh = updatedWorkspace.GetActiveMesh().Value;
        forkedMesh.Metadata.DerivedFrom.HasValue.Should().BeTrue();
        forkedMesh.Metadata.DerivedFrom.Value.Should().Be(baseId);
        var translate = forkedMesh.Metadata.GetProperty(TransformKeys.Translation).Value;
        translate.Should().NotBeNull();

        var stats = _fixture.Engine.Evaluators.GetStatistics(forkedMesh).Value;
        var originalStats = _fixture.Engine.Evaluators.GetStatistics(mesh).Value;

        (stats.MinX - originalStats.MinX).Should().BeApproximately(10, 0.01);
        (stats.MinY - originalStats.MinY).Should().BeApproximately(20, 0.01);
        (stats.MinZ - originalStats.MinZ).Should().BeApproximately(30, 0.01);
    }

    [Fact]
    public void Rotate_ValidMesh_ForksAndRotates()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.LoadStl("sphere.stl");
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        // Rotate 90 degrees around Z axis
        float angleRadians = (float)(System.Math.PI / 2.0f);
        var result = _transformFeature.Rotate(workspace, baseId, angleRadians, Vector3.UnitZ);

        result.IsSuccess.Should().BeTrue();
        var forkedMesh = result.Value.GetActiveMesh().Value;

        var rotation = forkedMesh.Metadata.GetProperty(TransformKeys.Rotation).Value;
        rotation.Should().NotBeNull();
    }

    [Fact]
    public void ClearTransforms_OnDerivedMesh_RestoresBaseMesh()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.LoadStl("sphere.stl");
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        workspace = _transformFeature.Translate(workspace, baseId, 10, 0, 0).Value;
        var forkedId = workspace.ActiveMeshId;

        // Ensure we are working with the fork
        workspace.Meshes.Count.Should().Be(2);

        var result = _transformFeature.ClearRotation(workspace, forkedId);

        result.IsSuccess.Should().BeTrue();
        var restoredWorkspace = result.Value;

        restoredWorkspace.Meshes.Count.Should().Be(1);
        restoredWorkspace.ActiveMeshId.Should().Be(baseId);
    }
}
