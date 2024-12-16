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

    partial void OnChannelConeDiameterChanged(float value) => SetSettings();
    partial void OnChannelConeLengthChanged(float value) => SetSettings();
    partial void OnChannelDiameterChanged(float value) => SetSettings();
    partial void OnChannelDepthChanged(float value) => SetSettings();

    private bool _isBusy = false;

    public AngledChannelsViewModel() : base() { }

    private async Task SetSettings() {
        if (_isBusy) { return; }
        _isBusy = true;

        var channel = new AngledAirChannel {
            Depth = ChannelDepth,
            Diameter = ChannelDiameter,
            BottomDiameter = ChannelConeDiameter,
            TipLength = ChannelConeLength
        };

        channel.Build();
        _settings[ChannelTypes.AngledHead] = channel;

        WeakReferenceMessenger.Default.Send(new ChannelSettingsUpdatedMessage(_settings));

        _isBusy = false;
    }

    protected override async Task SettingsUpdated(AirChannelSettings settings) {
        _settings = settings;
        if (_isBusy) { return; }
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
