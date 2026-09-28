using System.Numerics;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Tests.Features;

[Collection("GeometryEngine collection")]
public class ClearDecalsTests
{
    private readonly GeometryEngineFixture _fixture;
    private readonly IGlyphOutlineSource _outlineSource;

    public ClearDecalsTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _outlineSource = new TestGlyphOutlineSource();
        GlyphOutlineSourceProvider.Default = _outlineSource;
    }

    [Fact]
    public void ClearDecals_OnBaseMesh_RevertsMeshAndRemovesCommand()
    {
        var sphere = _fixture.Engine.Generators.GenerateSphere(Vector3.Zero, 15, 16).Value;
        var initialTriCount = sphere.TriangleCount;

        // AddMesh establishes the base mesh for the entry, so the replay has something to
        // revert to without the test wiring one up by hand.
        var (workspace, id) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), sphere);

        var tool = new GenerateDecals(_outlineSource);
        var decal = new TextDecal
        {
            Text = "TEST",
            Operation = EmbossOperation.Emboss,
            CapHeight = 5.0f,
            Depth = 0.8f,
            Anchor = new Vector3(0, 0, 15),
            AnchorNormal = Vector3.UnitZ
        };

        var embossedMesh = tool.Execute(_fixture.Engine, sphere, new[] { decal }).Value;
        var embossedRecord = workspace.GetRecord(id).Value.WithCommand(new DecalCommand(new[] { decal }));
        workspace = workspace.UpdateMesh(id, embossedMesh, embossedRecord).Value;

        embossedMesh.TriangleCount.Should().BeGreaterThan(initialTriCount);
        workspace.GetActiveRecord().Value.TextDecals().Should().NotBeEmpty();

        var clearFeature = new ClearDecals(_fixture.Engine);
        var clearedResult = clearFeature.Execute(workspace);

        clearedResult.IsSuccess.Should().BeTrue();
        var clearedMesh = clearedResult.Value.GetActiveMesh().Value;
        var clearedRecord = clearedResult.Value.GetActiveRecord().Value;

        clearedMesh.TriangleCount.Should().Be(initialTriCount);
        clearedRecord.TextDecals().Should().BeEmpty();
        clearedRecord.Commands.Should().NotContain(c => c is DecalCommand);

        // Identity is untouched by the round trip.
        clearedRecord.Id.Should().Be(id);
    }

    [Fact]
    public void ClearDecals_OnBaseMeshWithDownstreamMould_ClearsMouldAndRevertsToCleanBase()
    {
        var sphere = _fixture.Engine.Generators.GenerateSphere(Vector3.Zero, 15, 16).Value;
        var initialTriCount = sphere.TriangleCount;
        var (workspace, id) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), sphere);

        var tool = new GenerateDecals(_outlineSource);
        var decal = new TextDecal
        {
            Text = "MOLD",
            Operation = EmbossOperation.Emboss,
            CapHeight = 4.0f,
            Depth = 0.6f,
            Anchor = new Vector3(0, 0, 15),
            AnchorNormal = Vector3.UnitZ
        };

        var embossedBase = tool.Execute(_fixture.Engine, sphere, new[] { decal }).Value;
        var record = workspace.GetRecord(id).Value.WithCommand(new DecalCommand(new[] { decal }));

        var mouldDef = new ConcaveMouldDefinition(OffsetXY: 5.0, OffsetBottom: 5.0, OffsetTop: 5.0);
        var mouldMesh = mouldDef.Apply(_fixture.Engine, embossedBase).Value;
        record = record.WithMouldDefinition(mouldDef);

        workspace = workspace.UpdateMesh(id, mouldMesh, record).Value;

        var clearFeature = new ClearDecals(_fixture.Engine);
        var clearedResult = clearFeature.Execute(workspace);

        clearedResult.IsSuccess.Should().BeTrue();
        var clearedBase = clearedResult.Value.GetActiveMesh().Value;
        var clearedRecord = clearedResult.Value.GetActiveRecord().Value;

        clearedBase.TriangleCount.Should().Be(initialTriCount);
        clearedRecord.TextDecals().Should().BeEmpty();
        clearedRecord.MouldDefinition().Should().BeNull();
        clearedRecord.Commands.Should().NotContain(c => c is DecalCommand);
        clearedRecord.Commands.Should().NotContain(c => c is MouldDefinition);
    }

    [Fact]
    public void ClearDecals_OnMouldMesh_ClearsMouldDecalsAndPreservesMould()
    {
        var sphere = _fixture.Engine.Generators.GenerateSphere(Vector3.Zero, 15, 16).Value;
        var (workspace, id) = GeometryEngineFixture.AddActive(Workspace.CreateEmpty(), sphere);

        var mouldDef = new ConcaveMouldDefinition(OffsetXY: 5.0, OffsetBottom: 5.0, OffsetTop: 5.0);
        var mouldMesh = mouldDef.Apply(_fixture.Engine, sphere).Value;
        var record = workspace.GetRecord(id).Value.WithMouldDefinition(mouldDef);

        var tool = new GenerateDecals(_outlineSource);
        var mouldDecal = new TextDecal
        {
            Text = "MLD",
            Operation = EmbossOperation.Emboss,
            Target = EmbossTarget.Mould,
            CapHeight = 4.0f,
            Depth = 0.6f,
            Anchor = new Vector3(0, 0, 20),
            AnchorNormal = Vector3.UnitZ
        };

        var embossedMould = tool.Execute(_fixture.Engine, mouldMesh, new[] { mouldDecal }).Value;
        record = record.WithCommand(new MouldDecalCommand(new[] { mouldDecal }));

        workspace = workspace.UpdateMesh(id, embossedMould, record).Value;

        var clearFeature = new ClearDecals(_fixture.Engine);
        var clearedResult = clearFeature.Execute(workspace);

        clearedResult.IsSuccess.Should().BeTrue();
        var clearedRecord = clearedResult.Value.GetActiveRecord().Value;

        clearedRecord.TextDecals().Should().BeEmpty();
        clearedRecord.MouldDefinition().Should().NotBeNull();
        clearedRecord.Commands.Should().NotContain(c => c is MouldDecalCommand);
        clearedRecord.Commands.Should().Contain(c => c is MouldDefinition);
    }
}
