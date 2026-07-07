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

        // Fork - one original mesh, one smoothed mesh.
        updatedWorkspace.MeshCount.Should().Be(2);
        updatedWorkspace.ActiveMeshId.Should().NotBe(baseId);

        var smoothedMesh = updatedWorkspace.GetActiveMesh().Value;
        smoothedMesh.Metadata.Id.Should().NotBe(baseId);
        smoothedMesh.Metadata.DerivedFrom.Value.Should().Be(baseId);
        smoothedMesh.Metadata.HasBaseMesh.Should().BeTrue();
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

        var result = _smoothingFeature.Execute(workspace, new SmoothSettings(Inflation: 2.0f));

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
        var firstBaseMesh = workspace.GetActiveMeshMetadata().Value.GetBaseMesh().Value;

        // Smooth again with different settings
        var result = _smoothingFeature.Execute(workspace, new SmoothSettings(Iterations: 2));

        result.IsSuccess.Should().BeTrue();
        var finalWorkspace = result.Value;

        // Fork happens on the first apply. The second apply updates the smoothed mesh in-place.
        finalWorkspace.MeshCount.Should().Be(2);
        finalWorkspace.ActiveMeshId.Should().NotBe(baseId);

        // Re-derives from the same pristine BaseMesh both times (doesn't stack smoothing on
        // top of already-smoothed geometry, and doesn't re-clone on the second Apply).
        // GetBaseMesh sees the stored instance itself, so BeSameAs holds.
        finalWorkspace.GetActiveMeshMetadata().Value.GetBaseMesh().Value.Should().BeSameAs(firstBaseMesh);
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
    public void ComputeUnsmoothedMesh_AfterTransform_StaysAlignedWithCurrentMesh()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.LoadStl("sphere.stl");
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        // Smooth, then translate - the comparison reference shown in the Smoothing view must
        // follow the mesh to its new position, not sit back at BaseMesh's original spot.
        workspace = _smoothingFeature.Execute(workspace, new SmoothSettings()).Value;
        var smoothedId = workspace.ActiveMeshId;
        var transformFeature = new TransformMesh(_fixture.Engine);
        workspace = transformFeature.Translate(workspace, smoothedId, 50, 0, 0).Value;

        var currentMesh = workspace.GetActiveMesh().Value;

        var result = _resetFeature.ComputeUnsmoothedMesh(currentMesh);

        result.IsSuccess.Should().BeTrue();
        var unsmoothed = result.Value;

        var baseCopy = currentMesh.Metadata.GetBaseMesh().Value;
        var currentStats = _fixture.Engine.Evaluators.GetStatistics(currentMesh).Value;
        var unsmoothedStats = _fixture.Engine.Evaluators.GetStatistics(unsmoothed).Value;
        var baseStats = _fixture.Engine.Evaluators.GetStatistics(baseCopy).Value;

        // Aligned with the (translated, smoothed) current mesh...
        var currentCentreX = (currentStats.MinX + currentStats.MaxX) / 2;
        var unsmoothedCentreX = (unsmoothedStats.MinX + unsmoothedStats.MaxX) / 2;
        unsmoothedCentreX.Should().BeApproximately(currentCentreX, 2.0);

        // ...and NOT with the pristine BaseMesh, which never moves.
        var baseCentreX = (baseStats.MinX + baseStats.MaxX) / 2;
        (unsmoothedCentreX - baseCentreX).Should().BeApproximately(50, 2.0);
    }

    [Fact]
    public void ComputeUnsmoothedMesh_NoOtherCommands_ReturnsSameInstance()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.LoadStl("sphere.stl");
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(mesh.Metadata.Id).Value;

        workspace = _smoothingFeature.Execute(workspace, new SmoothSettings()).Value;
        var currentMesh = workspace.GetActiveMesh().Value;

        var result = _resetFeature.ComputeUnsmoothedMesh(currentMesh);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(currentMesh.Metadata.GetBaseMesh().Value);

        // The stored BaseMesh must still be usable after disposing the copy: resetting
        // smoothing replays from it.
        var reset = _resetFeature.Execute(workspace);
        reset.IsSuccess.Should().BeTrue();
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

        // Fork occurs upon smoothing. Reverting deletes the smoothed mesh and activates parent.
        resetWorkspace.MeshCount.Should().Be(1);
        resetWorkspace.ActiveMeshId.Should().Be(baseId);

        var resetMesh = resetWorkspace.GetActiveMesh().Value;
        resetMesh.Metadata.GetSmoothing().HasNoValue.Should().BeTrue();
        resetMesh.Metadata.Commands.OfType<RotateCommand>().Should().HaveCount(1);
    }
}
