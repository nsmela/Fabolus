using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Main;
using Fabolus.Wpf.Features.MeshManager;
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
