using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.AirChannel;
using Fabolus.Wpf.Features.Channels;
using Fabolus.Wpf.Features;
using SharpDX.Direct2D1;
using Fabolus.Wpf.Features.Channels.Path;
using Fabolus.Wpf.Features.Channels.Angled;

namespace Fabolus.Wpf.Pages.Channels.Path;
public partial class PathChannelsViewModel : BaseChannelsViewModel {

    [ObservableProperty] private float _depth;
    [ObservableProperty] private float _lowerDiameter;
    [ObservableProperty] private float _upperDiameter;
    [ObservableProperty] private float _lowerHeight;
    [ObservableProperty] private float _upperHeight;

    partial void OnDepthChanged(float value) => SetSettings();
    partial void OnLowerDiameterChanged(float value) => SetSettings();
    partial void OnUpperDiameterChanged(float value) => SetSettings();
    partial void OnLowerHeightChanged(float value) => SetSettings();
    partial void OnUpperHeightChanged(float value) => SetSettings();

    private bool _isBusy = false;

    public PathChannelsViewModel() : base() { }

    protected override async Task SettingsUpdated(AirChannelSettings settings) {
        _settings = settings;
        var channel = _settings[ChannelTypes.Path] as PathAirChannel;
        if (channel is null) { return; }
        if (_isBusy) { return; }

        _isBusy = true;

        Depth = channel.Depth;
        LowerDiameter = channel.LowerDiameter;
        UpperDiameter = channel.UpperDiameter;
        LowerHeight = channel.Height;
        UpperHeight = channel.UpperHeight;

        _isBusy = false;
    }

    private async Task SetSettings() {
        if (_isBusy) { return; }
        _isBusy = true;

        await ApplySettingsToChannel();
        await ApplySettings();

        _isBusy = false;
    }

    private async Task ApplySettingsToChannel() {
        if (!IsActiveChannelSelected) { return; }

        //there is an active channel

        var channel = _channels[_activeChannel.GUID] as PathAirChannel;
        channel = channel with {
            Depth = this.Depth,
            Height = this.LowerHeight,
            UpperHeight = this.UpperHeight,
            LowerDiameter = this.LowerDiameter,
            UpperDiameter = UpperDiameter,
        };

        channel.Build();
        _channels[channel.GUID] = channel;
        WeakReferenceMessenger.Default.Send(new AirChannelsUpdatedMessage(_channels));
    }

    private async Task ApplySettings() {
        var channel = new PathAirChannel {
            Depth = this.Depth,
            Height = this.LowerHeight,
            UpperHeight = this.UpperHeight,
            UpperDiameter = UpperDiameter,
        };

        channel.Build();
        _settings[ChannelTypes.Path] = channel;

        WeakReferenceMessenger.Default.Send(new ChannelSettingsUpdatedMessage(_settings));
    }
}
