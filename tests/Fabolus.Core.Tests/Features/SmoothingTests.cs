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

    /// <summary>
    /// Smoothing has to hand back the same object, a little rounder - not a cloud of fragments.
    /// </summary>
    /// <remarks>
    /// Every other test here asserts on metadata, or on a sphere. A sphere is convex and smooth,
    /// which is exactly the shape that hides a broken distance field: the Manifold engine's
    /// offset once took the sign of its signed distance from the nearest triangle's own face
    /// normal, which is only right when the closest point is in that triangle's interior. On a
    /// sphere it always is. On a real bolus, most of the surrounding volume is nearest to an edge
    /// or a corner instead, the sign flipped essentially at random, and ear_bolus came back as 25
    /// disconnected pieces inside a bounding box half again too big - while the whole suite
    /// stayed green. Hence a real bolus, and assertions about the shape rather than the plumbing.
    /// </remarks>
    [Theory]
    [InlineData("ear_bolus.stl")]
    [InlineData("eye_bolus.stl")]
    public void SmoothMesh_OnBolus_StaysOnePieceAtItsOriginalSize(string filename)
    {
        var mesh = _fixture.LoadStl(filename);
        var before = _fixture.Engine.Evaluators.GetStatistics(mesh).Value;

        var workspace = Workspace.CreateEmpty();
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(mesh.Metadata.Id).Value;

        var result = _smoothingFeature.Execute(workspace, new SmoothSettings());
        result.IsSuccess.Should().BeTrue();

        var smoothed = result.Value.GetActiveMesh().Value;
        var after = _fixture.Engine.Evaluators.GetStatistics(smoothed).Value;

        // SeparateComponents drops debris below 0.1 cubic mm, so anything it still reports is a
        // piece a user would see floating beside their model.
        var parts = _fixture.Engine.Evaluators.SeparateComponents(smoothed);
        parts.IsSuccess.Should().BeTrue();
        parts.Value.Should().HaveCount(1, "smoothing a single solid must not break it apart");

        // Smoothing erodes and re-dilates, so the result moves a little either way; what it must
        // not do is balloon. A shredded field shows up here as a box half again too large.
        after.MaxX.Should().BeApproximately(before.MaxX, 2.0);
        after.MinX.Should().BeApproximately(before.MinX, 2.0);
        after.MaxY.Should().BeApproximately(before.MaxY, 2.0);
        after.MinY.Should().BeApproximately(before.MinY, 2.0);
        after.MaxZ.Should().BeApproximately(before.MaxZ, 2.0);
        after.MinZ.Should().BeApproximately(before.MinZ, 2.0);

        // Volume survives within a fifth. Fragments lose most of it; a runaway offset gains it.
        after.Volume.Should().BeInRange(before.Volume * 0.8, before.Volume * 1.2);
    }

    /// <summary>
    /// The distance field the offset is built on has to agree with the mesh about which side of
    /// it a point is on, including in a concave crease.
    /// </summary>
    /// <remarks>
    /// Convex shapes cannot detect this: outside a convex solid every face adjacent to the
    /// closest edge agrees on the sign, so a cube and a sphere both come out perfect whether the
    /// sign test is right or wrong. It takes a concave crease, where a point can be inside the
    /// solid but on the outer side of one of the faces meeting there. Two overlapping spheres
    /// give one cheaply, and the offset splits the result in two when the sign is taken per-face.
    ///
    /// This goes through the modifier rather than the smoothing feature so it measures the field
    /// alone - the feature also decimates, and its triangle budget is a ratio of the input count,
    /// which on a coarse primitive is a far harsher reduction than any real mesh ever sees.
    /// </remarks>
    [Fact]
    public void OffsetDouble_AcrossAConcaveCrease_KeepsTheSolidWhole()
    {
        var left = _fixture.Engine.Generators.GenerateSphere(new Vector3(-7, 0, 0), 12, 24).Value;
        var right = _fixture.Engine.Generators.GenerateSphere(new Vector3(7, 0, 0), 12, 24).Value;

        var peanut = _fixture.Engine.Booleans.Union(left, right);
        peanut.IsSuccess.Should().BeTrue();

        var before = _fixture.Engine.Evaluators.GetStatistics(peanut.Value).Value;

        var result = _fixture.Engine.Modifiers.OffsetDouble(peanut.Value, 1.0f, iterations: 1, cellSize: 1.0f);
        result.IsSuccess.Should().BeTrue();

        var parts = _fixture.Engine.Evaluators.SeparateComponents(result.Value);
        parts.IsSuccess.Should().BeTrue();
        parts.Value.Should().HaveCount(1, "an erode-dilate of one solid must not cut it at the crease");

        var after = _fixture.Engine.Evaluators.GetStatistics(result.Value).Value;
        after.Volume.Should().BeInRange(before.Volume * 0.9, before.Volume * 1.1);
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

        // In-place - one smoothed mesh.
        updatedWorkspace.MeshCount.Should().Be(1);
        updatedWorkspace.ActiveMeshId.Should().Be(baseId);

        var smoothedMesh = updatedWorkspace.GetActiveMesh().Value;
        smoothedMesh.Metadata.Id.Should().Be(baseId);
        smoothedMesh.Metadata.DerivedFrom.HasValue.Should().BeFalse();
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

        // Updates the smoothed mesh in-place.
        finalWorkspace.MeshCount.Should().Be(1);
        finalWorkspace.ActiveMeshId.Should().Be(baseId);

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

        // Smoothing updates in place. Reverting just removes smoothing.
        resetWorkspace.MeshCount.Should().Be(1);
        resetWorkspace.ActiveMeshId.Should().Be(baseId);

        var resetMesh = resetWorkspace.GetActiveMesh().Value;
        resetMesh.Metadata.GetSmoothing().HasNoValue.Should().BeTrue();
        resetMesh.Metadata.Commands.OfType<RotateCommand>().Should().HaveCount(1);
    }
}
