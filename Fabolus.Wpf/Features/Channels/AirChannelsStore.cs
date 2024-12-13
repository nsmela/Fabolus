using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Features.Channels.Straight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Features.Channels;
public class AirChannelsStore {
    private AirChannel _preview = new StraightAirChannel(); //acts as the settings for the selected channel type
    private List<AirChannel> _channels = [];

    public AirChannelsStore() {
        //messaging
        WeakReferenceMessenger.Default.Register<AddAirChannelMessage>(this, async (r,m) => await AddChannel(m.channel));
        WeakReferenceMessenger.Default.Register<ClearAirChannelsMessage>(this, async (r, m) => await ClearChannels());
        WeakReferenceMessenger.Default.Register<RemoveAirChannelMessage>(this, async (r, m) => await RemoveChannel(m.channel));
        WeakReferenceMessenger.Default.Register<ChannelSettingsUpdatedMessage>(this, async (r,m) => await SetPreview(m.settings));

        WeakReferenceMessenger.Default.Register<AirChannelsRequestMessage>(this, (r, m) => m.Reply(_channels.ToArray()));
        WeakReferenceMessenger.Default.Register<ChannelsSettingsRequestMessage>(this, (r, m) => m.Reply(_preview));
    }

    private async Task AddChannel(AirChannel channel) {
        _channels.Add(channel);
        await OnChannelsUpdated();
    }

    private async Task ClearChannels() {
        _channels.Clear();
        await OnChannelsUpdated();
    }

    private async Task RemoveChannel(AirChannel channel) {
        if (!_channels.Contains(channel)) { throw new ArgumentNullException("air channel was not found"); }
    
        _channels.Remove(channel);
        await OnChannelsUpdated();
    }

    private async Task SetPreview(AirChannel channel) {
        _preview = channel;
        await OnChannelsUpdated();
    }

    private async Task OnChannelsUpdated() {
        WeakReferenceMessenger.Default.Send(new AirChannelsUpdatedMessage(_channels.ToArray()));  
    }
}
