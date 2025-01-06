using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.AirChannel;
using Fabolus.Wpf.Features;
using Fabolus.Wpf.Features.Channels;
using Fabolus.Wpf.Features.Channels.Angled;
using Fabolus.Wpf.Features.Channels.Straight;

namespace Fabolus.Wpf.Pages.Channels.Straight;

public partial class StraightChannelsViewModel : BaseChannelsViewModel {

    [ObservableProperty] private float _channelDepth;
    [ObservableProperty] private float _channelDiameter;
    [ObservableProperty] private float _channelNozzleDiameter;
    [ObservableProperty] private float _channelNozzleLength;

    partial void OnChannelDepthChanged(float value) => SetSettings();
    partial void OnChannelDiameterChanged(float value) => SetSettings();
    partial void OnChannelNozzleDiameterChanged(float value) => SetSettings();
    partial void OnChannelNozzleLengthChanged(float value) => SetSettings();

    public StraightChannelsViewModel() : base(){ }

    private async Task ApplySettings() {
        var channel = new StraightAirChannel {
            Depth = ChannelDepth,
            UpperDiameter = ChannelDiameter,
            LowerDiameter = ChannelNozzleDiameter,
            TipLength = ChannelNozzleLength
        };

        channel.Build();
        _settings[ChannelTypes.Straight] = channel;

        _isBusy = true;
        WeakReferenceMessenger.Default.Send(new ChannelSettingsUpdatedMessage(_settings));
        _isBusy = false;
    }

    private async Task ApplySettingsToChannel() {
        if (!IsActiveChannelSelected) { return; }
        //there is an active channel

        var channel = _channels[_activeChannel.GUID] as StraightAirChannel;
        channel = channel with {
            Depth = ChannelDepth,
            UpperDiameter = ChannelDiameter,
            LowerDiameter = ChannelNozzleDiameter,
            TipLength = ChannelNozzleLength
        };

        channel.Build();
        _channels[channel.GUID] = channel;
        WeakReferenceMessenger.Default.Send(new AirChannelsUpdatedMessage(_channels));
    }

    private async Task SetSettings() {
        if (_isBusy) { return; }
        _isBusy = true;

        await ApplySettingsToChannel();
        await ApplySettings();

        _isBusy = false;
    }

    protected override async Task SettingsUpdated(AirChannelSettings settings) {
        if (_isBusy) { return; }
        _settings = settings;
        var channel = _settings[ChannelTypes.Straight] as StraightAirChannel;
        if (channel is null) { return; }

        _isBusy = true;

        ChannelDepth = channel.Depth;
        ChannelDiameter = channel.UpperDiameter;
        ChannelNozzleDiameter = channel.LowerDiameter;
        ChannelNozzleLength = channel.TipLength;

        _isBusy = false;
    }

}
