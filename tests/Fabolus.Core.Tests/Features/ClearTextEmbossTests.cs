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
            AnchorNormal = Vector3.UnitZ,
            ProjectOntoSurface = false
        };

        var applied = tool.Apply(_fixture.Engine, mesh, decal).Value;
        var appliedMetadata = mesh.Metadata
            .WithCommand(new TextEmbossCommand(decal, _outlineSource))
            .WithTextDecal(decal);

        var embossedMesh = applied.WithMetadata(appliedMetadata);
        workspace = workspace.UpdateMesh(embossedMesh).Value;

        embossedMesh.TriangleCount.Should().BeGreaterThan(initialTriCount);
        workspace.GetActiveMesh().Value.Metadata.TextDecal().HasValue.Should().BeTrue();

        var clearFeature = new ClearTextEmboss(_fixture.Engine);
        var clearedResult = clearFeature.Execute(workspace);

        clearedResult.IsSuccess.Should().BeTrue();
        var clearedMesh = clearedResult.Value.GetActiveMesh().Value;

        clearedMesh.TriangleCount.Should().Be(initialTriCount);
        clearedMesh.Metadata.TextDecal().HasNoValue.Should().BeTrue();
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
            AnchorNormal = Vector3.UnitZ,
            ProjectOntoSurface = false
        };

        var applied = tool.Apply(_fixture.Engine, mesh, decal).Value;
        var appliedMetadata = mesh.Metadata
            .WithCommand(new TextEmbossCommand(decal, _outlineSource))
            .WithTextDecal(decal);

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
        clearedBase.Metadata.TextDecal().HasNoValue.Should().BeTrue();
        clearedBase.Metadata.MouldDefinition().HasNoValue.Should().BeTrue();
        clearedBase.Metadata.Commands.Should().NotContain(c => c is TextEmbossCommand);
        clearedBase.Metadata.Commands.Should().NotContain(c => c is MouldDefinition);
    }
}
