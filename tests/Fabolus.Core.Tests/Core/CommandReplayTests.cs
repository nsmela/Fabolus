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

    [Fact]
    public void Apply_SameHistoryTwice_HandsBackTheSameMesh()
    {
        // Smoothing is not repeatable: the engine's offset meshes in parallel and differs a little on
        // every call. Replayed afresh each time, the body the parting line was traced on moved from
        // run to run, and with it the rims, the line and whether the cut severed.
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        var commands = new IMeshCommand[] { new SmoothSettings() };

        var first = CommandReplay.Apply(_fixture.Engine, mesh, commands).Value;
        var second = CommandReplay.Apply(_fixture.Engine, mesh, commands.ToList()).Value;

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void GetMeshAtStage_AfterSmoothRotateAndMould_IsTheBodyTheMouldWasBuiltAround()
    {
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        var (workspace, id) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), mesh);

        workspace = new SmoothMesh(_fixture.Engine).Execute(workspace, new SmoothSettings()).Value;
        workspace = new Fabolus.Core.Features.Transforms.TransformMesh(_fixture.Engine)
            .Rotate(workspace, id, 0.3f, GeometryEngine.Core.Geometry.Primitives.Vec3.UnitX).Value;
        var body = workspace.GetMesh(id).Value;

        workspace = new GenerateMould(_fixture.Engine).Execute(workspace, id, new ContouredMouldDefinition(OffsetXY: 2.0)).Value;

        var recovered = CommandReplay.GetMeshAtStage(
            _fixture.Engine, workspace.GetMesh(id).Value, workspace.GetRecord(id).Value, CommandPriority.Transform);

        recovered.Value.Should().BeSameAs(body);
    }

    [Fact]
    public void Apply_HistoriesThatPart_EachKeepTheirOwnResult()
    {
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        var smooth = new SmoothSettings();
        IMeshCommand[] Gentle() => [smooth, new SmoothSettings(Intensity: 0.5f, Iterations: 2)];

        var gentle = CommandReplay.Apply(_fixture.Engine, mesh, Gentle()).Value;
        var harder = CommandReplay.Apply(_fixture.Engine, mesh, new IMeshCommand[] { smooth, new SmoothSettings(Intensity: 1.5f, Iterations: 2) }).Value;

        harder.Should().NotBeSameAs(gentle);
        CommandReplay.Apply(_fixture.Engine, mesh, Gentle()).Value.Should().BeSameAs(gentle);
    }
}
