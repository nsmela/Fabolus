using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Features.Channels;
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

    private bool _isBusy = false;

    public StraightChannelsViewModel() : base(){ }
    public StraightChannelsViewModel(StraightAirChannel settings) : base() {
        SettingsUpdated(settings);
    }

    protected override async Task SettingsUpdated(AirChannel? preview) {
        var channel = preview as StraightAirChannel;
        if (channel is null) { return; }
        if (_isBusy) { return; }

        _isBusy = true;

        ChannelDepth = channel.Depth;
        ChannelDiameter = channel.Diameter;
        ChannelNozzleDiameter = channel.BottomDiameter;
        ChannelNozzleLength = channel.TipLength;

        _isBusy = false;
    }

    private async Task SetSettings() {
        if (_isBusy) { return; }
        _isBusy = true;

        var channel = new StraightAirChannel {
            Depth = ChannelDepth,
            Diameter = ChannelDiameter,
            BottomDiameter = ChannelNozzleDiameter,
            TipLength = ChannelNozzleLength
        };

        WeakReferenceMessenger.Default.Send(new SetChannelSettingsMessage(channel));

        _isBusy = false;
    }
}
