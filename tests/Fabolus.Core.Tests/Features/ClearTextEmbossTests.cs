using System.Numerics;
using Fabolus.Core.Features.Emboss;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Tests.Features;

[Collection("GeometryEngine collection")]
public class ClearTextEmbossTests
{
    private readonly GeometryEngineFixture _fixture;
    private readonly IGlyphOutlineSource _outlineSource;

    public ClearTextEmbossTests(GeometryEngineFixture fixture)
    {
        _fixture = fixture;
        _outlineSource = new TestGlyphOutlineSource();
    }

    [Fact]
    public void ClearTextEmboss_OnBaseMesh_RevertsMeshAndRemovesCommand()
    {
        var sphere = _fixture.Engine.Generators.GenerateSphere(Vector3.Zero, 15, 16).Value;
        var initialTriCount = sphere.TriangleCount;

        var mesh = sphere.WithMetadata(sphere.Metadata.WithBaseMesh(sphere));
        var workspace = Workspace.CreateEmpty().AddMesh(mesh).Value;

        var tool = new TextEmbossTool(_outlineSource);
        var decal = new TextDecal
        {
            Text = "TEST",
            Operation = EmbossOperation.Emboss,
            CapHeight = 5.0f,
            Depth = 0.8f,
            Anchor = new Vector3(0, 0, 15),
            AnchorNormal = Vector3.UnitZ
        };

        var applied = tool.Apply(_fixture.Engine, mesh, new[] { decal }).Value;
        var appliedMetadata = mesh.Metadata
            .WithCommand(new TextEmbossCommand(new[] { decal }, _outlineSource))
            .WithTextDecals(new[] { decal });

        var embossedMesh = applied.WithMetadata(appliedMetadata);
        workspace = workspace.UpdateMesh(embossedMesh).Value;

        embossedMesh.TriangleCount.Should().BeGreaterThan(initialTriCount);
        workspace.GetActiveMesh().Value.Metadata.TextDecals().HasValue.Should().BeTrue();

        var clearFeature = new ClearTextEmboss(_fixture.Engine);
        var clearedResult = clearFeature.Execute(workspace);

        clearedResult.IsSuccess.Should().BeTrue();
        var clearedMesh = clearedResult.Value.GetActiveMesh().Value;

        clearedMesh.TriangleCount.Should().Be(initialTriCount);
        clearedMesh.Metadata.TextDecals().HasNoValue.Should().BeTrue();
        clearedMesh.Metadata.Commands.Should().NotContain(c => c is TextEmbossCommand);
    }

    [Fact]
    public void ClearTextEmboss_OnBaseMeshWithDownstreamMould_ClearsMouldAndRevertsToCleanBase()
    {
        var sphere = _fixture.Engine.Generators.GenerateSphere(Vector3.Zero, 15, 16).Value;
        var initialTriCount = sphere.TriangleCount;
        var mesh = sphere.WithMetadata(sphere.Metadata.WithBaseMesh(sphere));
        var workspace = Workspace.CreateEmpty().AddMesh(mesh).Value;

        var tool = new TextEmbossTool(_outlineSource);
        var decal = new TextDecal
        {
            Text = "MOLD",
            Operation = EmbossOperation.Emboss,
            CapHeight = 4.0f,
            Depth = 0.6f,
            Anchor = new Vector3(0, 0, 15),
            AnchorNormal = Vector3.UnitZ
        };

        var applied = tool.Apply(_fixture.Engine, mesh, new[] { decal }).Value;
        var appliedMetadata = mesh.Metadata
            .WithCommand(new TextEmbossCommand(new[] { decal }, _outlineSource))
            .WithTextDecals(new[] { decal });

        var embossedBase = applied.WithMetadata(appliedMetadata);

        var mouldDef = new ConcaveMouldDefinition(OffsetXY: 5.0, OffsetBottom: 5.0, OffsetTop: 5.0);

        var mouldResult = mouldDef.Apply(_fixture.Engine, embossedBase).Value;
        var mouldMetadata = embossedBase.Metadata
            .WithCommand(mouldDef)
            .WithMouldDefinition(mouldDef);

        var mouldMesh = mouldResult.WithMetadata(mouldMetadata);
        workspace = workspace.UpdateMesh(mouldMesh).Value;

        var clearFeature = new ClearTextEmboss(_fixture.Engine);
        var clearedResult = clearFeature.Execute(workspace);

        clearedResult.IsSuccess.Should().BeTrue();
        var clearedBase = clearedResult.Value.GetActiveMesh().Value;

        clearedBase.TriangleCount.Should().Be(initialTriCount);
        clearedBase.Metadata.TextDecals().HasNoValue.Should().BeTrue();
        clearedBase.Metadata.MouldDefinition().HasNoValue.Should().BeTrue();
        clearedBase.Metadata.Commands.Should().NotContain(c => c is TextEmbossCommand);
        clearedBase.Metadata.Commands.Should().NotContain(c => c is MouldDefinition);
    }

    [Fact]
    public void ClearTextEmboss_OnMouldMesh_ClearsMouldDecalsAndPreservesMould()
    {
        var sphere = _fixture.Engine.Generators.GenerateSphere(Vector3.Zero, 15, 16).Value;
        var mesh = sphere.WithMetadata(sphere.Metadata.WithBaseMesh(sphere));
        var workspace = Workspace.CreateEmpty().AddMesh(mesh).Value;

        var mouldDef = new ConcaveMouldDefinition(OffsetXY: 5.0, OffsetBottom: 5.0, OffsetTop: 5.0);
        var mouldResult = mouldDef.Apply(_fixture.Engine, mesh).Value;
        var mouldMetadata = mesh.Metadata
            .WithCommand(mouldDef)
            .WithMouldDefinition(mouldDef);
        var mouldMesh = mouldResult.WithMetadata(mouldMetadata);

        var tool = new TextEmbossTool(_outlineSource);
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

        var applied = tool.Apply(_fixture.Engine, mouldMesh, new[] { mouldDecal }).Value;
        var appliedMetadata = mouldMesh.Metadata
            .WithCommand(new MouldTextEmbossCommand(new[] { mouldDecal }, _outlineSource))
            .WithTextDecals(new[] { mouldDecal });

        var embossedMould = applied.WithMetadata(appliedMetadata);
        workspace = workspace.UpdateMesh(embossedMould).Value;

        var clearFeature = new ClearTextEmboss(_fixture.Engine);
        var clearedResult = clearFeature.Execute(workspace);

        clearedResult.IsSuccess.Should().BeTrue();
        var clearedMould = clearedResult.Value.GetActiveMesh().Value;

        clearedMould.Metadata.TextDecals().HasNoValue.Should().BeTrue();
        clearedMould.Metadata.MouldDefinition().HasValue.Should().BeTrue();
        clearedMould.Metadata.Commands.Should().NotContain(c => c is MouldTextEmbossCommand);
        clearedMould.Metadata.Commands.Should().Contain(c => c is MouldDefinition);
    }
}
