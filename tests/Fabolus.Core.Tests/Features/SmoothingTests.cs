using System.Linq;
using System.Numerics;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Core.Features.Smoothing;
using Fabolus.Core.Features.Transforms;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Tests.Features;

[Collection("GeometryEngine collection")]
public class SmoothingTests
{
    private readonly GeometryEngineFixture _fixture;
    private readonly SmoothMesh _smoothingFeature;
    private readonly ResetSmoothing _resetFeature;

    public SmoothingTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _smoothingFeature = new SmoothMesh(_fixture.Engine);
        _resetFeature = new ResetSmoothing(_fixture.Engine);
    }

    [Fact]
    public void SmoothMesh_ValidMesh_SmoothsInPlace()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.LoadStl("sphere.stl");
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        var result = _smoothingFeature.Execute(workspace, new SmoothSettings());

        result.IsSuccess.Should().BeTrue();
        var updatedWorkspace = result.Value;

        // No fork - still just one mesh, same id, only its geometry changed.
        updatedWorkspace.Meshes.Count.Should().Be(1);
        updatedWorkspace.ActiveMeshId.Should().Be(baseId);

        var smoothedMesh = updatedWorkspace.GetActiveMesh().Value;
        smoothedMesh.Metadata.Id.Should().Be(baseId);
        smoothedMesh.Metadata.BaseMesh.HasValue.Should().BeTrue();
        smoothedMesh.Metadata.GetSmoothing().HasValue.Should().BeTrue();
    }

    [Fact]
    public void SmoothMesh_WithInflation_AppliesInflation()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.LoadStl("sphere.stl");
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        // Capture stats before smoothing - Execute updates this mesh's Workspace entry in
        // place, which disposes the original native mesh.
        var originalStats = _fixture.Engine.Evaluators.GetStatistics(mesh).Value;

        var result = _smoothingFeature.Execute(workspace, new SmoothSettings());

        result.IsSuccess.Should().BeTrue();
        var smoothedMesh = result.Value.GetActiveMesh().Value;
        var smoothedStats = _fixture.Engine.Evaluators.GetStatistics(smoothedMesh).Value;

        // Bounding box should have grown due to inflation
        (smoothedStats.MaxX - smoothedStats.MinX).Should().BeGreaterThan(originalStats.MaxX - originalStats.MinX + 1.0);
    }

    [Fact]
    public void SmoothMesh_AppliedTwice_StaysInPlaceAndDoesNotStack()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.LoadStl("sphere.stl");
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        // Smooth once
        workspace = _smoothingFeature.Execute(workspace, new SmoothSettings()).Value;
        var firstBaseMesh = workspace.GetActiveMesh().Value.Metadata.BaseMesh.Value;

        // Smooth again with different settings
        var result = _smoothingFeature.Execute(workspace, new SmoothSettings(Iterations: 2));

        result.IsSuccess.Should().BeTrue();
        var finalWorkspace = result.Value;

        // Still only one mesh, same id - never forks.
        finalWorkspace.Meshes.Count.Should().Be(1);
        finalWorkspace.ActiveMeshId.Should().Be(baseId);

        // Re-derives from the same pristine BaseMesh both times (doesn't stack smoothing on
        // top of already-smoothed geometry, and doesn't re-clone on the second Apply).
        finalWorkspace.GetActiveMesh().Value.Metadata.BaseMesh.Value.Should().BeSameAs(firstBaseMesh);
    }

    [Fact]
    public void Smooth_AfterTranslate_PreservesTranslationInFinalGeometry()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.LoadStl("sphere.stl");
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        var originalStats = _fixture.Engine.Evaluators.GetStatistics(mesh).Value;

        var transformFeature = new TransformMesh(_fixture.Engine);
        workspace = transformFeature.Translate(workspace, baseId, 50, 0, 0).Value;

        var result = _smoothingFeature.Execute(workspace, new SmoothSettings());

        result.IsSuccess.Should().BeTrue();
        var smoothedMesh = result.Value.GetActiveMesh().Value;
        var smoothedStats = _fixture.Engine.Evaluators.GetStatistics(smoothedMesh).Value;

        // Smoothing must replay on top of the translation (re-deriving straight from the
        // untranslated BaseMesh would silently discard it).
        (smoothedStats.MinX - originalStats.MinX).Should().BeApproximately(50, 2.0);
    }

    [Fact]
    public void ResetSmoothing_RemovesSmoothingButKeepsOtherCommands()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.LoadStl("sphere.stl");
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        var transformFeature = new TransformMesh(_fixture.Engine);
        workspace = transformFeature.Rotate(workspace, baseId, (float)(System.Math.PI / 4), Vector3.UnitZ).Value;
        workspace = _smoothingFeature.Execute(workspace, new SmoothSettings()).Value;

        var result = _resetFeature.Execute(workspace);

        result.IsSuccess.Should().BeTrue();
        var resetWorkspace = result.Value;

        // Still no fork - reverting stays on the same mesh entry.
        resetWorkspace.Meshes.Count.Should().Be(1);
        resetWorkspace.ActiveMeshId.Should().Be(baseId);

        var resetMesh = resetWorkspace.GetActiveMesh().Value;
        resetMesh.Metadata.GetSmoothing().HasNoValue.Should().BeTrue();
        resetMesh.Metadata.Commands.OfType<RotateCommand>().Should().HaveCount(1);
    }
}
