using System.Numerics;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Decal;
using Moq;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.Emboss;

public sealed class TestOutlineSource : IGlyphOutlineSource
{
    public Result<IReadOnlyList<Polygon2D>> GetOutlines(string text, DecalFont font, float capHeight, float tracking)
    {
        return Result.Success<IReadOnlyList<Polygon2D>>(new List<Polygon2D>
        {
            new()
            {
                OuterBoundary = new List<Vector2>
                {
                    new(-5, -3), new(5, -3), new(5, 3), new(-5, 3)
                }
            }
        });
    }

    public TextMetrics MeasureText(string text, DecalFont font, float capHeight, float tracking)
    {
        return new TextMetrics(10f, capHeight, new float[] { 10f });
    }
}

public class EmbossViewModelTests
{
    private static (DecalViewModel vm, IMessenger messenger, Mock<IGeometryEngine> engineMock) CreateViewModel()
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
        engineMock.Setup(e => e.Evaluators.GetStatistics(It.IsAny<IMesh>()))
            .Returns(Result<MeshStatistics>.Success(new MeshStatistics { MinX = -10, MaxX = 10, MinY = -10, MaxY = 10, MinZ = -10, MaxZ = 10 }));
        engineMock.Setup(e => e.Evaluators.ValidateTopology(It.IsAny<IMesh>()))
            .Returns(Result<TopologyValidation>.Success(new TopologyValidation { IsManifold = true, IsWatertight = true }));
        engineMock.Setup(e => e.Evaluators.Raycast(It.IsAny<IMesh>(), It.IsAny<Vector3>(), It.IsAny<Vector3>()))
            .Returns<IMesh, Vector3, Vector3>((m, o, d) => Result<RaycastHit>.Success(new RaycastHit(o + d * 10f, -d, 10f)));
        engineMock.Setup(e => e.Booleans.Union(It.IsAny<IMesh>(), It.IsAny<IMesh>()))
            .Returns<IMesh, IMesh>((a, b) => Result<IMesh>.Success(a));
        engineMock.Setup(e => e.Booleans.Subtract(It.IsAny<IMesh>(), It.IsAny<IMesh>(), It.IsAny<Vector3>()))
            .Returns<IMesh, IMesh, Vector3>((a, b, shift) => Result<IMesh>.Success(a));

        var vm = new DecalViewModel(messenger, alertMock.Object, engineMock.Object, outlineSource);
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
    public void AddDecalCommand_AddsNewDecal()
    {
        var (vm, _, _) = CreateViewModel();

        Assert.Equal(0, vm.DecalCount);

        vm.AddDecalCommand.Execute(null);
        Assert.Equal(1, vm.DecalCount);

        vm.AddDecalCommand.Execute(null);
        Assert.Equal(2, vm.DecalCount);
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
    public async Task ActivateAsync_WithImportedDecalCommand_InheritsDecalsAndSetsIsAppliedTrue()
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
        var command = new DecalCommand(new[] { decal });
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

        engineMock.Setup(e => e.CloneMesh(It.IsAny<IMesh>()))
            .Returns<IMesh>(m => Result<IMesh>.Success(m));
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
        Assert.Equal("Applied", vm.StatusWord);
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);
        Assert.False(vm.HasSelectedDecal);

        vm.SelectDecalCommand.Execute(vm.Decals[0].Id);
        Assert.Equal("IMPORTED", vm.LabelText);
        Assert.Equal(7.5f, vm.CapHeight);
        Assert.Equal(1.2f, vm.Depth);
        Assert.Equal(EmbossOperation.Engrave, vm.Operation);
        Assert.Equal(30, vm.Rotation);
        Assert.Equal("Preview", vm.StatusWord);
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
        engineMock.Setup(e => e.Booleans.Subtract(It.IsAny<IMesh>(), It.IsAny<IMesh>(), It.IsAny<Vector3>()))
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
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);

        // Select decal first
        vm.SelectDecalCommand.Execute(vm.Decals[0].Id);
        Assert.Equal(vm.Decals[0].Id, vm.SelectedDecalId);

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

        var metadata = new MeshMetadata().WithId(Guid.NewGuid()).WithName("TestMesh");
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
    }

    [Fact]
    public async Task DecalList_SyncsWithDecalsAndSelection()
    {
        var (vm, _, engineMock) = CreateViewModel();
        var mockMesh = new Mock<IMesh>();
        mockMesh.Setup(m => m.Vertices).Returns(new Vector3[3]);
        mockMesh.Setup(m => m.Triangles).Returns(new int[3]);

        var metadata = new MeshMetadata().WithId(Guid.NewGuid()).WithName("TestMesh");
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
        Assert.False(vm.DecalList[0].IsSelected);

        // Select decal
        vm.SelectDecalCommand.Execute(vm.DecalList[0].Id);
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
        engineMock.Setup(e => e.Booleans.Subtract(It.IsAny<IMesh>(), It.IsAny<IMesh>(), It.IsAny<Vector3>()))
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
            .WithCommand(new DecalCommand(new[] { decal1 }))
            .WithMouldDefinition(mouldDef)
            .WithCommand(new MouldDecalCommand(new[] { decal2 }));

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

        Assert.Equal(EmbossTarget.Mould, vm.DecalList[1].Target);
        Assert.Equal("Mould", vm.DecalList[1].TargetText);
        Assert.True(vm.DecalList[1].HasMould);
    }

    [Fact]
    public async Task AddDecal_SwitchingTargetToMould_PreservesPreviousBaseDecalTarget()
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
        Assert.Equal(2, vm.DecalList.Count);
        Assert.Equal(EmbossTarget.Mould, vm.DecalList[0].Target);
        Assert.Equal(EmbossTarget.Mould, vm.DecalList[1].Target);

        // Click "+ Add decal"
        vm.AddDecalCommand.Execute(null);

        Assert.Equal(3, vm.DecalCount);
        Assert.Equal(3, vm.DecalList.Count);
        Assert.Equal(vm.DecalList[2].Id, vm.SelectedDecalId);

        // Switch target of newly added decal to Base
        vm.Target = EmbossTarget.Base;

        // Verify first decal remained Mould and 3rd decal is Base
        Assert.Equal(EmbossTarget.Mould, vm.DecalList[0].Target);
        Assert.Equal("Mould", vm.DecalList[0].TargetText);

        Assert.Equal(EmbossTarget.Base, vm.DecalList[2].Target);
        Assert.Equal("Base", vm.DecalList[2].TargetText);
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
        engineMock.Setup(e => e.Booleans.Subtract(It.IsAny<IMesh>(), It.IsAny<IMesh>(), It.IsAny<Vector3>()))
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
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);
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
        engineMock.Setup(e => e.Evaluators.ValidateTopology(It.IsAny<IMesh>()))
            .Returns(Result<TopologyValidation>.Success(new TopologyValidation { IsManifold = true }));
        engineMock.Setup(e => e.Polygons.GetMeshShadow(It.IsAny<IMesh>()))
            .Returns(Result<Polygon2D>.Success(new Polygon2D { OuterBoundary = new Vector2[] { new(-20, -30), new(20, -30), new(20, 30), new(-20, 30) } }));
        engineMock.Setup(e => e.Polygons.OffsetPolygon(It.IsAny<Polygon2D>(), It.IsAny<float>()))
            .Returns<Polygon2D, float>((p, _) => Result<Polygon2D>.Success(p));
        engineMock.Setup(e => e.Polygons.ExtrudePolygon(It.IsAny<Polygon2D>(), It.IsAny<float>(), It.IsAny<float>()))
            .Returns(Result<IMesh>.Success(mockMesh.Object));
        engineMock.Setup(e => e.Booleans.Subtract(It.IsAny<IMesh>(), It.IsAny<IMesh>(), It.IsAny<Vector3>()))
            .Returns(Result<IMesh>.Success(mockMesh.Object));

        var workspace = Workspace.CreateEmpty().AddMesh(mockMesh.Object).Value;
        await vm.ActivateAsync(workspace);

        Assert.True(vm.HasMould);
        Assert.Equal(6, vm.MouldPresetPoints.Count);
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);
        Assert.False(vm.HasSelectedDecal);

        var frontPreset = Assert.Single(vm.MouldPresetPoints, p => p.Name == "Front");
        var curve1Preset = Assert.Single(vm.MouldPresetPoints, p => p.Name == "Curve 1");

        vm.Target = EmbossTarget.Mould;

        // Apply "Front" preset when no decal is selected -> selects Front decal (which already exists at Front)
        vm.ApplyPresetByNameCommand.Execute("Front");

        Assert.Equal(EmbossTarget.Mould, vm.Target);
        Assert.Equal(frontPreset.Position, vm.Anchor);
        Assert.Equal(frontPreset.Normal, vm.AnchorNormal);
        Assert.Equal(0, vm.Rotation);
        Assert.True(vm.CapHeight > 0f && vm.CapHeight <= 10.0f);
        Assert.True(vm.HasSelectedDecal);

        // Apply "Curve 1" preset (vertical)
        vm.ApplyPresetByNameCommand.Execute("Curve 1");
        Assert.Equal(curve1Preset.Position, vm.Anchor);
        Assert.Equal(90, vm.Rotation);

        // Apply decals with mould
        var embossedMock = new Mock<IMesh>();
        embossedMock.Setup(m => m.Vertices).Returns(mockMesh.Object.Vertices);
        embossedMock.Setup(m => m.Triangles).Returns(mockMesh.Object.Triangles);
        embossedMock.Setup(m => m.WithMetadata(It.IsAny<MeshMetadata>()))
            .Returns<MeshMetadata>(meta =>
            {
                var copy = new Mock<IMesh>();
                copy.Setup(x => x.Metadata).Returns(meta);
                copy.Setup(x => x.Vertices).Returns(mockMesh.Object.Vertices);
                copy.Setup(x => x.Triangles).Returns(mockMesh.Object.Triangles);
                return copy.Object;
            });

        engineMock.Setup(e => e.Booleans.Union(It.IsAny<IMesh>(), It.IsAny<IMesh>()))
            .Returns(Result<IMesh>.Success(embossedMock.Object));

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.True(vm.IsApplied);
        Assert.False(vm.IsDecalsExpanded);

        // Clear reverts to edit mode and removes translucent overlay
        vm.ClearTextCommand.Execute(null);
        Assert.False(vm.IsApplied);
        Assert.True(vm.IsDecalsExpanded);
    }

    [Fact]
    public async Task ActivateAsync_WithBaseMesh_CalculatesBasePresetPointsAndAllowsTopFrontBackSnapping()
    {
        var (vm, messenger, engineMock) = CreateViewModel();

        var mockMesh = new Mock<IMesh>();
        mockMesh.Setup(m => m.Vertices).Returns(new Vector3[]
        {
            new(-20, -30,  0),
            new( 20, -30,  0),
            new( 20,  30,  0),
            new(-20,  30,  0),
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

        var metadata = new MeshMetadata().WithId(Guid.NewGuid()).WithName("BaseMesh");
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

        Assert.False(vm.HasMould);
        Assert.Equal(EmbossTarget.Base, vm.Target);
        Assert.Equal(3, vm.BasePresetPoints.Count);
        Assert.Equal(3, vm.ActivePresetPoints.Count);

        var topPreset = Assert.Single(vm.BasePresetPoints, p => p.Name == "Top");
        var frontPreset = Assert.Single(vm.BasePresetPoints, p => p.Name == "Front");
        var backPreset = Assert.Single(vm.BasePresetPoints, p => p.Name == "Back");

        Assert.Equal(0, topPreset.RotationDeg);
        Assert.Equal(0, frontPreset.RotationDeg);
        Assert.Equal(0, backPreset.RotationDeg);

        // Apply "Top" preset
        vm.ApplyPresetByNameCommand.Execute("Top");
        Assert.Equal(EmbossTarget.Base, vm.Target);
        Assert.Equal(topPreset.Position, vm.Anchor);
        Assert.Equal(topPreset.Normal, vm.AnchorNormal);
        Assert.Equal(0, vm.Rotation);
        Assert.True(vm.CapHeight > 0f && vm.CapHeight <= 10.0f);

        // Apply "Front" preset
        vm.ApplyPresetByNameCommand.Execute("Front");
        Assert.Equal(EmbossTarget.Base, vm.Target);
        Assert.Equal(frontPreset.Position, vm.Anchor);
        Assert.Equal(0, vm.Rotation);
    }

    [Fact]
    public async Task AddDecal_GeneratesOnFirstFreeAnchorInViewedTarget()
    {
        var (vm, messenger, engineMock) = CreateViewModel();

        var mockMesh = new Mock<IMesh>();
        mockMesh.Setup(m => m.Vertices).Returns(new Vector3[]
        {
            new(-20, -30,  0),
            new( 20, -30,  0),
            new( 20,  30,  0),
            new(-20,  30,  0),
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

        // Initial decals start on Mould target at Front (file name) and Back (volume)
        Assert.True(vm.HasMould);
        Assert.Equal(EmbossTarget.Mould, vm.Target);
        Assert.Equal(2, vm.DecalCount);
        var frontPreset = vm.MouldPresetPoints.First(p => p.Name == "Front");
        var backPreset = vm.MouldPresetPoints.First(p => p.Name == "Back");
        Assert.Equal(frontPreset.Position, vm.Decals[0].Anchor);
        Assert.Equal(EmbossTarget.Mould, vm.Decals[0].Target);
        Assert.Equal("MouldMesh", vm.Decals[0].Text);

        Assert.Equal(backPreset.Position, vm.Decals[1].Anchor);
        Assert.Equal(EmbossTarget.Mould, vm.Decals[1].Target);
        Assert.Contains("cc", vm.Decals[1].Text);

        // Add 3rd decal -> should place at Left (next free anchor on Mould target)
        vm.AddDecalCommand.Execute(null);
        Assert.Equal(3, vm.DecalCount);
        var leftPreset = vm.MouldPresetPoints.First(p => p.Name == "Left");
        Assert.Equal(leftPreset.Position, vm.Decals[2].Anchor);
        Assert.Equal(EmbossTarget.Mould, vm.Decals[2].Target);

        // Switch to Base target and add decal -> should place at Top (first free anchor on Base target)
        vm.Target = EmbossTarget.Base;
        vm.AddDecalCommand.Execute(null);
        Assert.Equal(4, vm.DecalCount);
        var topPreset = vm.BasePresetPoints.First(p => p.Name == "Top");
        Assert.Equal(topPreset.Position, vm.Decals[3].Anchor);
        Assert.Equal(EmbossTarget.Base, vm.Decals[3].Target);
    }

    [Fact]
    public async Task SelectionMode_DefaultsToNoSelection_AndAllowsSwitchingAndDeselecting()
    {
        var (vm, _, engineMock) = CreateViewModel();

        var mockMesh = new Mock<IMesh>();
        mockMesh.Setup(m => m.Vertices).Returns(new Vector3[]
        {
            new(-20, -30,  0),
            new( 20, -30,  0),
            new(  0,  30, 50)
        });
        mockMesh.Setup(m => m.Triangles).Returns(new int[] { 0, 1, 2 });
        var metadata = new MeshMetadata().WithId(Guid.NewGuid()).WithName("TestMesh");
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
            .Returns(Result<MeshStatistics>.Success(new MeshStatistics { MaxZ = 50 }));
        engineMock.Setup(e => e.Evaluators.GetRenderData(It.IsAny<IMesh>()))
            .Returns(Result<RenderData>.Success(new RenderData { Vertices = new double[9], Triangles = new int[3] }));

        var workspace = Workspace.CreateEmpty().AddMesh(mockMesh.Object).Value;
        await vm.ActivateAsync(workspace);

        // Verify default on activation: NO decal selected
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);
        Assert.False(vm.HasSelectedDecal);
        Assert.Single(vm.Decals);
        Assert.False(vm.DecalList[0].IsSelected);

        // Add 2nd decal -> becomes selected
        vm.AddDecalCommand.Execute(null);
        Assert.Equal(2, vm.DecalCount);
        var secondDecalId = vm.Decals[1].Id;
        Assert.Equal(secondDecalId, vm.SelectedDecalId);
        Assert.True(vm.HasSelectedDecal);
        Assert.False(vm.DecalList[0].IsSelected);
        Assert.True(vm.DecalList[1].IsSelected);

        // Switch selection to 1st decal
        var firstDecalId = vm.Decals[0].Id;
        vm.SelectDecalCommand.Execute(firstDecalId);
        Assert.Equal(firstDecalId, vm.SelectedDecalId);
        Assert.True(vm.HasSelectedDecal);
        Assert.True(vm.DecalList[0].IsSelected);
        Assert.False(vm.DecalList[1].IsSelected);

        // Deselect by setting SelectedDecalId to Guid.Empty (e.g. from viewport click on background/mesh)
        vm.SelectedDecalId = Guid.Empty;
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);
        Assert.False(vm.HasSelectedDecal);
        Assert.False(vm.DecalList[0].IsSelected);
        Assert.False(vm.DecalList[1].IsSelected);
    }

    [Fact]
    public async Task SwitchingTarget_DoesNotRequireSelectedDecal()
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
        engineMock.Setup(e => e.Evaluators.ValidateTopology(It.IsAny<IMesh>()))
            .Returns(Result<TopologyValidation>.Success(new TopologyValidation { IsManifold = true }));
        engineMock.Setup(e => e.Polygons.GetMeshShadow(It.IsAny<IMesh>()))
            .Returns(Result<Polygon2D>.Success(new Polygon2D { OuterBoundary = new Vector2[] { new(-20, -30), new(20, -30), new(20, 30), new(-20, 30) } }));
        engineMock.Setup(e => e.Polygons.OffsetPolygon(It.IsAny<Polygon2D>(), It.IsAny<float>()))
            .Returns<Polygon2D, float>((p, _) => Result<Polygon2D>.Success(p));
        engineMock.Setup(e => e.Polygons.ExtrudePolygon(It.IsAny<Polygon2D>(), It.IsAny<float>(), It.IsAny<float>()))
            .Returns(Result<IMesh>.Success(mockMesh.Object));
        engineMock.Setup(e => e.Booleans.Subtract(It.IsAny<IMesh>(), It.IsAny<IMesh>(), It.IsAny<Vector3>()))
            .Returns(Result<IMesh>.Success(mockMesh.Object));

        var workspace = Workspace.CreateEmpty().AddMesh(mockMesh.Object).Value;
        await vm.ActivateAsync(workspace);

        // Starts with Mould target, NO decal selected
        Assert.True(vm.HasMould);
        Assert.Equal(EmbossTarget.Mould, vm.Target);
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);
        Assert.False(vm.HasSelectedDecal);

        // Switch to Base without selecting any decal
        vm.Target = EmbossTarget.Base;
        Assert.Equal(EmbossTarget.Base, vm.Target);
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);
        Assert.False(vm.HasSelectedDecal);

        // Switch back to Mould without selecting any decal
        vm.Target = EmbossTarget.Mould;
        Assert.Equal(EmbossTarget.Mould, vm.Target);
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);
        Assert.False(vm.HasSelectedDecal);
    }

    [Fact]
    public async Task DraggingDecal_UpdatesPositionAndCompletesOnDragFinish()
    {
        var (vm, outlineMock, engineMock) = CreateViewModel();

        var mockMesh = new Mock<IMesh>();
        mockMesh.Setup(m => m.Vertices).Returns(new Vector3[3]);
        mockMesh.Setup(m => m.Triangles).Returns(new int[3]);
        mockMesh.Setup(m => m.Metadata).Returns(new MeshMetadata().WithId(Guid.NewGuid()).WithName("Test"));
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

        var decal = vm.DecalList[0];
        vm.SelectedDecalId = decal.Id;

        var sceneManager = (DecalSceneManager)vm.SceneManager;

        // Simulate decal move
        var newPos = new Vector3(15, 25, 35);
        var newNorm = new Vector3(0, 0, 1);
        
        // Raising DecalMoved
        var movedMethod = typeof(DecalViewModel).GetMethod("OnDecalMoved", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        movedMethod!.Invoke(vm, new object[] { decal.Id, newPos, newNorm });

        Assert.Equal(newPos, vm.Anchor);
        Assert.Equal(newNorm, vm.AnchorNormal);

        // Raising DecalDragCompleted
        var dragCompletedMethod = typeof(DecalViewModel).GetMethod("OnDecalDragCompleted", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        dragCompletedMethod!.Invoke(vm, new object[] { decal.Id });

        Assert.Equal(newPos, vm.Anchor);
    }
}
