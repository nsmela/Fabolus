using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
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

        WeakReferenceMessenger.Default.Send(new SetChannelSettingsMessage(channel));

        _isBusy = false;
    }

    protected override async Task SettingsUpdated(AirChannel? preview) {
        if (_isBusy) { return; }
        var channel = preview as AngledAirChannel;
        if (channel is null) { return; }

        _isBusy = true;

        ChannelDepth = channel.Depth;
        ChannelDiameter = channel.Diameter;
        ChannelConeDiameter = channel.BottomDiameter;
        ChannelConeLength = channel.TipLength; 

        _isBusy = false;
    }
}
