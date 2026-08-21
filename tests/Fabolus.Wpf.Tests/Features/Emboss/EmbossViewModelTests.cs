using System.Numerics;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Features.Emboss;
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

        var vm = new EmbossViewModel(messenger, alertMock.Object, engineMock.Object, outlineSource);
        return (vm, messenger, engineMock);
    }

    [Fact]
    public void Target_ChangingToMould_SetsMirrorTrue()
    {
        var (vm, _, _) = CreateViewModel();

        vm.Target = EmbossTarget.Base;
        Assert.False(vm.Mirror);

        vm.Target = EmbossTarget.Mould;
        Assert.True(vm.Mirror);

        vm.Target = EmbossTarget.Base;
        Assert.False(vm.Mirror);
    }

    [Fact]
    public void Operation_ChangingToEngrave_UpdatesDepthLabelAndApplyLabel()
    {
        var (vm, _, _) = CreateViewModel();

        vm.Operation = EmbossOperation.Emboss;
        Assert.Equal("Height", vm.DepthLabel);
        Assert.Equal("Apply emboss", vm.ApplyLabel);

        vm.Operation = EmbossOperation.Engrave;
        Assert.Equal("Depth", vm.DepthLabel);
        Assert.Equal("Apply engraving", vm.ApplyLabel);
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
    public void ResetCommand_ResetsRotation()
    {
        var (vm, _, _) = CreateViewModel();

        vm.Rotation = 45;
        vm.CapHeight = 10f;
        vm.ResetCommand.Execute(null);

        Assert.Equal(0, vm.Rotation);
        Assert.Equal(6.0f, vm.CapHeight);
        Assert.Equal("FABOLUS", vm.LabelText);
    }

    [Fact]
    public void ClearCommand_WhenNotApplied_DoesNothing()
    {
        var (vm, _, _) = CreateViewModel();
        Assert.False(vm.IsApplied);

        vm.ClearCommand.Execute(null);
        Assert.False(vm.IsApplied);
    }

    [Fact]
    public async Task ActivateAsync_WithImportedTextEmbossCommand_InheritsDecalAndSetsIsAppliedTrue()
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
        var command = new TextEmbossCommand(decal);
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
}
