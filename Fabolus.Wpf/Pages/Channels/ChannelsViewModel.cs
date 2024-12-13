using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Features.Channels;
using Fabolus.Wpf.Pages.Channels.Straight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Pages.Channels;

public partial class ChannelsViewModel : BaseViewModel {
    public override string TitleText => "Channels";

    public override SceneManager GetSceneManager => new ChannelsSceneManager();

    [ObservableProperty] private BaseChannelsViewModel _channelsViewModel = new StraightChannelsViewModel();

    private AirChannel _previewChannel;
    private AirChannel[] _channels = [];

    public ChannelsViewModel() {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<AirChannelsUpdatedMessage>(this, (r, m) => { });

    }

    private async Task ChannelsUpdated(AirChannel[] channels) {
        _channels = channels;
        //updating listing
    }

    private async Task PreviewUpdated(AirChannel preview) {
        //preview acts as the settings holder
        //update which type of channel view to show
        if (_previewChannel.ChannelType == preview.ChannelType) { return; }

        //state machine, end previous view and start new view

        _previewChannel = preview;

    }
}
