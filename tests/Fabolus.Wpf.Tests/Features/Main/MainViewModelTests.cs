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
            if (m.Key == UISettings.SplitViewEnabledLabel) {
                m.Reply(false); // mock response
            }
        });

        var mockEngine = new Mock<IGeometryEngine>();
        var mockDialogue = new Mock<IDialogueSystem>();
        var mockAlert = new Mock<IAlertDialog>();

        var viewModel = new MainViewModel(messenger, mockEngine.Object, mockDialogue.Object, mockAlert.Object);

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
