using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Main;
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
        messenger.Register<MainViewModelTests, AppPreferenceRequestMessage>(this, (r, m) =>
        {
            if (m.Key == UISettings.DefaultImportFolderLabel) m.Reply("");
            else if (m.Key == UISettings.DefaultExportFolderLabel) m.Reply("");
            else if (m.Key == UISettings.DefaultExportFormatLabel) m.Reply("Stl");
            else if (m.Key == UISettings.PrintBedWidthLabel) m.Reply(200f);
            else if (m.Key == UISettings.PrintBedDepthLabel) m.Reply(200f);
            else if (m.Key == UISettings.PrintBedHeightLabel) m.Reply(200f);
            else if (m.Key == UISettings.ShowBedGridLabel) m.Reply(true);
            else if (m.Key == UISettings.AutodetectChannelsLabel) m.Reply(true);
            else if (m.Key == UISettings.ChannelDiameterLabel) m.Reply(4f);
            else if (m.Key == UISettings.ViewportBackgroundLabel) m.Reply("Graphite");
            else if (m.Key == UISettings.UnitsLabel) m.Reply("Millimeters");
            else if (m.Key == UISettings.SplitViewEnabledLabel) m.Reply(false);
            else if (m.Key == UISettings.CutViewEnabledLabel) m.Reply(false);
            else m.Reply(false);
        });

        var mockEngine = new Mock<IGeometryEngine>();
        var mockDialogue = new Mock<IDialogueSystem>();
        var mockAlert = new Mock<IAlertDialog>();

        var viewModel = new MainViewModel(messenger, mockEngine.Object, mockDialogue.Object, mockAlert.Object, null!);

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
}
