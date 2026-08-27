using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Main;
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

    private static MainViewModel CreateViewModel(IMessenger messenger)
    {
        // PreferencesViewModel requests every preference up front and casts each response,
        // so the stub has to answer all of them with the declared default and exact type.
        var preferences = new Dictionary<string, object> {
            [UISettings.DefaultImportFolderLabel] = string.Empty,
            [UISettings.DefaultExportFolderLabel] = string.Empty,
            [UISettings.DefaultExportFormatLabel] = ExportFormat.Stl.ToString(),
            [UISettings.PrintBedWidthLabel] = 250.0f,
            [UISettings.PrintBedDepthLabel] = 250.0f,
            [UISettings.ShowBedGridLabel] = true,
            [UISettings.AutodetectChannelsLabel] = true,
            [UISettings.ChannelDiameterLabel] = 4.0f,
            [UISettings.ViewportBackgroundLabel] = ViewportBackground.Graphite.ToString(),
            [UISettings.SplitViewEnabledLabel] = false,
            [UISettings.CutViewEnabledLabel] = false,
            [UISettings.DecalsEnabledLabel] = true,
            [UISettings.DecalAutoPlaceScopeLabel] = DecalAutoPlaceScope.Mould.ToString(),
            [UISettings.DecalAutoPlaceFilenameLabel] = true,
            [UISettings.DecalFilenameAnchorLabel] = DecalAnchor.Front.ToString(),
            [UISettings.DecalAutoPlaceVolumeLabel] = true,
            [UISettings.DecalVolumeAnchorLabel] = DecalAnchor.Back.ToString(),
            [UISettings.DecalDefaultFontLabel] = DecalFont.Sans.ToString(),
            [UISettings.DecalDefaultCapHeightLabel] = 6.0f,
            [UISettings.DecalDefaultDepthLabel] = 0.8f,
            [UISettings.DecalDefaultOperationLabel] = EmbossOperation.Engrave.ToString(),
            [UISettings.SmoothIterationsLabel] = 1,
            [UISettings.SmoothIntensityLabel] = 1.5f,
            [UISettings.SmoothInflationLabel] = 0.2f,
            [UISettings.SmoothRemeshRatioLabel] = 1.0f,
            [UISettings.SmoothResolutionLabel] = 1.0f,
            [UISettings.SmoothDisplayModeLabel] = SmoothDisplayMode.None.ToString(),
            [UISettings.OverhangWarningAngleLabel] = 45.0f,
            [UISettings.OverhangCriticalAngleLabel] = 65.0f,
            [UISettings.CutViewScopeLabel] = CutViewScope.Base.ToString(),
            [UISettings.MouldShapeLabel] = MouldShapeType.Concave.ToString(),
            [UISettings.MouldWallThicknessLabel] = 2.0f,
            [UISettings.MouldBaseHeightLabel] = 5.0f,
            [UISettings.MouldTroughHeightLabel] = 0.0f,
            [UISettings.MouldTroughOffsetLabel] = 2.5f,
            [UISettings.MouldTroughShapeLabel] = TroughShapeType.Footprint.ToString(),
        };

        messenger.Register<Dictionary<string, object>, AppPreferenceRequestMessage>(preferences, (r, m) => m.Reply(r[m.Key]));

        var mockEngine = new Mock<IGeometryEngine>();
        var mockDialogue = new Mock<IDialogueSystem>();
        var mockAlert = new Mock<IAlertDialog>();
        var appPreferences = new AppPreferencesStore();

        return new MainViewModel(messenger, mockEngine.Object, mockDialogue.Object, mockAlert.Object, appPreferences);
    }
}
