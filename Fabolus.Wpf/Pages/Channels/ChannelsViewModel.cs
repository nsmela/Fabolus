using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.AirChannel;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Helpers;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Features.Channels;
using Fabolus.Wpf.Features.Channels.Angled;
using Fabolus.Wpf.Features.Channels.Straight;
using Fabolus.Wpf.Pages.Channels.Angled;
using Fabolus.Wpf.Pages.Channels.Straight;

namespace Fabolus.Wpf.Pages.Channels;

public partial class ChannelsViewModel : BaseViewModel {
    public override string TitleText => "channels";

    public override SceneManager GetSceneManager => new ChannelsSceneManager();

    [ObservableProperty] private BaseChannelsViewModel? _currentChannelViewModel;
    [ObservableProperty] private string[] _channelNames = [];
    [ObservableProperty] private int _activeToolIndex = 0;

    partial void OnActiveToolIndexChanged(int value) {
        AirChannel channel = ((ChannelTypes)value).ToAirChannel();

        WeakReferenceMessenger.Default.Send(new ChannelSettingsUpdatedMessage(channel));
    }

    private AirChannel _previewChannel;
    private AirChannel[] _channelsList = [];

    public ChannelsViewModel() {
        ChannelNames = EnumHelper.GetEnumDescriptions<ChannelTypes>().ToArray();

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
        if (preview is null) { return; }
        if (_previewChannel is not null && preview.ChannelType == _previewChannel.ChannelType) { return; }

        //state machine, end previous view and start new view

        _previewChannel = preview;
        CurrentChannelViewModel = preview.ChannelType switch {
            ChannelTypes.Straight => new StraightChannelsViewModel(),
            ChannelTypes.AngledHead => new AngledChannelsViewModel(),
            _ => throw new NotImplementedException()
        };

        ActiveToolIndex = (int)preview.ChannelType;
    }

    #region Commands

    [RelayCommand]
    private void ClearChannels() {
        WeakReferenceMessenger.Default.Send(new ClearAirChannelsMessage());
    }

    #endregion
}
