using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Core.Features.MeshIO;
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
        updatedWorkspace.MeshCount.Should().Be(1);
        updatedWorkspace.ActiveMeshId.Should().Be(baseId);

        using var translatedMesh = updatedWorkspace.GetActiveMesh().Value;
        translatedMesh.Metadata.Id.Should().Be(baseId);
        var translate = translatedMesh.Metadata.Translation().Value;
        translate.Should().NotBeNull();

        var stats = _fixture.Engine.Evaluators.GetStatistics(translatedMesh).Value;

        (stats.MinX - originalStats.MinX).Should().BeApproximately(10, 0.01);
        (stats.MinY - originalStats.MinY).Should().BeApproximately(20, 0.01);
        (stats.MinZ - originalStats.MinZ).Should().BeApproximately(30, 0.01);

        // The metadata's cached Stats must track the move too - UI elements are sized from it.
        translatedMesh.Metadata.MeshStats().Value.MinX.Should().BeApproximately(stats.MinX, 0.01);
    }

    [Fact]
    public void Rotate_RefreshesBoundingBoxStats()
    {
        var workspace = Workspace.CreateEmpty();
        var cube = _fixture.UnitCube();
        var id = cube.Metadata.Id;
        workspace = workspace.AddMesh(cube).Value.SetActiveMesh(id).Value;

        // 45 degrees about Z: the unit cube's XY footprint grows from 1.0 to sqrt(2). The
        // metadata's cached Stats must reflect that - the rotation axis gizmo is sized from
        // it, and stale import-time bounds left it too small after committed rotations.
        workspace = _transformFeature.Rotate(workspace, id, (float)(System.Math.PI / 4), Vector3.UnitZ).Value;

        var rotatedMesh = workspace.GetActiveMesh().Value;
        var stats = rotatedMesh.Metadata.MeshStats().Value;

        (stats.MaxX - stats.MinX).Should().BeApproximately(System.Math.Sqrt(2), 0.01);
        (stats.MaxY - stats.MinY).Should().BeApproximately(System.Math.Sqrt(2), 0.01);
        (stats.MaxZ - stats.MinZ).Should().BeApproximately(1.0, 0.01);
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
        var onceRotated = workspace.GetActiveMeshMetadata().Value;

        onceRotated.HasBaseMesh.Should().BeTrue();
        onceRotated.BaseMeshMetadata.Value.Id.Should().Be(baseId);

        // Rotate again on the same (in-place) mesh - BaseMesh must still point at the true
        // original, not the once-rotated intermediate state (the bug this fixes).
        workspace = _transformFeature.Rotate(workspace, onceRotated.Id, angleRadians, Vector3.UnitZ).Value;
        var twiceRotated = workspace.GetActiveMeshMetadata().Value;

        twiceRotated.HasBaseMesh.Should().BeTrue();
        twiceRotated.BaseMeshMetadata.Value.Id.Should().Be(baseId);
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
        restoredWorkspace.MeshCount.Should().Be(1);
        restoredWorkspace.ActiveMeshId.Should().Be(baseId);

        restoredWorkspace.GetActiveMeshMetadata().Value.Rotation().HasNoValue.Should().BeTrue();
    }
}
