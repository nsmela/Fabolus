using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.AirChannel;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Features.Channels;
using Fabolus.Wpf.Pages.Channels.Straight;

namespace Fabolus.Wpf.Pages.Channels;

public partial class ChannelsViewModel : BaseViewModel {
    public override string TitleText => "channels";

    public override SceneManager GetSceneManager => new ChannelsSceneManager();

    [ObservableProperty] private BaseChannelsViewModel _currentChannelViewModel = new StraightChannelsViewModel();
    [ObservableProperty] private ChannelTypes[] Types = [
        ChannelTypes.Straight,
        ChannelTypes.AngledHead,
        ChannelTypes.Path,
    ];

    private AirChannel _previewChannel;
    private AirChannel[] _channelsList = [];

    public ChannelsViewModel() {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<AirChannelsUpdatedMessage>(this, async (r, m) => await ChannelsUpdated(m.channels));
        WeakReferenceMessenger.Default.Register<ChannelSettingsUpdatedMessage>(this, async (r, m) => await PreviewUpdated(m.settings));

        var preview = WeakReferenceMessenger.Default.Send(new ChannelsSettingsRequestMessage()).Response;
        PreviewUpdated(preview);
    }

    private async Task ChannelsUpdated(AirChannel[] channels) {
        _channelsList = channels;
        //updating listing
    }

    private async Task PreviewUpdated(AirChannel? preview) {
        //preview acts as the settings holder
        //update which type of channel view to show

        //state machine, end previous view and start new view

        _previewChannel = preview;

    }

    #region Commands

    [RelayCommand]
    private void ClearChannels() {
        WeakReferenceMessenger.Default.Send(new ClearAirChannelsMessage());
    }

    #endregion
}
