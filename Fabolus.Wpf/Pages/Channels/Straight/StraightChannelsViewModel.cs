using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Features.Channels.Straight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Pages.Channels.Straight;
public partial class StraightChannelsViewModel : BaseChannelsViewModel {
    [ObservableProperty] private float _channelDepth;
    [ObservableProperty] private float _channelDiameter;
    [ObservableProperty] private float _channelNozzleDiameter;
    [ObservableProperty] private float _channelNozzleLength;

    private bool _isBusy = false;

    private async Task SetSettings() {
        if (_isBusy) { return; }
        _isBusy = true;

        var channel = new StraightAirChannel {
            Depth = _channelDepth,
            Diameter = _channelDiameter,
        };

        WeakReferenceMessenger.Default.Send();

        _isBusy = false;
    }
}
