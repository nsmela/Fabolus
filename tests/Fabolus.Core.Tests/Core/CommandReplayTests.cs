using System.Numerics;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.Smoothing;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Tests.Core;

[Collection("GeometryEngine collection")]
public class CommandReplayTests
{
    private readonly GeometryEngineFixture _fixture;

    public CommandReplayTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GetMeshAtStage_NoHigherPriorityCommands_ReturnsSameInstance()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(mesh.Metadata.Id).Value;

        var activeMesh = workspace.GetActiveMesh().Value;

        var result = CommandReplay.GetMeshAtStage(_fixture.Engine, activeMesh, CommandPriority.Transform);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(activeMesh);
    }

    [Fact]
    public void GetMeshAtStage_MouldOnlyCommands_ReturnsBaseMeshInstance()
    {
        // Regression: a mesh whose ONLY command is a mould (never rotated/smoothed) made
        // GetMeshAtStage(Transform) replay an empty command list, which used to hand back
        // the metadata-held BaseMesh instance itself. Callers disposing the result then
        // destroyed the base out from under the workspace, crashing the next replay
        // (e.g. Clear Mould). The result must now always be an owned copy.
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        var generateMould = new GenerateMould(_fixture.Engine);
        workspace = generateMould.Execute(workspace, baseId, new ContouredMouldDefinition(OffsetXY: 2.0)).Value;

        var activeMesh = workspace.GetActiveMesh().Value;
        
        var stageResult = CommandReplay.GetMeshAtStage(_fixture.Engine, activeMesh, CommandPriority.Transform);

        stageResult.IsSuccess.Should().BeTrue();
        stageResult.Value.Should().NotBeSameAs(activeMesh.Metadata.GetBaseMesh().Value);

        // The stored BaseMesh must still be alive: clearing the mould replays from it.
        var clearResult = new ClearMould(_fixture.Engine).Execute(workspace);
        clearResult.IsSuccess.Should().BeTrue();
        clearResult.Value.GetActiveMeshMetadata().Value.MouldDefinition().HasNoValue.Should().BeTrue();
    }

    [Fact]
    public void Apply_ConsumesBaseCopy_WorkspaceMeshSurvivesRepeatedReplays()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        var baseId = mesh.Metadata.Id;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(baseId).Value;

        var smoothFeature = new SmoothMesh(_fixture.Engine);

        // Each Execute replays from a fresh base copy that the replay consumes; running it
        // repeatedly must not degrade or destroy the stored BaseMesh.
        workspace = smoothFeature.Execute(workspace, new SmoothSettings()).Value;
        workspace = smoothFeature.Execute(workspace, new SmoothSettings { Iterations = 2 }).Value;

        var reset = new ResetSmoothing(_fixture.Engine).Execute(workspace);
        reset.IsSuccess.Should().BeTrue();
        reset.Value.GetActiveMeshMetadata().Value.GetSmoothing().HasNoValue.Should().BeTrue();
    }
}
