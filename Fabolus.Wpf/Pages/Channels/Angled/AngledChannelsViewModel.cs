using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.AirChannel;
using Fabolus.Wpf.Features;
using Fabolus.Wpf.Features.Channels;
using Fabolus.Wpf.Features.Channels.Angled;

namespace Fabolus.Wpf.Pages.Channels.Angled;

public partial class AngledChannelsViewModel : BaseChannelsViewModel {
    [ObservableProperty] private float _channelConeDiameter;
    [ObservableProperty] private float _channelConeLength;
    [ObservableProperty] private float _channelDiameter;
    [ObservableProperty] private float _channelDepth;

    partial void OnChannelConeDiameterChanged(float oldValue, float newValue) {
        if (oldValue == newValue) { return; }
        SetSettings();
    }

    partial void OnChannelConeLengthChanged(float oldValue, float newValue) {
        if (oldValue == newValue) { return; }
        SetSettings();
    }

    partial void OnChannelDiameterChanged(float oldValue, float newValue) {
        if (oldValue == newValue) { return; }
        SetSettings();
    }

    partial void OnChannelDepthChanged(float oldValue, float newValue) {
        if (oldValue == newValue) { return; }
        SetSettings();
    }

    private void ApplySettings() {
        var channel = new AngledAirChannel {
            Depth = ChannelDepth,
            Diameter = ChannelDiameter,
            BottomDiameter = ChannelConeDiameter,
            TipLength = ChannelConeLength,
        };

        channel.Build();
        _settings[ChannelTypes.AngledHead] = channel;

        _isBusy = true;
        WeakReferenceMessenger.Default.Send(new ChannelSettingsUpdatedMessage(_settings));
        _isBusy = false;
    }


    private void ApplySettingsToChannel() {
        if (!IsActiveChannelSelected) { return; }

        //there is an active channel

        var channel = _channels[_activeChannel.GUID] as AngledAirChannel;
        if (channel is null) { return; }
        channel = channel with {
            Depth = ChannelDepth,
            Diameter = ChannelDiameter,
            BottomDiameter = ChannelConeDiameter,
            TipLength = ChannelConeLength
        };

        channel.Build();
        _channels[channel.GUID] = channel;
        WeakReferenceMessenger.Default.Send(new AirChannelsUpdatedMessage(_channels));
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
        var channel = _settings[ChannelTypes.AngledHead] as AngledAirChannel;
        if (channel is null) { return; }

        _isBusy = true;

        ChannelDepth = channel.Depth;
        ChannelDiameter = channel.Diameter;
        ChannelConeDiameter = channel.BottomDiameter;
        ChannelConeLength = channel.TipLength; 

        _isBusy = false;
    }

}
