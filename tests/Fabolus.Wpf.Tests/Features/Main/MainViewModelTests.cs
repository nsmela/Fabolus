using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.CutSplit;
using Fabolus.Wpf.Features.Main;
using Fabolus.Wpf.Features.MeshManager;
using Fabolus.Wpf.Features.PartingSplit;
using Fabolus.Wpf.Features.Smoothing;
using Fabolus.Wpf.Features.Viewport;
using Moq;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.Main;

public class MainViewModelTests
{
    [Fact]
    public void CaptureScreenshotCommand_SendsCaptureScreenshotMessage()
    {
        // Arrange
        var messenger = new StrongReferenceMessenger();
        var viewModel = CreateViewModel(messenger);

        bool messageReceived = false;
        messenger.Register<CaptureScreenshotMessage>(this, (r, m) =>
        {
            messageReceived = true;
        });

        // Act
        viewModel.CaptureScreenshotCommand.Execute(null);

        // Assert
        Assert.True(messageReceived, "CaptureScreenshotMessage should have been sent.");
    }

    [Fact]
    public void ToggleWireframeCommand_CyclesModeAndToolTip()
    {
        var viewModel = CreateViewModel(new StrongReferenceMessenger());

        Assert.Equal(WireframeMode.None, viewModel.WireframeMode);
        var solidToolTip = viewModel.WireframeToolTip;

        viewModel.ToggleWireframeCommand.Execute(null);
        Assert.Equal(WireframeMode.Overlay, viewModel.WireframeMode);
        var overlayToolTip = viewModel.WireframeToolTip;
        Assert.NotEqual(solidToolTip, overlayToolTip);

        viewModel.ToggleWireframeCommand.Execute(null);
        Assert.Equal(WireframeMode.Only, viewModel.WireframeMode);
        Assert.NotEqual(overlayToolTip, viewModel.WireframeToolTip);

        viewModel.ToggleWireframeCommand.Execute(null);
        Assert.Equal(WireframeMode.None, viewModel.WireframeMode);
        Assert.Equal(solidToolTip, viewModel.WireframeToolTip);
    }

    [Fact]
    public void WireframeMode_RaisesToolTipChange()
    {
        var viewModel = CreateViewModel(new StrongReferenceMessenger());

        var raised = false;
        viewModel.PropertyChanged += (_, e) =>
            raised |= e.PropertyName == nameof(MainViewModel.WireframeToolTip);

        viewModel.ToggleWireframeCommand.Execute(null);

        Assert.True(raised, "WireframeToolTip should notify so the button's tooltip updates.");
    }

    /// <summary>
    /// The info panel is shared, but what is on it belongs to the view that published it. Swapping
    /// a view in empties it, so a view that publishes nothing shows nothing rather than inheriting
    /// the last view's numbers.
    /// </summary>
    [Fact]
    public async Task SwitchingView_EmptiesTheMeshInfoPanel()
    {
        var messenger = new StrongReferenceMessenger();
        var viewModel = CreateViewModel(messenger);

        // The constructor starts a fire-and-forget switch to the mesh manager, which publishes to
        // the panel itself. Let it settle first, or this races it and passes either way.
        await SettledOnMeshManager(viewModel);

        // What the outgoing view had published. Given a moment to stand, so that a straggling
        // publish from the activation above shows up as this assert failing rather than as the
        // final one passing for the wrong reason.
        messenger.Send(new UpdateMeshInfoMessage([new TextInfoItem { Label = "Volume", Value = "12 mL" }]));
        await Task.Delay(50);
        Assert.Single(viewModel.InfoViewModel.InfoItems);

        // The switch itself: assigning the property is what every SwitchTo...Async does, and the
        // clear rides on it rather than on any one view.
        viewModel.CurrentView = new Mock<IViewState>().Object;

        Assert.Empty(viewModel.InfoViewModel.InfoItems);
    }

    private static readonly IGeometryEngine Engine = global::GeometryEngine.BspGeometryEngine.Create();

    private static IMesh Box(double size) =>
        Engine.Generators.GenerateBox(new Vector3(0, 0, 0), new Vector3(size, size, size)).Value;

    [Fact]
    public async Task ConfirmClose_WithNothingLoaded_ClosesWithoutAsking()
    {
        var dialogue = new Mock<IDialogueSystem>();
        var viewModel = await SettledOn(Workspace.CreateEmpty(), new StrongReferenceMessenger(), dialogue);

        Assert.True(viewModel.ConfirmClose());
        dialogue.Verify(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmClose_AfterOnlyImporting_ClosesWithoutAsking()
    {
        var messenger = new StrongReferenceMessenger();
        var record = MeshRecord.ForImport("bolus");
        var workspace = Workspace.CreateEmpty().AddMesh(Box(10), record).Value;

        var dialogue = new Mock<IDialogueSystem>();
        var viewModel = await SettledOn(workspace, messenger, dialogue);
        messenger.Send(new MeshesSavedMessage(workspace, [record.Id]));

        Assert.True(viewModel.ConfirmClose());
        dialogue.Verify(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// The change is read off the current view, not the main view model's copy of the workspace:
    /// most views only hand their workspace back on deactivation, which closing never reaches.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConfirmClose_WithAChangeNotExported_AsksAndFollowsTheAnswer(bool closeAnyway)
    {
        var messenger = new StrongReferenceMessenger();
        var record = MeshRecord.ForImport("ear_bolus");
        var imported = Workspace.CreateEmpty().AddMesh(Box(10), record).Value;
        var edited = imported.UpdateMesh(record.Id, Box(12), record).Value;

        var dialogue = new Mock<IDialogueSystem>();
        dialogue.Setup(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>())).Returns(closeAnyway);

        var viewModel = await SettledOn(edited, messenger, dialogue);
        messenger.Send(new MeshesSavedMessage(imported, [record.Id]));

        Assert.Equal(closeAnyway, viewModel.ConfirmClose());
        dialogue.Verify(d => d.ShowConfirmation(
            It.IsAny<string>(), It.Is<string>(message => message.Contains("ear_bolus"))), Times.Once);
    }

    /// <summary>
    /// Leaving a view while it is still opening (#89). The parting view spends seconds off the UI
    /// thread working out the body before it builds its scene, and the tabs stay clickable all that
    /// time. A switch that deactivated it then cleared the meshes that work resumed into, which
    /// threw - and closed the app before the crash handler existed. The tab bar is disabled while a
    /// switch runs, and a switch asked for anyway (from code) is dropped rather than let through.
    /// </summary>
    [Fact]
    public void SwitchingAwayWhileAViewIsStillOpening_IsDroppedAndDoesNotThrow() => UiThread.Run(async () =>
    {
        var engine = global::GeometryEngine.BspGeometryEngine.Create();
        var messenger = new StrongReferenceMessenger();
        var viewModel = new MainViewModel(
            messenger, engine, new Mock<IDialogueSystem>().Object, new Mock<IAlertDialog>().Object);
        await SettledOnMeshManager(viewModel);

        // The parting view is off by default.
        messenger.Send(new PreferenceSectionUpdateMessage<CutSplitPreferences>(
            CutSplitPreferences.Default with { SplitViewEnabled = true }));

        // Handed over the way a view hands its workspace on: by being deactivated.
        var moulded = MouldedWorkspace(engine, "eye_bolus.stl");
        var current = new Mock<IViewState>();
        current.Setup(v => v.DeactivateAsync()).ReturnsAsync(moulded);
        viewModel.CurrentView = current.Object;

        // Not awaited: the second click arrives while the first view is still opening.
        var opening = viewModel.ShowPartingSplitCommand.ExecuteAsync(null);
        Assert.False(opening.IsCompleted, "the parting view opened synchronously, so nothing is racing");
        Assert.False(viewModel.CanSwitchViews, "the tab bar stays enabled while a view is opening");

        var leaving = viewModel.SwitchToMouldViewCommand.ExecuteAsync(null);
        Assert.True(leaving.IsCompleted, "a switch during a switch should be dropped, not queued");

        await opening;

        Assert.IsType<PartingSplitViewModel>(viewModel.CurrentView);
        Assert.True(viewModel.CanSwitchViews, "the switch finished but left the tab bar disabled");
        Assert.False(viewModel.IsLoading, "the switch finished but left the loading overlay up");
    });

    /// <summary>A workspace whose active mesh is a convex mould generated on the named scan.</summary>
    private static Workspace MouldedWorkspace(IGeometryEngine engine, string file)
    {
        var imported = engine.IO.Import(AssetPath(file));
        Assert.True(imported.IsSuccess, imported.IsFailure ? imported.Error.Description : "");

        var record = MeshRecord.ForImport(file).WithBaseMesh(imported.Value);
        var workspace = Workspace.CreateEmpty().AddMesh(imported.Value, record).Value;

        var moulded = new GenerateMould(engine).Execute(
            workspace, record.Id, new ConvexMouldDefinition(3.0, 3.0, 3.0) { TargetMeshId = record.Id });
        Assert.True(moulded.IsSuccess, moulded.IsFailure ? moulded.Error.Description : "");
        return moulded.Value;
    }

    private static string AssetPath(string name)
    {
        // Walked up to rather than reached by a fixed path: CI's x64 output sits a level deeper.
        var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = System.IO.Path.Combine(directory.FullName, "files", name);
            if (System.IO.File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new System.IO.FileNotFoundException($"Could not find tests/files/{name}.");
    }

    /// <summary>
    /// A main view model whose startup switch has finished, with a stand-in view holding
    /// <paramref name="workspace"/> as the current one.
    /// </summary>
    private static async Task<MainViewModel> SettledOn(
        Workspace workspace, IMessenger messenger, Mock<IDialogueSystem> dialogue)
    {
        var viewModel = new MainViewModel(
            messenger, new Mock<IGeometryEngine>().Object, dialogue.Object, new Mock<IAlertDialog>().Object);
        await SettledOnMeshManager(viewModel);

        var view = new Mock<IViewState>();
        view.Setup(v => v.CurrentWorkspace).Returns(workspace);
        viewModel.CurrentView = view.Object;

        return viewModel;
    }

    /// <summary>
    /// Waits for the constructor's fire-and-forget view switch to run all the way through. The
    /// view is assigned before its ActivateAsync is awaited, so waiting on CurrentView alone
    /// returns while the activation - and its publishing - is still to come; IsLoading goes false
    /// only once the switch is finished.
    /// </summary>
    private static async Task SettledOnMeshManager(MainViewModel viewModel)
    {
        for (int i = 0; i < 200 && (viewModel.CurrentView is not MeshManagerViewModel || viewModel.IsLoading); i++)
        {
            await Task.Delay(10);
        }

        Assert.IsType<MeshManagerViewModel>(viewModel.CurrentView);
        Assert.False(viewModel.IsLoading, "the startup view switch never finished");
    }

    private static MainViewModel CreateViewModel(IMessenger messenger)
    {
        // No preference store is registered, so every section request falls back to that
        // section's Default - which is exactly the set this stub used to spell out by hand.
        var mockEngine = new Mock<IGeometryEngine>();
        var mockDialogue = new Mock<IDialogueSystem>();
        var mockAlert = new Mock<IAlertDialog>();

        return new MainViewModel(messenger, mockEngine.Object, mockDialogue.Object, mockAlert.Object);
    }
}
