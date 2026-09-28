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
        var mesh = _fixture.LoadStl("sphere.stl");
        var originalStats = _fixture.Engine.Evaluators.GetStatistics(mesh).Value;
        var (workspace, baseId) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), mesh);

        var result = _transformFeature.Translate(workspace, baseId, 10, 20, 30);

        result.IsSuccess.Should().BeTrue();
        var updatedWorkspace = result.Value;

        // No fork - still just one entry, same id, only its geometry changed.
        updatedWorkspace.MeshCount.Should().Be(1);
        updatedWorkspace.ActiveMeshId.Should().Be(baseId);

        var record = updatedWorkspace.GetActiveRecord().Value;
        record.Id.Should().Be(baseId);
        record.Translation().Should().NotBeNull();

        var translatedMesh = updatedWorkspace.GetActiveMesh().Value;
        var stats = _fixture.Engine.Evaluators.GetStatistics(translatedMesh).Value;

        (stats.BoundsMin.X - originalStats.BoundsMin.X).Should().BeApproximately(10, 0.01);
        (stats.BoundsMin.Y - originalStats.BoundsMin.Y).Should().BeApproximately(20, 0.01);
        (stats.BoundsMin.Z - originalStats.BoundsMin.Z).Should().BeApproximately(30, 0.01);

        // The cached Stats must track the move too - UI elements are sized from them.
        translatedMesh.Stats()!.BoundsMin.X.Should().BeApproximately(stats.BoundsMin.X, 0.01);
    }

    [Fact]
    public void Rotate_RefreshesBoundingBoxStats()
    {
        var cube = _fixture.UnitCube();
        var (workspace, id) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), cube);

        // 45 degrees about Z: the unit cube's XY footprint grows from 1.0 to sqrt(2). The cached
        // Stats must reflect that - the rotation axis gizmo is sized from them, and stale
        // import-time bounds left it too small after committed rotations.
        workspace = _transformFeature.Rotate(workspace, id, (float)(System.Math.PI / 4), Vector3.UnitZ).Value;

        var stats = workspace.GetActiveMesh().Value.Stats();
        stats.Should().NotBeNull();

        (stats!.BoundsMax.X - stats.BoundsMin.X).Should().BeApproximately(System.Math.Sqrt(2), 0.01);
        (stats.BoundsMax.Y - stats.BoundsMin.Y).Should().BeApproximately(System.Math.Sqrt(2), 0.01);
        (stats.BoundsMax.Z - stats.BoundsMin.Z).Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void Rotate_KeepsTheTopologyAuditRatherThanRecomputingIt()
    {
        // A rigid transform moves vertices without touching connectivity, so the audit taken
        // before the rotation still reads the same afterwards. This is the Transform carry rule
        // arriving through a real feature rather than in isolation.
        var cube = _fixture.UnitCube();
        var (workspace, id) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), cube);
        workspace = workspace.UpdateMesh(id, workspace.GetMesh(id).Value.WithMeasurements(_fixture.Engine)).Value;

        var before = workspace.GetMesh(id).Value.Topology();
        before.Should().NotBeNull();

        workspace = _transformFeature.Rotate(workspace, id, (float)(System.Math.PI / 4), Vector3.UnitZ).Value;

        workspace.GetActiveMesh().Value.Topology().Should().Be(before);
    }

    [Fact]
    public void Rotate_ValidMesh_RotatesInPlace()
    {
        var mesh = _fixture.LoadStl("sphere.stl");
        var (workspace, baseId) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), mesh);

        // Rotate 90 degrees around Z axis
        float angleRadians = (float)(System.Math.PI / 2.0f);
        var result = _transformFeature.Rotate(workspace, baseId, angleRadians, Vector3.UnitZ);

        result.IsSuccess.Should().BeTrue();
        result.Value.GetActiveRecord().Value.Rotation().Should().NotBeNull();
    }

    [Fact]
    public void Rotate_Twice_ReplaysFromTheTrueOriginal_NotTheOnceRotatedIntermediate()
    {
        var mesh = _fixture.LoadStl("sphere.stl");
        var (workspace, baseId) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), mesh);

        var original = workspace.GetRecord(baseId).Value.BaseMesh;
        original.Should().NotBeNull();

        float angleRadians = (float)(System.Math.PI / 4.0f);
        workspace = _transformFeature.Rotate(workspace, baseId, angleRadians, Vector3.UnitZ).Value;
        workspace.GetActiveRecord().Value.BaseMesh.Should().BeSameAs(original);

        // Rotate again on the same (in-place) entry - BaseMesh must still be the true original,
        // not the once-rotated intermediate state (the bug this covers).
        workspace = _transformFeature.Rotate(workspace, baseId, angleRadians, Vector3.UnitZ).Value;
        workspace.GetActiveRecord().Value.BaseMesh.Should().BeSameAs(original);

        // And the two rotations compose into one net command rather than stacking.
        workspace.GetActiveRecord().Value.Commands.OfType<RotateCommand>().Should().ContainSingle();
    }

    [Fact]
    public void ClearRotation_RestoresPreRotationGeometry()
    {
        var mesh = _fixture.LoadStl("sphere.stl");
        var (workspace, baseId) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), mesh);

        workspace = _transformFeature.Rotate(workspace, baseId, (float)(System.Math.PI / 4), Vector3.UnitZ).Value;

        var result = _transformFeature.ClearRotation(workspace, baseId);

        result.IsSuccess.Should().BeTrue();
        var restoredWorkspace = result.Value;

        // No fork - stays on the same entry throughout.
        restoredWorkspace.MeshCount.Should().Be(1);
        restoredWorkspace.ActiveMeshId.Should().Be(baseId);

        restoredWorkspace.GetActiveRecord().Value.Rotation().Should().BeNull();
    }
}
