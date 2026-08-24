using System.Numerics;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Features.Emboss;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Emboss;
using Moq;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.Emboss;

public sealed class TestOutlineSource : IGlyphOutlineSource
{
    public IReadOnlyList<Polygon2D> GetOutlines(string text, DecalFont font, float capHeight, float tracking)
    {
        return new List<Polygon2D>
        {
            new()
            {
                OuterBoundary = new List<Vector2>
                {
                    new(-5, -3), new(5, -3), new(5, 3), new(-5, 3)
                }
            }
        };
    }

    public TextMetrics MeasureText(string text, DecalFont font, float capHeight, float tracking)
    {
        return new TextMetrics(10f, capHeight, new float[] { 10f });
    }
}

public class EmbossViewModelTests
{
    private static (EmbossViewModel vm, IMessenger messenger, Mock<IGeometryEngine> engineMock) CreateViewModel()
    {
        var messenger = new StrongReferenceMessenger();
        var preferences = new Dictionary<string, object>
        {
            [UISettings.PrintBedWidthLabel] = 250.0f,
            [UISettings.PrintBedDepthLabel] = 250.0f,
            [UISettings.ShowBedGridLabel] = true,
        };

        messenger.Register<Dictionary<string, object>, AppPreferenceRequestMessage>(preferences, (r, m) =>
        {
            if (r.TryGetValue(m.Key, out var val))
                m.Reply(val);
        });

        var engineMock = new Mock<IGeometryEngine>();
        var alertMock = new Mock<IAlertDialog>();
        var outlineSource = new TestOutlineSource();

        var prismMock = new Mock<IMesh>();
        prismMock.Setup(m => m.Vertices).Returns(new Vector3[3]);
        prismMock.Setup(m => m.Triangles).Returns(new int[3]);
        engineMock.Setup(e => e.Generators.BuildTextPrism(
            It.IsAny<IReadOnlyList<Polygon2D>>(),
            It.IsAny<DecalFrame>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<IMesh?>()))
            .Returns(Result<IMesh>.Success(prismMock.Object));
        engineMock.Setup(e => e.Evaluators.GetRenderData(It.IsAny<IMesh>()))
            .Returns(Result<RenderData>.Success(new RenderData { Vertices = new double[9], Triangles = new int[3] }));

        var vm = new EmbossViewModel(messenger, alertMock.Object, engineMock.Object, outlineSource);
        return (vm, messenger, engineMock);
    }


    [Fact]
    public void Operation_ChangingToEngrave_UpdatesDepthLabel()
    {
        var (vm, _, _) = CreateViewModel();

        vm.Operation = EmbossOperation.Emboss;
        Assert.Equal("Height", vm.DepthLabel);
        Assert.Equal("Apply decals", vm.ApplyLabel);

        vm.Operation = EmbossOperation.Engrave;
        Assert.Equal("Depth", vm.DepthLabel);
        Assert.Equal("Apply decals", vm.ApplyLabel);
    }

    [Fact]
    public void StartPlacingCommand_TogglesIsPicking()
    {
        var (vm, _, _) = CreateViewModel();

        Assert.False(vm.IsPicking);

        vm.StartPlacingCommand.Execute(null);
        Assert.True(vm.IsPicking);

        vm.StartPlacingCommand.Execute(null);
        Assert.False(vm.IsPicking);
    }

    [Fact]
    public void ClearTextCommand_WhenNotApplied_DoesNothing()
    {
        var (vm, _, _) = CreateViewModel();
        Assert.False(vm.IsApplied);

        vm.ClearTextCommand.Execute(null);
        Assert.False(vm.IsApplied);
    }

    [Fact]
    public async Task ActivateAsync_WithImportedTextEmbossCommand_InheritsDecalsAndSetsIsAppliedTrue()
    {
        var (vm, _, engineMock) = CreateViewModel();
        var mockMesh = new Mock<IMesh>();
        mockMesh.Setup(m => m.Vertices).Returns(new Vector3[3]);
        mockMesh.Setup(m => m.Triangles).Returns(new int[3]);

        var decal = new TextDecal
        {
            Text = "IMPORTED",
            CapHeight = 7.5f,
            Depth = 1.2f,
            Operation = EmbossOperation.Engrave,
            RotationDeg = 30f,
            Anchor = new Vector3(5, 10, 15),
            AnchorNormal = Vector3.UnitZ
        };
        var command = new TextEmbossCommand(new[] { decal });
        var metadata = new MeshMetadata()
            .WithId(Guid.NewGuid())
            .WithName("Test")
            .WithCommand(command);
        mockMesh.Setup(m => m.Metadata).Returns(metadata);
        mockMesh.Setup(m => m.WithMetadata(It.IsAny<MeshMetadata>()))
            .Returns<MeshMetadata>(meta =>
            {
                var copy = new Mock<IMesh>();
                copy.Setup(x => x.Metadata).Returns(meta);
                copy.Setup(x => x.Vertices).Returns(new Vector3[3]);
                copy.Setup(x => x.Triangles).Returns(new int[3]);
                return copy.Object;
            });

        engineMock.Setup(e => e.Evaluators.GetStatistics(It.IsAny<IMesh>()))
            .Returns(Result<MeshStatistics>.Success(new MeshStatistics { MaxZ = 10 }));
        engineMock.Setup(e => e.Evaluators.GetRenderData(It.IsAny<IMesh>()))
            .Returns(Result<RenderData>.Success(new RenderData { Vertices = new double[9], Triangles = new int[3] }));

        var prismMock = new Mock<IMesh>();
        prismMock.Setup(m => m.Vertices).Returns(new Vector3[3]);
        prismMock.Setup(m => m.Triangles).Returns(new int[3]);
        engineMock.Setup(e => e.Generators.BuildTextPrism(
            It.IsAny<IReadOnlyList<Polygon2D>>(),
            It.IsAny<DecalFrame>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<IMesh?>()))
            .Returns(Result<IMesh>.Success(prismMock.Object));
        engineMock.Setup(e => e.Generators.GenerateSphere(It.IsAny<Vector3>(), It.IsAny<double>(), It.IsAny<int>()))
            .Returns(Result<IMesh>.Success(prismMock.Object));

        var workspace = Workspace.CreateEmpty().AddMesh(mockMesh.Object).Value;
        await vm.ActivateAsync(workspace);

        Assert.True(vm.IsApplied);
        Assert.False(vm.HasMould);
        Assert.Equal(EmbossTarget.Base, vm.Target);
        Assert.Equal("IMPORTED", vm.LabelText);
        Assert.Equal(7.5f, vm.CapHeight);
        Assert.Equal(1.2f, vm.Depth);
        Assert.Equal(EmbossOperation.Engrave, vm.Operation);
        Assert.Equal(30, vm.Rotation);
        Assert.Equal("Applied", vm.StatusWord);
        Assert.Equal(1, vm.DecalCount);
    }

    [Fact]
    public async Task ActivateAsync_WithMouldMesh_SetsHasMouldTrue()
    {
        var (vm, _, engineMock) = CreateViewModel();
        var mockMesh = new Mock<IMesh>();
        mockMesh.Setup(m => m.Vertices).Returns(new Vector3[3]);
        mockMesh.Setup(m => m.Triangles).Returns(new int[3]);

        var mouldDef = new Fabolus.Core.Features.Moulds.ConcaveMouldDefinition(5, 5, 5);
        var metadata = new MeshMetadata()
            .WithId(Guid.NewGuid())
            .WithName("Mould Mesh")
            .WithCommand(mouldDef);
        mockMesh.Setup(m => m.Metadata).Returns(metadata);
        mockMesh.Setup(m => m.WithMetadata(It.IsAny<MeshMetadata>()))
            .Returns<MeshMetadata>(meta =>
            {
                var copy = new Mock<IMesh>();
                copy.Setup(x => x.Metadata).Returns(meta);
                copy.Setup(x => x.Vertices).Returns(new Vector3[3]);
                copy.Setup(x => x.Triangles).Returns(new int[3]);
                return copy.Object;
            });

        engineMock.Setup(e => e.Evaluators.GetStatistics(It.IsAny<IMesh>()))
            .Returns(Result<MeshStatistics>.Success(new MeshStatistics { MaxZ = 10 }));
        engineMock.Setup(e => e.Evaluators.GetRenderData(It.IsAny<IMesh>()))
            .Returns(Result<RenderData>.Success(new RenderData { Vertices = new double[9], Triangles = new int[3] }));

        var prismMock = new Mock<IMesh>();
        prismMock.Setup(m => m.Vertices).Returns(new Vector3[3]);
        prismMock.Setup(m => m.Triangles).Returns(new int[3]);
        engineMock.Setup(e => e.Generators.BuildTextPrism(
            It.IsAny<IReadOnlyList<Polygon2D>>(),
            It.IsAny<DecalFrame>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<IMesh?>()))
            .Returns(Result<IMesh>.Success(prismMock.Object));
        engineMock.Setup(e => e.Generators.GenerateSphere(It.IsAny<Vector3>(), It.IsAny<double>(), It.IsAny<int>()))
            .Returns(Result<IMesh>.Success(prismMock.Object));
        engineMock.Setup(e => e.CloneMesh(It.IsAny<IMesh>()))
            .Returns<IMesh>(m => Result<IMesh>.Success(m));

        var workspace = Workspace.CreateEmpty().AddMesh(mockMesh.Object).Value;
        await vm.ActivateAsync(workspace);

        Assert.True(vm.HasMould);
    }

    [Fact]
    public async Task ApplyCommand_OnBaseMesh_AppliesEmbossSuccessfully()
    {
        var (vm, _, engineMock) = CreateViewModel();
        var mockMesh = new Mock<IMesh>();
        mockMesh.Setup(m => m.Vertices).Returns(new Vector3[3]);
        mockMesh.Setup(m => m.Triangles).Returns(new int[3]);

        var metadata = new MeshMetadata()
            .WithId(Guid.NewGuid())
            .WithName("Base Mesh");
        mockMesh.Setup(m => m.Metadata).Returns(metadata);
        mockMesh.Setup(m => m.WithMetadata(It.IsAny<MeshMetadata>()))
            .Returns<MeshMetadata>(meta =>
            {
                var copy = new Mock<IMesh>();
                copy.Setup(x => x.Metadata).Returns(meta);
                copy.Setup(x => x.Vertices).Returns(new Vector3[3]);
                copy.Setup(x => x.Triangles).Returns(new int[3]);
                copy.Setup(x => x.WithMetadata(It.IsAny<MeshMetadata>()))
                    .Returns<MeshMetadata>(m2 => copy.Object);
                return copy.Object;
            });

        engineMock.Setup(e => e.Evaluators.GetStatistics(It.IsAny<IMesh>()))
            .Returns(Result<MeshStatistics>.Success(new MeshStatistics { MaxZ = 10 }));
        engineMock.Setup(e => e.Evaluators.ValidateTopology(It.IsAny<IMesh>()))
            .Returns(Result<TopologyValidation>.Success(new TopologyValidation { IsWatertight = true, IsManifold = true }));
        engineMock.Setup(e => e.Evaluators.GetRenderData(It.IsAny<IMesh>()))
            .Returns(Result<RenderData>.Success(new RenderData { Vertices = new double[9], Triangles = new int[3] }));

        var embossedMeshMock = new Mock<IMesh>();
        embossedMeshMock.Setup(m => m.Vertices).Returns(new Vector3[3]);
        embossedMeshMock.Setup(m => m.Triangles).Returns(new int[3]);
        embossedMeshMock.Setup(m => m.WithMetadata(It.IsAny<MeshMetadata>()))
            .Returns<MeshMetadata>(meta =>
            {
                var copy = new Mock<IMesh>();
                copy.Setup(x => x.Metadata).Returns(meta);
                copy.Setup(x => x.Vertices).Returns(new Vector3[3]);
                copy.Setup(x => x.Triangles).Returns(new int[3]);
                return copy.Object;
            });

        engineMock.Setup(e => e.Booleans.Union(It.IsAny<IMesh>(), It.IsAny<IMesh>()))
            .Returns(Result<IMesh>.Success(embossedMeshMock.Object));
        engineMock.Setup(e => e.Generators.BuildTextPrism(
            It.IsAny<IReadOnlyList<Polygon2D>>(),
            It.IsAny<DecalFrame>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<float>(),
            It.IsAny<IMesh?>()))
            .Returns(Result<IMesh>.Success(embossedMeshMock.Object));
        engineMock.Setup(e => e.Generators.GenerateSphere(It.IsAny<Vector3>(), It.IsAny<double>(), It.IsAny<int>()))
            .Returns(Result<IMesh>.Success(embossedMeshMock.Object));

        var workspace = Workspace.CreateEmpty().AddMesh(mockMesh.Object).Value;
        await vm.ActivateAsync(workspace);

        vm.LabelText = "TEST";
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.True(vm.IsApplied);
        Assert.Equal("Applied", vm.StatusWord);
        Assert.Empty(vm.ErrorText);
    }

    [Fact]
    public async Task DeleteSelectedDecal_RemovesDecalAndUpdatesCount()
    {
        var (vm, _, engineMock) = CreateViewModel();
        var mockMesh = new Mock<IMesh>();
        mockMesh.Setup(m => m.Vertices).Returns(new Vector3[3]);
        mockMesh.Setup(m => m.Triangles).Returns(new int[3]);
        var metadata = new MeshMetadata().WithId(Guid.NewGuid()).WithName("Test");
        mockMesh.Setup(m => m.Metadata).Returns(metadata);
        mockMesh.Setup(m => m.WithMetadata(It.IsAny<MeshMetadata>()))
            .Returns<MeshMetadata>(meta =>
            {
                var copy = new Mock<IMesh>();
                copy.Setup(x => x.Metadata).Returns(meta);
                copy.Setup(x => x.Vertices).Returns(new Vector3[3]);
                copy.Setup(x => x.Triangles).Returns(new int[3]);
                return copy.Object;
            });

        engineMock.Setup(e => e.Evaluators.GetStatistics(It.IsAny<IMesh>()))
            .Returns(Result<MeshStatistics>.Success(new MeshStatistics { MaxZ = 10 }));
        engineMock.Setup(e => e.Evaluators.GetRenderData(It.IsAny<IMesh>()))
            .Returns(Result<RenderData>.Success(new RenderData { Vertices = new double[9], Triangles = new int[3] }));

        var workspace = Workspace.CreateEmpty().AddMesh(mockMesh.Object).Value;
        await vm.ActivateAsync(workspace);

        Assert.Equal(1, vm.DecalCount);

        vm.DeleteSelectedDecalCommand.Execute(null);
        Assert.Equal(0, vm.DecalCount);
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);
    }

    [Fact]
    public async Task ClearDecals_ClearsAllDecals()
    {
        var (vm, _, engineMock) = CreateViewModel();
        var mockMesh = new Mock<IMesh>();
        mockMesh.Setup(m => m.Vertices).Returns(new Vector3[3]);
        mockMesh.Setup(m => m.Triangles).Returns(new int[3]);
        var metadata = new MeshMetadata().WithId(Guid.NewGuid()).WithName("Test");
        mockMesh.Setup(m => m.Metadata).Returns(metadata);
        mockMesh.Setup(m => m.WithMetadata(It.IsAny<MeshMetadata>()))
            .Returns<MeshMetadata>(meta =>
            {
                var copy = new Mock<IMesh>();
                copy.Setup(x => x.Metadata).Returns(meta);
                copy.Setup(x => x.Vertices).Returns(new Vector3[3]);
                copy.Setup(x => x.Triangles).Returns(new int[3]);
                return copy.Object;
            });

        engineMock.Setup(e => e.Evaluators.GetStatistics(It.IsAny<IMesh>()))
            .Returns(Result<MeshStatistics>.Success(new MeshStatistics { MaxZ = 10 }));
        engineMock.Setup(e => e.Evaluators.GetRenderData(It.IsAny<IMesh>()))
            .Returns(Result<RenderData>.Success(new RenderData { Vertices = new double[9], Triangles = new int[3] }));

        var workspace = Workspace.CreateEmpty().AddMesh(mockMesh.Object).Value;
        await vm.ActivateAsync(workspace);

        Assert.Equal(1, vm.DecalCount);

        vm.ClearDecalsCommand.Execute(null);
        Assert.Equal(0, vm.DecalCount);
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);
        Assert.Empty(vm.DecalList);
    }

    [Fact]
    public async Task DecalList_SyncsWithDecalsAndSelection()
    {
        var (vm, _, engineMock) = CreateViewModel();
        var mockMesh = new Mock<IMesh>();
        mockMesh.Setup(m => m.Vertices).Returns(new Vector3[3]);
        mockMesh.Setup(m => m.Triangles).Returns(new int[3]);
        var metadata = new MeshMetadata().WithId(Guid.NewGuid()).WithName("Test");
        mockMesh.Setup(m => m.Metadata).Returns(metadata);
        mockMesh.Setup(m => m.WithMetadata(It.IsAny<MeshMetadata>()))
            .Returns<MeshMetadata>(meta =>
            {
                var copy = new Mock<IMesh>();
                copy.Setup(x => x.Metadata).Returns(meta);
                copy.Setup(x => x.Vertices).Returns(new Vector3[3]);
                copy.Setup(x => x.Triangles).Returns(new int[3]);
                return copy.Object;
            });

        engineMock.Setup(e => e.Evaluators.GetStatistics(It.IsAny<IMesh>()))
            .Returns(Result<MeshStatistics>.Success(new MeshStatistics { MaxZ = 10 }));
        engineMock.Setup(e => e.Evaluators.GetRenderData(It.IsAny<IMesh>()))
            .Returns(Result<RenderData>.Success(new RenderData { Vertices = new double[9], Triangles = new int[3] }));

        var workspace = Workspace.CreateEmpty().AddMesh(mockMesh.Object).Value;
        await vm.ActivateAsync(workspace);

        Assert.Single(vm.DecalList);
        Assert.Equal("FABOLUS", vm.DecalList[0].Text);
        Assert.True(vm.DecalList[0].IsSelected);

        // Edit text
        vm.LabelText = "NEW TEXT";
        Assert.Equal("NEW TEXT", vm.DecalList[0].Text);

        // Delete item by ID
        var id = vm.DecalList[0].Id;
        vm.DeleteDecalByIdCommand.Execute(id);
        Assert.Empty(vm.DecalList);
        Assert.Equal(0, vm.DecalCount);
    }

    [Fact]
    public async Task ApplyCommand_CollapsesDecalsExpander()
    {
        var (vm, _, engineMock) = CreateViewModel();
        var mockMesh = new Mock<IMesh>();
        mockMesh.Setup(m => m.Vertices).Returns(new Vector3[3]);
        mockMesh.Setup(m => m.Triangles).Returns(new int[3]);
        var metadata = new MeshMetadata().WithId(Guid.NewGuid()).WithName("Test");
        mockMesh.Setup(m => m.Metadata).Returns(metadata);
        mockMesh.Setup(m => m.WithMetadata(It.IsAny<MeshMetadata>()))
            .Returns<MeshMetadata>(meta =>
            {
                var copy = new Mock<IMesh>();
                copy.Setup(x => x.Metadata).Returns(meta);
                copy.Setup(x => x.Vertices).Returns(new Vector3[3]);
                copy.Setup(x => x.Triangles).Returns(new int[3]);
                copy.Setup(x => x.WithMetadata(It.IsAny<MeshMetadata>()))
                    .Returns<MeshMetadata>(m2 => copy.Object);
                return copy.Object;
            });

        engineMock.Setup(e => e.Evaluators.GetStatistics(It.IsAny<IMesh>()))
            .Returns(Result<MeshStatistics>.Success(new MeshStatistics { MaxZ = 10 }));
        engineMock.Setup(e => e.Evaluators.ValidateTopology(It.IsAny<IMesh>()))
            .Returns(Result<TopologyValidation>.Success(new TopologyValidation { IsWatertight = true, IsManifold = true }));
        engineMock.Setup(e => e.Evaluators.GetRenderData(It.IsAny<IMesh>()))
            .Returns(Result<RenderData>.Success(new RenderData { Vertices = new double[9], Triangles = new int[3] }));

        var embossedMeshMock = new Mock<IMesh>();
        embossedMeshMock.Setup(m => m.Vertices).Returns(new Vector3[3]);
        embossedMeshMock.Setup(m => m.Triangles).Returns(new int[3]);
        embossedMeshMock.Setup(m => m.WithMetadata(It.IsAny<MeshMetadata>()))
            .Returns<MeshMetadata>(meta =>
            {
                var copy = new Mock<IMesh>();
                copy.Setup(x => x.Metadata).Returns(meta);
                copy.Setup(x => x.Vertices).Returns(new Vector3[3]);
                copy.Setup(x => x.Triangles).Returns(new int[3]);
                return copy.Object;
            });

        engineMock.Setup(e => e.Booleans.Union(It.IsAny<IMesh>(), It.IsAny<IMesh>()))
            .Returns(Result<IMesh>.Success(embossedMeshMock.Object));

        var workspace = Workspace.CreateEmpty().AddMesh(mockMesh.Object).Value;
        await vm.ActivateAsync(workspace);

        Assert.True(vm.IsDecalsExpanded);

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.True(vm.IsApplied);
        Assert.False(vm.IsDecalsExpanded);
    }

    [Fact]
    public async Task ActivateAsync_WithMould_SetsHasMouldAndTargetOnDecalList()
    {
        var (vm, _, engineMock) = CreateViewModel();
        var mockMesh = new Mock<IMesh>();
        mockMesh.Setup(m => m.Vertices).Returns(new Vector3[3]);
        mockMesh.Setup(m => m.Triangles).Returns(new int[3]);

        var mouldDef = new ConcaveMouldDefinition();
        var decal1 = new TextDecal { Id = Guid.NewGuid(), Text = "BASE1", Target = EmbossTarget.Base, CapHeight = 5f, Operation = EmbossOperation.Emboss };
        var decal2 = new TextDecal { Id = Guid.NewGuid(), Text = "MOULD1", Target = EmbossTarget.Mould, CapHeight = 6f, Operation = EmbossOperation.Engrave };

        var metadata = new MeshMetadata()
            .WithId(Guid.NewGuid())
            .WithName("MouldMesh")
            .WithBaseMesh(mockMesh.Object)
            .WithMouldDefinition(mouldDef)
            .WithCommand(mouldDef)
            .WithTextDecals(new[] { decal1, decal2 });

        mockMesh.Setup(m => m.Metadata).Returns(metadata);
        mockMesh.Setup(m => m.WithMetadata(It.IsAny<MeshMetadata>()))
            .Returns<MeshMetadata>(meta =>
            {
                var copy = new Mock<IMesh>();
                copy.Setup(x => x.Metadata).Returns(meta);
                copy.Setup(x => x.Vertices).Returns(new Vector3[3]);
                copy.Setup(x => x.Triangles).Returns(new int[3]);
                return copy.Object;
            });

        engineMock.Setup(e => e.CloneMesh(It.IsAny<IMesh>()))
            .Returns<IMesh>(m => Result<IMesh>.Success(m));
        engineMock.Setup(e => e.Evaluators.GetStatistics(It.IsAny<IMesh>()))
            .Returns(Result<MeshStatistics>.Success(new MeshStatistics { MaxZ = 10 }));
        engineMock.Setup(e => e.Evaluators.GetRenderData(It.IsAny<IMesh>()))
            .Returns(Result<RenderData>.Success(new RenderData { Vertices = new double[9], Triangles = new int[3] }));

        var workspace = Workspace.CreateEmpty().AddMesh(mockMesh.Object).Value;
        await vm.ActivateAsync(workspace);

        Assert.True(vm.HasMould);
        Assert.True(vm.IsApplied);
        Assert.Equal(2, vm.DecalCount);
        Assert.Equal(2, vm.DecalList.Count);

        Assert.Equal(EmbossTarget.Base, vm.DecalList[0].Target);
        Assert.Equal("Base", vm.DecalList[0].TargetText);
        Assert.True(vm.DecalList[0].HasMould);
        Assert.Contains("Base", vm.DecalList[0].Summary);

        Assert.Equal(EmbossTarget.Mould, vm.DecalList[1].Target);
        Assert.Equal("Mould", vm.DecalList[1].TargetText);
        Assert.True(vm.DecalList[1].HasMould);
        Assert.Contains("Mould", vm.DecalList[1].Summary);
    }

    [Fact]
    public async Task StartPlacing_SwitchingTargetToMouldAndPlacingDecal_PreservesPreviousBaseDecalTarget()
    {
        var (vm, _, engineMock) = CreateViewModel();
        var mockMesh = new Mock<IMesh>();
        mockMesh.Setup(m => m.Vertices).Returns(new Vector3[3]);
        mockMesh.Setup(m => m.Triangles).Returns(new int[3]);

        var mouldDef = new ConcaveMouldDefinition();
        var metadata = new MeshMetadata()
            .WithId(Guid.NewGuid())
            .WithName("MouldMesh")
            .WithBaseMesh(mockMesh.Object)
            .WithMouldDefinition(mouldDef)
            .WithCommand(mouldDef);

        mockMesh.Setup(m => m.Metadata).Returns(metadata);
        mockMesh.Setup(m => m.WithMetadata(It.IsAny<MeshMetadata>()))
            .Returns<MeshMetadata>(meta =>
            {
                var copy = new Mock<IMesh>();
                copy.Setup(x => x.Metadata).Returns(meta);
                copy.Setup(x => x.Vertices).Returns(new Vector3[3]);
                copy.Setup(x => x.Triangles).Returns(new int[3]);
                return copy.Object;
            });

        engineMock.Setup(e => e.CloneMesh(It.IsAny<IMesh>()))
            .Returns<IMesh>(m => Result<IMesh>.Success(m));
        engineMock.Setup(e => e.Evaluators.GetStatistics(It.IsAny<IMesh>()))
            .Returns(Result<MeshStatistics>.Success(new MeshStatistics { MaxZ = 10 }));
        engineMock.Setup(e => e.Evaluators.GetRenderData(It.IsAny<IMesh>()))
            .Returns(Result<RenderData>.Success(new RenderData { Vertices = new double[9], Triangles = new int[3] }));

        var workspace = Workspace.CreateEmpty().AddMesh(mockMesh.Object).Value;
        await vm.ActivateAsync(workspace);

        Assert.True(vm.HasMould);
        Assert.Single(vm.DecalList);
        Assert.Equal(EmbossTarget.Base, vm.DecalList[0].Target);

        // Click "+ Add decal"
        vm.StartPlacingCommand.Execute(null);
        Assert.True(vm.IsPicking);
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);

        // Switch target to Mould
        vm.Target = EmbossTarget.Mould;

        // Previous decal must STILL have Target == Base
        Assert.Equal(EmbossTarget.Base, vm.DecalList[0].Target);
        Assert.Equal("Base", vm.DecalList[0].TargetText);

        // Invoke OnDecalPlaced callback for the new decal
        var onDecalPlacedMethod = typeof(EmbossViewModel).GetMethod("OnDecalPlaced", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        onDecalPlacedMethod!.Invoke(vm, new object[] { new Vector3(0, 0, 20), Vector3.UnitZ });

        Assert.Equal(2, vm.DecalCount);
        Assert.Equal(2, vm.DecalList.Count);

        // Verify first decal remained Base and second decal is Mould
        Assert.Equal(EmbossTarget.Base, vm.DecalList[0].Target);
        Assert.Equal("Base", vm.DecalList[0].TargetText);
        Assert.Contains("Base", vm.DecalList[0].Summary);

        Assert.Equal(EmbossTarget.Mould, vm.DecalList[1].Target);
        Assert.Equal("Mould", vm.DecalList[1].TargetText);
        Assert.Contains("Mould", vm.DecalList[1].Summary);
    }

    [Fact]
    public async Task ClearText_PreservesDecalsAndRevertsToEditMode()
    {
        var (vm, _, engineMock) = CreateViewModel();
        var mockMesh = new Mock<IMesh>();
        mockMesh.Setup(m => m.Vertices).Returns(new Vector3[3]);
        mockMesh.Setup(m => m.Triangles).Returns(new int[3]);
        var metadata = new MeshMetadata().WithId(Guid.NewGuid()).WithName("Test");
        mockMesh.Setup(m => m.Metadata).Returns(metadata);
        mockMesh.Setup(m => m.WithMetadata(It.IsAny<MeshMetadata>()))
            .Returns<MeshMetadata>(meta =>
            {
                var copy = new Mock<IMesh>();
                copy.Setup(x => x.Metadata).Returns(meta);
                copy.Setup(x => x.Vertices).Returns(new Vector3[3]);
                copy.Setup(x => x.Triangles).Returns(new int[3]);
                copy.Setup(x => x.WithMetadata(It.IsAny<MeshMetadata>()))
                    .Returns<MeshMetadata>(m2 => copy.Object);
                return copy.Object;
            });

        engineMock.Setup(e => e.Evaluators.GetStatistics(It.IsAny<IMesh>()))
            .Returns(Result<MeshStatistics>.Success(new MeshStatistics { MaxZ = 10 }));
        engineMock.Setup(e => e.Evaluators.ValidateTopology(It.IsAny<IMesh>()))
            .Returns(Result<TopologyValidation>.Success(new TopologyValidation { IsWatertight = true, IsManifold = true }));
        engineMock.Setup(e => e.Evaluators.GetRenderData(It.IsAny<IMesh>()))
            .Returns(Result<RenderData>.Success(new RenderData { Vertices = new double[9], Triangles = new int[3] }));

        var embossedMeshMock = new Mock<IMesh>();
        embossedMeshMock.Setup(m => m.Vertices).Returns(new Vector3[3]);
        embossedMeshMock.Setup(m => m.Triangles).Returns(new int[3]);
        embossedMeshMock.Setup(m => m.WithMetadata(It.IsAny<MeshMetadata>()))
            .Returns<MeshMetadata>(meta =>
            {
                var copy = new Mock<IMesh>();
                copy.Setup(x => x.Metadata).Returns(meta);
                copy.Setup(x => x.Vertices).Returns(new Vector3[3]);
                copy.Setup(x => x.Triangles).Returns(new int[3]);
                return copy.Object;
            });

        engineMock.Setup(e => e.Booleans.Union(It.IsAny<IMesh>(), It.IsAny<IMesh>()))
            .Returns(Result<IMesh>.Success(embossedMeshMock.Object));

        var workspace = Workspace.CreateEmpty().AddMesh(mockMesh.Object).Value;
        await vm.ActivateAsync(workspace);

        Assert.Equal(1, vm.DecalCount);
        Assert.Equal("FABOLUS", vm.DecalList[0].Text);

        // Apply decals
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.True(vm.IsApplied);
        Assert.False(vm.IsDecalsExpanded);

        // Clear applied decals (reverts baked geometry, keeps decal definitions)
        vm.ClearTextCommand.Execute(null);

        Assert.False(vm.IsApplied);
        Assert.True(vm.IsDecalsExpanded);
        Assert.Equal(1, vm.DecalCount);
        Assert.Single(vm.DecalList);
        Assert.Equal("FABOLUS", vm.DecalList[0].Text);
        Assert.Equal(vm.DecalList[0].Id, vm.SelectedDecalId);
    }

    [Fact]
    public async Task ActivateAsync_WithMould_CalculatesPresetPointsAndAllowsPresetSnapping()
    {
        var (vm, _, engineMock) = CreateViewModel();
        var mockMesh = new Mock<IMesh>();
        mockMesh.Setup(m => m.Vertices).Returns(new Vector3[]
        {
            new(-20, -30, 0),
            new( 20, -30, 0),
            new( 20,  30, 0),
            new(-20,  30, 0),
            new(-20, -30, 50),
            new( 20, -30, 50),
            new( 20,  30, 50),
            new(-20,  30, 50),
        });
        mockMesh.Setup(m => m.Triangles).Returns(new int[]
        {
            0, 1, 5, 0, 5, 4,
            2, 3, 7, 2, 7, 6,
            3, 0, 4, 3, 4, 7,
            1, 2, 6, 1, 6, 5,
            0, 3, 2, 0, 2, 1,
            4, 5, 6, 4, 6, 7
        });

        var mouldDef = new ConcaveMouldDefinition();
        var metadata = new MeshMetadata()
            .WithId(Guid.NewGuid())
            .WithName("MouldMesh")
            .WithBaseMesh(mockMesh.Object)
            .WithMouldDefinition(mouldDef)
            .WithCommand(mouldDef);

        mockMesh.Setup(m => m.Metadata).Returns(metadata);
        mockMesh.Setup(m => m.WithMetadata(It.IsAny<MeshMetadata>()))
            .Returns<MeshMetadata>(meta =>
            {
                var copy = new Mock<IMesh>();
                copy.Setup(x => x.Metadata).Returns(meta);
                copy.Setup(x => x.Vertices).Returns(mockMesh.Object.Vertices);
                copy.Setup(x => x.Triangles).Returns(mockMesh.Object.Triangles);
                return copy.Object;
            });

        engineMock.Setup(e => e.CloneMesh(It.IsAny<IMesh>()))
            .Returns<IMesh>(m => Result<IMesh>.Success(m));
        engineMock.Setup(e => e.Evaluators.GetStatistics(It.IsAny<IMesh>()))
            .Returns(Result<MeshStatistics>.Success(new MeshStatistics
            {
                MinX = -20, MaxX = 20,
                MinY = -30, MaxY = 30,
                MinZ = 0, MaxZ = 50
            }));
        engineMock.Setup(e => e.Evaluators.GetRenderData(It.IsAny<IMesh>()))
            .Returns(Result<RenderData>.Success(new RenderData { Vertices = new double[9], Triangles = new int[3] }));

        var workspace = Workspace.CreateEmpty().AddMesh(mockMesh.Object).Value;
        await vm.ActivateAsync(workspace);

        Assert.True(vm.HasMould);
        Assert.Equal(6, vm.MouldPresetPoints.Count);

        var frontPreset = Assert.Single(vm.MouldPresetPoints, p => p.Name == "Front");
        var curve1Preset = Assert.Single(vm.MouldPresetPoints, p => p.Name == "Curve 1");

        // Apply "Front" preset to current decal
        vm.ApplyPresetByNameCommand.Execute("Front");

        Assert.Equal(EmbossTarget.Mould, vm.Target);
        Assert.Equal(frontPreset.Position, vm.Anchor);
        Assert.Equal(frontPreset.Normal, vm.AnchorNormal);

        // Apply "Curve 1" preset
        vm.ApplyPresetByNameCommand.Execute("Curve 1");
        Assert.Equal(curve1Preset.Position, vm.Anchor);
    }
}
