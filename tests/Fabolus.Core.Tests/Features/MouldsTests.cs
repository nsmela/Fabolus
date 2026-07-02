using System.Linq;
using System.Numerics;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.AirChannels;
using Fabolus.Core.Features.Transforms;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Tests.Features;

[Collection("GeometryEngine collection")]
public class MouldsTests
{
    private readonly GeometryEngineFixture _fixture;
    private readonly GenerateMould _generateMouldFeature;

    public MouldsTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _generateMouldFeature = new GenerateMould(_fixture.Engine);
    }

    [Fact]
    public void GenerateMould_PreservesCommandsFromSourceMesh()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(mesh.Metadata.Id).Value;

        var transformFeature = new TransformMesh(_fixture.Engine);
        workspace = transformFeature.Rotate(workspace, mesh.Metadata.Id, (float)(System.Math.PI / 4), Vector3.UnitZ).Value;
        var rotatedMeshId = workspace.ActiveMeshId;

        var mouldDef = new ContouredMouldDefinition(OffsetXY: 2.0);
        var result = _generateMouldFeature.Execute(workspace, rotatedMeshId, mouldDef);

        result.IsSuccess.Should().BeTrue();
        var mouldMesh = result.Value.GetActiveMesh().Value;

        // Boolean ops hand back bare metadata - the source mesh's prior commands (the
        // rotation) must be carried forward explicitly, in addition to the new MouldDefinition.
        mouldMesh.Metadata.Commands.OfType<RotateCommand>().Should().HaveCount(1);
        mouldMesh.Metadata.Commands.OfType<MouldDefinition>().Should().HaveCount(1);
    }

    [Fact]
    public void GenerateMould_Contoured_SubtractsTargetMesh()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(mesh.Metadata.Id).Value;

        var mouldDef = new ContouredMouldDefinition(OffsetXY: 2.0);

        var result = _generateMouldFeature.Execute(workspace, mesh.Metadata.Id, mouldDef);

        result.IsSuccess.Should().BeTrue();
        var updatedWorkspace = result.Value;

        var mouldMesh = updatedWorkspace.GetActiveMesh().Value;

        // Ensure the mould definition is tracked
        mouldMesh.Metadata.MouldDefinition().HasValue.Should().BeTrue();
        mouldMesh.Metadata.MouldDefinition().Value.TargetMeshId.Should().Be(mesh.Metadata.Id);

        // Subtracted target mesh should make it a hollow shell
        var stats = _fixture.Engine.Evaluators.GetStatistics(mouldMesh).Value;
        stats.Volume.Should().BeGreaterThan(0);
        
        // Since it's contoured and subtracted, the volume should be roughly the shell volume
    }

    [Fact]
    public void GenerateMould_Convex_IncludesAirChannels()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(mesh.Metadata.Id).Value;

        var airChannel = new AirChannelModel(
            System.Guid.NewGuid(),
            AirChannelType.Straight,
            2.0, 5.0, 5.0,
            new StraightAirChannel(new Vector3(0, 0, 10), 5.0f, 20.0f, 2.0f, 5.0f)
        );

        var mouldDef = new ConvexMouldDefinition(OffsetXY: 5.0, OffsetBottom: 5.0, OffsetTop: 5.0)
        {
            AirChannels = new[] { airChannel }
        };

        var result = _generateMouldFeature.Execute(workspace, mesh.Metadata.Id, mouldDef);

        result.IsSuccess.Should().BeTrue();
        var mouldMesh = result.Value.GetActiveMesh().Value;

        mouldMesh.Metadata.MouldDefinition().Value.AirChannels.Count.Should().Be(1);
    }

    [Fact]
    public void GenerateMould_Concave_GeneratesMould()
    {
        var workspace = Workspace.CreateEmpty();
        var mesh = _fixture.Engine.Generators.GenerateSphere(new Vector3(0, 0, 0), 10).Value;
        workspace = workspace.AddMesh(mesh).Value.SetActiveMesh(mesh.Metadata.Id).Value;

        var mouldDef = new ConcaveMouldDefinition(OffsetXY: 5.0, OffsetBottom: 5.0, OffsetTop: 5.0);

        var result = _generateMouldFeature.Execute(workspace, mesh.Metadata.Id, mouldDef);

        result.IsSuccess.Should().BeTrue();
        var mouldMesh = result.Value.GetActiveMesh().Value;

        var stats = _fixture.Engine.Evaluators.GetStatistics(mouldMesh).Value;
        stats.MaxZ.Should().BeGreaterThan(10);
        stats.MinZ.Should().BeLessThan(-10);
    }
}
