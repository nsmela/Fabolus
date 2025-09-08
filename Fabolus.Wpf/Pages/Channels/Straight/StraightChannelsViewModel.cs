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

    partial void OnChannelDepthChanged(float oldValue, float newValue) {
        if (_isBusy) { return; }
        if (oldValue == newValue) { return; }
        SetSettings();
    }
    partial void OnChannelDiameterChanged(float oldValue, float newValue) {
        if (_isBusy) { return; }
        if (oldValue == newValue) { return; }
        SetSettings();
    }
    partial void OnChannelNozzleDiameterChanged(float oldValue, float newValue) {
        if (_isBusy) { return; }
        if (oldValue == newValue) { return; }

        // nozzle diameter constrained to ChannelDiameter
        if (newValue > ChannelDiameter) {
            _isBusy = true;
            ChannelNozzleDiameter = ChannelDiameter;
            _isBusy = false;
        }

        SetSettings();
    }
    partial void OnChannelNozzleLengthChanged(float oldValue, float newValue) {
        if (_isBusy) { return; }
        if (oldValue == newValue) { return; }
        SetSettings();
    }

    private void ApplySettings() {
        var channel = new StraightAirChannel {
            Depth = ChannelDepth,
            UpperDiameter = ChannelDiameter,
            LowerDiameter = ChannelNozzleDiameter,
            TipLength = ChannelNozzleLength
        };

        channel.Build();
        _settings[ChannelTypes.Straight] = channel;

        _isBusy = true;
        _messenger.Send(new ChannelSettingsUpdatedMessage(_settings));
        _isBusy = false;
    }

    private void ApplySettingsToChannel() {
        if (!IsActiveChannelSelected) { return; }
        //there is an active channel

        if (_channels[_activeChannel.GUID] is StraightAirChannel channel) {
            channel = channel with {
                Depth = ChannelDepth,
                UpperDiameter = ChannelDiameter,
                LowerDiameter = ChannelNozzleDiameter,
                TipLength = ChannelNozzleLength
            };

            channel.Build();
            _channels[channel.GUID] = channel;
            _messenger.Send(new AirChannelsUpdatedMessage(_channels));
        }
    }

    private void SetSettings() {
        if (_isBusy) { return; }
        _isBusy = true;

        ApplySettingsToChannel();
        ApplySettings();

        _isBusy = false;
    }

    protected override void SettingsUpdated(AirChannelSettings settings) {
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
