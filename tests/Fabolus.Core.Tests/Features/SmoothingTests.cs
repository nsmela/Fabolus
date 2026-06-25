using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Core.Features.Smoothing;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Tests.Features;

[Collection("GeometryEngine collection")]
public class SmoothingTests
{
    private readonly GeometryEngineFixture _fixture;
    private readonly SmoothMesh _smoothingFeature;

    public SmoothingTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _smoothingFeature = new SmoothMesh(_fixture.Engine);
    }

    [Fact]
    public void SmoothMesh_ValidMesh_ForksAndSmooths()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.LoadStl("sphere.stl");
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        var result = _smoothingFeature.Execute(workspace, new SmoothSettings());

        result.IsSuccess.Should().BeTrue();
        var updatedWorkspace = result.Value;

        updatedWorkspace.Meshes.Count.Should().Be(2);
        var smoothedMesh = updatedWorkspace.GetActiveMesh().Value;

        smoothedMesh.Metadata.DerivedFrom.Value.Should().Be(baseId);
        smoothedMesh.Metadata.GetSmoothing().HasValue.Should().BeTrue();
    }

    [Fact]
    public void SmoothMesh_WithInflation_AppliesInflation()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.LoadStl("sphere.stl");
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        var result = _smoothingFeature.Execute(workspace, new SmoothSettings());

        result.IsSuccess.Should().BeTrue();
        var smoothedMesh = result.Value.GetActiveMesh().Value;
        
        var originalStats = _fixture.Engine.Evaluators.GetStatistics(mesh).Value;
        var smoothedStats = _fixture.Engine.Evaluators.GetStatistics(smoothedMesh).Value;

        // Bounding box should have grown due to inflation
        (smoothedStats.MaxX - smoothedStats.MinX).Should().BeGreaterThan(originalStats.MaxX - originalStats.MinX + 1.0);
    }

    [Fact]
    public void SmoothMesh_AlreadyDerived_UpdatesExistingDerivedMesh()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.LoadStl("sphere.stl");
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        // Smooth once
        workspace = _smoothingFeature.Execute(workspace, new SmoothSettings()).Value;
        var firstSmoothedId = workspace.ActiveMeshId;

        // Smooth again
        var result = _smoothingFeature.Execute(workspace, new SmoothSettings());

        result.IsSuccess.Should().BeTrue();
        var finalWorkspace = result.Value;

        // Should still only have 2 meshes (base + updated derived)
        finalWorkspace.Meshes.Count.Should().Be(2);
        finalWorkspace.ActiveMeshId.Should().Be(firstSmoothedId);
    }
}
