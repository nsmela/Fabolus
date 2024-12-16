using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.AirChannel;
using Fabolus.Wpf.Common.Helpers;
using Fabolus.Wpf.Features.Channels.Straight;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Features.Channels;
public class AirChannelsStore {
    private ChannelTypes _selectedType;
    private Dictionary<ChannelTypes, AirChannel> _settings = []; //saved channel settings
    private List<AirChannel> _channels = [];
    private Guid? _selectedChannel;
    private AirChannel Preview => _settings[_selectedType];

    public AirChannelsStore() {
        _settings = EnumHelper
            .GetEnums<ChannelTypes>()
            .Select(c => c.ToAirChannel())
            .ToDictionary( x => x.ChannelType);

        _selectedType = ChannelTypes.Straight;

        //messaging
        WeakReferenceMessenger.Default.Register<AddAirChannelMessage>(this, async (r,m) => await AddChannel(m.Channel));
        WeakReferenceMessenger.Default.Register<ClearAirChannelsMessage>(this, async (r, m) => await ClearChannels());
        WeakReferenceMessenger.Default.Register<RemoveAirChannelMessage>(this, async (r, m) => await RemoveChannel(m.Channel));
        WeakReferenceMessenger.Default.Register<SetChannelTypeMessage>(this, async (r, m) => await SetType(m.Type));
        WeakReferenceMessenger.Default.Register<SetChannelSettingsMessage>(this, async (r, m) => await SetPreview(m.Settings));
        WeakReferenceMessenger.Default.Register<SetSelectedChannelMessage>(this, async (r,m) => await SetSelectedChannel(m.Channel));

        WeakReferenceMessenger.Default.Register<AirChannelsRequestMessage>(this, (r, m) => m.Reply(_channels.ToArray()));
        WeakReferenceMessenger.Default.Register<ChannelsSettingsRequestMessage>(this, (r, m) => m.Reply(Preview));
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
        var type = channel.ChannelType;
        _settings[type] = channel;

        if (type != _selectedType) { return; }
        await OnSettingsChanged();
    }

    private async Task SetType(ChannelTypes type) {
        _selectedType = type;
        await OnSettingsChanged();
    }

    private async Task SetSelectedChannel(AirChannel? channel) {
        _selectedChannel = channel?.GUID;
    }

    private async Task OnChannelsUpdated() {
        WeakReferenceMessenger.Default.Send(new AirChannelsUpdatedMessage(_channels.ToArray()));  
    }

    private async Task OnSettingsChanged() {
        WeakReferenceMessenger.Default.Send(new ChannelSettingsUpdatedMessage(Preview));
    }
}
