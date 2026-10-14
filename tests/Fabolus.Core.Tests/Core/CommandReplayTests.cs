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
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        var (workspace, id) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), mesh);

        var activeMesh = workspace.GetActiveMesh().Value;
        var record = workspace.GetRecord(id).Value;

        var result = CommandReplay.GetMeshAtStage(_fixture.Engine, activeMesh, record, CommandPriority.Transform);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(activeMesh);
    }

    [Fact]
    public void GetMeshAtStage_MouldOnlyCommands_RewindsToTheBaseAndLeavesItReusable()
    {
        // Regression: a mesh whose ONLY command is a mould (never rotated/smoothed) makes
        // GetMeshAtStage(Transform) replay an empty command list, so the answer is the record's
        // base mesh. That used to be a bug rather than an answer - a mesh owned native memory,
        // so handing out the stored instance let the caller dispose the workspace's own base and
        // crash the next replay. Meshes are immutable values now, so sharing the instance is the
        // right thing to do; what still has to hold is that the base stays usable afterwards.
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        var (workspace, baseId) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), mesh);

        var generateMould = new GenerateMould(_fixture.Engine);
        workspace = generateMould.Execute(workspace, baseId, new ContouredMouldDefinition(OffsetXY: 2.0)).Value;

        var activeMesh = workspace.GetActiveMesh().Value;
        var record = workspace.GetRecord(baseId).Value;

        var stageResult = CommandReplay.GetMeshAtStage(_fixture.Engine, activeMesh, record, CommandPriority.Transform);

        stageResult.IsSuccess.Should().BeTrue();

        // Rewound past the mould: the pre-mould geometry, not the mould shell.
        stageResult.Value.Should().NotBeSameAs(activeMesh);
        stageResult.Value.TriangleCount.Should().Be(record.BaseMesh!.TriangleCount);

        // The stored BaseMesh must still be usable: clearing the mould replays from it.
        var clearResult = new ClearMould(_fixture.Engine).Execute(workspace);
        clearResult.IsSuccess.Should().BeTrue();
        clearResult.Value.GetActiveRecord().Value.MouldDefinition().Should().BeNull();
    }

    [Fact]
    public void Apply_ConsumesBaseCopy_WorkspaceMeshSurvivesRepeatedReplays()
    {
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        var (workspace, _) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), mesh);

        var smoothFeature = new SmoothMesh(_fixture.Engine);

        // Each Execute replays from the stored base; running it repeatedly must not degrade or
        // destroy that base.
        workspace = smoothFeature.Execute(workspace, new SmoothSettings()).Value;
        workspace = smoothFeature.Execute(workspace, new SmoothSettings(Iterations: 2)).Value;

        var reset = new ResetSmoothing(_fixture.Engine).Execute(workspace);
        reset.IsSuccess.Should().BeTrue();
        reset.Value.GetActiveRecord().Value.Smoothing().Should().BeNull();
    }
}
