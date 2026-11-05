using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Rotatation;
using Fabolus.Wpf.Features.Viewport;
using HelixToolkit.Wpf.SharpDX;
using Moq;
using Xunit;
using HelixDiffuseMaterial = HelixToolkit.Wpf.SharpDX.DiffuseMaterial;
using HelixMeshGeometry = HelixToolkit.SharpDX.MeshGeometry3D;

namespace Fabolus.Wpf.Tests.Features.Rotatation;

/// <summary>
/// Looking through the mesh in the rotation view, to line it up against the bed and axes.
/// </summary>
/// <remarks>
/// What these pin down is the set of things that all have to agree for it to draw at all: the alpha
/// in the vertex colours, the flag that puts the model in the transparent pass, a material with a
/// shader for that pass (a vertex colour material in it draws nothing), and the backdrop that stops
/// the result being added to the viewport's gradient rather than blended with it.
/// </remarks>
public class RotateSceneOpacityTests
{
    private static readonly IGeometryEngine Engine = global::GeometryEngine.BspGeometryEngine.Create();

    [Fact]
    public void AtFullOpacity_TheMeshIsDrawnPlainly() => UiThread.Run(async () =>
    {
        var scene = await SceneOf(new StrongReferenceMessenger());

        var mesh = scene.Mesh;
        Assert.False(mesh.IsTransparent);
        Assert.IsType<VertColorMaterial>(mesh.Material);
        Assert.All(Colours(mesh), c => Assert.Equal(1f, c.Alpha));
        Assert.Empty(scene.Backdrops);
    });

    [Fact]
    public void LoweringTheOpacity_DrawsTheMeshInTheTransparentPass() => UiThread.Run(async () =>
    {
        var scene = await SceneOf(new StrongReferenceMessenger());

        scene.ViewModel.MeshOpacity = 60f;

        var mesh = scene.Mesh;
        Assert.True(mesh.IsTransparent);
        Assert.All(Colours(mesh), c => Assert.Equal(0.6f, c.Alpha, 3));

        // Unlit, and taking its colour wholly from the vertices, alpha included. Short of that the
        // overhang colours would be blended with the material's own white, or lit twice.
        var skin = Assert.IsType<HelixDiffuseMaterial>(mesh.Material);
        Assert.True(skin.EnableUnLit);
        Assert.Equal(1f, skin.VertexColorBlendingFactor);
    });

    /// <summary>
    /// The colours are the point of this view. Only the alpha may change, or a see-through mesh
    /// would be reporting different overhangs from a solid one.
    /// </summary>
    [Fact]
    public void LoweringTheOpacity_LeavesTheOverhangColoursAlone() => UiThread.Run(async () =>
    {
        var scene = await SceneOf(new StrongReferenceMessenger());
        var solid = Colours(scene.Mesh);

        scene.ViewModel.MeshOpacity = 40f;
        var seeThrough = Colours(scene.Mesh);

        Assert.Equal(solid.Count, seeThrough.Count);
        for (int i = 0; i < solid.Count; i++)
        {
            Assert.Equal(solid[i].Red, seeThrough[i].Red);
            Assert.Equal(solid[i].Green, seeThrough[i].Green);
            Assert.Equal(solid[i].Blue, seeThrough[i].Blue);
        }
    });

    [Fact]
    public void RaisingTheOpacityBackToFull_RestoresThePlainDraw() => UiThread.Run(async () =>
    {
        var scene = await SceneOf(new StrongReferenceMessenger());

        scene.ViewModel.MeshOpacity = 60f;
        scene.ViewModel.MeshOpacity = 100f;

        var mesh = scene.Mesh;
        Assert.False(mesh.IsTransparent);
        Assert.IsType<VertColorMaterial>(mesh.Material);
        Assert.All(Colours(mesh), c => Assert.Equal(1f, c.Alpha));
        Assert.Empty(scene.Backdrops);
    });

    /// <summary>
    /// Without it the viewport's image stays alpha 0 where the mesh is over empty background, and
    /// WPF adds the mesh to the gradient: pale on Graphite, washed to white on Light steel.
    /// </summary>
    [Fact]
    public void WhileSeeThrough_ThereIsExactlyOneBackdrop() => UiThread.Run(async () =>
    {
        var scene = await SceneOf(new StrongReferenceMessenger());

        scene.ViewModel.MeshOpacity = 60f;
        scene.ViewModel.MeshOpacity = 45f;
        scene.ViewModel.XAxisAngle = 20f;

        Assert.Single(scene.Backdrops);
    });

    /// <summary>Changing the viewport background must not leave the old gradient in the scene.</summary>
    [Fact]
    public void ChangingTheViewportBackground_ReplacesTheBackdrop() => UiThread.Run(async () =>
    {
        var messenger = new StrongReferenceMessenger();
        var scene = await SceneOf(messenger);

        scene.ViewModel.MeshOpacity = 60f;
        var before = Assert.Single(scene.Backdrops);

        messenger.SaveSection(GeneralPreferences.Default with { ViewportBackground = ViewportBackground.LightSteel });

        Assert.NotSame(before, Assert.Single(scene.Backdrops));
    });

    /// <summary>General preferences cover far more than this, and most changes are nothing to redraw.</summary>
    [Fact]
    public void ChangingAnotherGeneralPreference_KeepsTheBackdrop() => UiThread.Run(async () =>
    {
        var messenger = new StrongReferenceMessenger();
        var scene = await SceneOf(messenger);

        scene.ViewModel.MeshOpacity = 60f;
        var before = Assert.Single(scene.Backdrops);

        messenger.SaveSection(GeneralPreferences.Default with { ExportFormat = ExportFormat.ThreeMF });

        Assert.Same(before, Assert.Single(scene.Backdrops));
    });

    /// <summary>And changing it while the mesh is solid must not add one.</summary>
    [Fact]
    public void ChangingTheViewportBackgroundWhileSolid_AddsNoBackdrop() => UiThread.Run(async () =>
    {
        var messenger = new StrongReferenceMessenger();
        var scene = await SceneOf(messenger);

        messenger.SaveSection(GeneralPreferences.Default with { ViewportBackground = ViewportBackground.LightSteel });

        Assert.Empty(scene.Backdrops);
    });

    /// <summary>The scene is cleared on activation, so a backdrop from before that is gone.</summary>
    [Fact]
    public void AfterTheSceneIsCleared_TheBackdropComesBackWithTheNextDraw() => UiThread.Run(async () =>
    {
        var scene = await SceneOf(new StrongReferenceMessenger());
        scene.ViewModel.MeshOpacity = 60f;

        scene.ViewModel.SceneManager.OnActivated();
        Assert.Empty(scene.Backdrops);

        scene.ViewModel.MeshOpacity = 50f;
        Assert.Single(scene.Backdrops);
    });

    [Fact]
    public void ThePreviewRotation_StillAppliesToASeeThroughMesh() => UiThread.Run(async () =>
    {
        var scene = await SceneOf(new StrongReferenceMessenger());
        scene.ViewModel.MeshOpacity = 60f;

        scene.ViewModel.XAxisAngle = 30f;

        Assert.True(scene.Mesh.IsTransparent);
        Assert.False(scene.Mesh.Transform.Value.IsIdentity);
    });

    /// <summary>
    /// Committing a rotation rebuilds the mesh. Someone lining a model up takes several goes at it,
    /// and it should not snap back to solid after each one.
    /// </summary>
    [Fact]
    public void CommittingARotation_KeepsTheOpacity() => UiThread.Run(async () =>
    {
        var scene = await SceneOf(new StrongReferenceMessenger());
        scene.ViewModel.MeshOpacity = 60f;

        scene.ViewModel.XAxisAngle = 30f;
        await scene.ViewModel.SaveAxisRotationCommand.ExecuteAsync(null);

        Assert.Equal(60f, scene.ViewModel.MeshOpacity);
        Assert.True(scene.Mesh.IsTransparent);
        Assert.All(Colours(scene.Mesh), c => Assert.Equal(0.6f, c.Alpha, 3));
    });

    [Fact]
    public void ThePreference_SeedsTheOpacityOnActivation() => UiThread.Run(async () =>
    {
        var scene = await SceneOf(MessengerWithStored(RotationPreferences.Default with { MeshOpacity = 70f }));

        Assert.Equal(70f, scene.ViewModel.MeshOpacity);
        Assert.True(scene.Mesh.IsTransparent);
        Assert.All(Colours(scene.Mesh), c => Assert.Equal(0.7f, c.Alpha, 3));
        Assert.Single(scene.Backdrops);
    });

    [Fact]
    public void AStoredOpacityOutOfRange_IsHeldToTheSliderFloor() => UiThread.Run(async () =>
    {
        var scene = await SceneOf(MessengerWithStored(RotationPreferences.Default with { MeshOpacity = 1f }));

        Assert.Equal(RotationPreferences.Ranges.MeshOpacityMin, scene.ViewModel.MeshOpacity);
    });

    [Theory]
    [InlineData(ViewportBackground.Graphite, 0x1A, 0x2D)]
    [InlineData(ViewportBackground.LightSteel, 0xD8, 0xB0)]
    public void TheBackgroundBrush_RunsBetweenTheGradientsColours(ViewportBackground background, byte topRed, byte bottomRed) =>
        UiThread.Run(() =>
        {
            var brush = ViewportBackgroundColours.CreateBrush(background);

            Assert.True(brush.IsFrozen);
            Assert.Equal(topRed, brush.GradientStops[0].Color.R);
            Assert.Equal(bottomRed, brush.GradientStops[1].Color.R);
            return Task.CompletedTask;
        });

    private static StrongReferenceMessenger MessengerWithStored(RotationPreferences stored)
    {
        var messenger = new StrongReferenceMessenger();
        messenger.Register<PreferenceSectionRequestMessage<RotationPreferences>>(
            new object(), (_, request) => request.Reply(stored));
        return messenger;
    }

    private static List<HelixToolkit.Maths.Color4> Colours(MeshGeometryModel3D mesh)
    {
        var colours = ((HelixMeshGeometry)mesh.Geometry!).Colors;
        Assert.NotNull(colours);
        Assert.NotEmpty(colours);
        return colours.ToList();
    }

    private static async Task<Scene> SceneOf(IMessenger messenger)
    {
        var vm = new RotateViewModel(messenger, Mock.Of<IAlertDialog>(), Engine);
        var scene = new Scene(vm);

        // Measured, because the rotation gizmo is sized from the mesh's bounds.
        var box = Engine.Generators.GenerateBox(new Vector3(-20, -30, 0), new Vector3(20, 30, 50)).Value.Measured(Engine);
        await vm.ActivateAsync(Workspace.CreateEmpty().AddMesh(box, MeshRecord.ForImport("box")).Value);

        return scene;
    }

    /// <summary>What the viewport would be holding, going by what the scene manager told it.</summary>
    private sealed class Scene
    {
        private readonly Dictionary<Guid, Element3D> _visuals = new();

        public RotateViewModel ViewModel { get; }

        public Scene(RotateViewModel viewModel)
        {
            ViewModel = viewModel;
            viewModel.SceneManager.VisualAddedOrUpdated += visual => _visuals[visual.GUID] = visual;
            viewModel.SceneManager.VisualRemovedById += id => _visuals.Remove(id);
            viewModel.SceneManager.VisualsCleared += _visuals.Clear;
        }

        public MeshGeometryModel3D Mesh =>
            Assert.Single(_visuals.Values.OfType<MeshGeometryModel3D>(), SceneVisual.GetIsModelGeometry);

        public IReadOnlyList<ScreenQuadModel3D> Backdrops =>
            _visuals.Values.OfType<ScreenQuadModel3D>().ToList();
    }
}
