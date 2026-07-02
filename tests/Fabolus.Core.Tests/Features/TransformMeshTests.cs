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
    public void Translate_ValidMesh_TranslatesInPlace()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.LoadStl("sphere.stl");
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        // Capture stats before Translate - Execute updates this mesh's Workspace entry in
        // place, which disposes the original native mesh.
        var originalStats = _fixture.Engine.Evaluators.GetStatistics(mesh).Value;

        var result = _transformFeature.Translate(workspace, baseId, 10, 20, 30);

        result.IsSuccess.Should().BeTrue();
        var updatedWorkspace = result.Value;

        // No fork - still just one mesh, same id, only its geometry changed.
        updatedWorkspace.Meshes.Count.Should().Be(1);
        updatedWorkspace.ActiveMeshId.Should().Be(baseId);

        var translatedMesh = updatedWorkspace.GetActiveMesh().Value;
        translatedMesh.Metadata.Id.Should().Be(baseId);
        var translate = translatedMesh.Metadata.Translation().Value;
        translate.Should().NotBeNull();

        var stats = _fixture.Engine.Evaluators.GetStatistics(translatedMesh).Value;

        (stats.MinX - originalStats.MinX).Should().BeApproximately(10, 0.01);
        (stats.MinY - originalStats.MinY).Should().BeApproximately(20, 0.01);
        (stats.MinZ - originalStats.MinZ).Should().BeApproximately(30, 0.01);
    }

    [Fact]
    public void Rotate_ValidMesh_RotatesInPlace()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.LoadStl("sphere.stl");
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        // Rotate 90 degrees around Z axis
        float angleRadians = (float)(System.Math.PI / 2.0f);
        var result = _transformFeature.Rotate(workspace, baseId, angleRadians, Vector3.UnitZ);

        result.IsSuccess.Should().BeTrue();
        var rotatedMesh = result.Value.GetActiveMesh().Value;

        var rotation = rotatedMesh.Metadata.Rotation().Value;
        rotation.Should().NotBeNull();
    }

    [Fact]
    public void Rotate_Twice_PropagatesTransitiveRootBaseMesh_NotImmediateParent()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.LoadStl("sphere.stl");
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        float angleRadians = (float)(System.Math.PI / 4.0f);
        workspace = _transformFeature.Rotate(workspace, baseId, angleRadians, Vector3.UnitZ).Value;
        var onceRotated = workspace.GetActiveMesh().Value;

        onceRotated.Metadata.BaseMesh.HasValue.Should().BeTrue();
        onceRotated.Metadata.BaseMesh.Value.Metadata.Id.Should().Be(baseId);

        // Rotate again on the same (in-place) mesh - BaseMesh must still point at the true
        // original, not the once-rotated intermediate state (the bug this fixes).
        workspace = _transformFeature.Rotate(workspace, onceRotated.Metadata.Id, angleRadians, Vector3.UnitZ).Value;
        var twiceRotated = workspace.GetActiveMesh().Value;

        twiceRotated.Metadata.BaseMesh.HasValue.Should().BeTrue();
        twiceRotated.Metadata.BaseMesh.Value.Metadata.Id.Should().Be(baseId);
    }

    [Fact]
    public void ClearRotation_RestoresPreRotationGeometry()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.LoadStl("sphere.stl");
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        workspace = _transformFeature.Rotate(workspace, baseId, (float)(System.Math.PI / 4), Vector3.UnitZ).Value;

        var result = _transformFeature.ClearRotation(workspace, baseId);

        result.IsSuccess.Should().BeTrue();
        var restoredWorkspace = result.Value;

        // No fork - stays on the same mesh entry throughout.
        restoredWorkspace.Meshes.Count.Should().Be(1);
        restoredWorkspace.ActiveMeshId.Should().Be(baseId);

        var restoredMesh = restoredWorkspace.GetActiveMesh().Value;
        restoredMesh.Metadata.Rotation().HasNoValue.Should().BeTrue();
    }
}
