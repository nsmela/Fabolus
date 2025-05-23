﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.AirChannel;
using Fabolus.Wpf.Features.Channels;
using Fabolus.Wpf.Features;
using Fabolus.Wpf.Features.Channels.Path;
using CommunityToolkit.Mvvm.Input;

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
    private bool HasPathChannel => _channels.Any(x => x.Value.ChannelType == ChannelTypes.Path);

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

        var channel = _channels.PathChannel();
        channel = channel with {
            Depth = this.Depth,
            Height = this.LowerHeight,
            UpperHeight = this.UpperHeight,
            LowerDiameter = this.LowerDiameter,
            UpperDiameter = UpperDiameter,
        };

        channel.Build();

        //paths channel only has a single channel within the AirChannelsCollection
        if (HasPathChannel) {
            var id = _channels.First(x => x.Value.ChannelType == ChannelTypes.Path).Key;
            channel = channel with { GUID = id };
        }

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

    [RelayCommand]
    public async Task ClearPathPoints() {
        if (!HasPathChannel) { return; }

        // clear storage
        _channels.RemovePaths();

        WeakReferenceMessenger.Default.Send(new AirChannelsUpdatedMessage(_channels));

        //clear preview
        var setting = _settings[ChannelTypes.Path] as PathAirChannel with { PathPoints = [] };
        _settings[ChannelTypes.Path] = setting;
        WeakReferenceMessenger.Default.Send(new ChannelSettingsUpdatedMessage(_settings));

        //clear active channel
        _activeChannel = setting.New();
        WeakReferenceMessenger.Default.Send(new ActiveChannelUpdatedMessage(_activeChannel));
    }

}
