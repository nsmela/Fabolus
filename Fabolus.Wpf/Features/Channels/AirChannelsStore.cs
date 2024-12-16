using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.AirChannel;
using Fabolus.Wpf.Common.Helpers;

namespace Fabolus.Wpf.Features.Channels;
public class AirChannelsStore {
    private ChannelTypes _selectedType;
    private Dictionary<ChannelTypes, AirChannel> _settings = []; //saved channel settings
    private Dictionary<Guid, AirChannel> _channels = [];
    private Guid? _selectedChannel;
    private AirChannel Preview => _settings[_selectedType];

    public AirChannelsStore() {
        _settings = EnumHelper
            .GetEnums<ChannelTypes>()
            .Select(c => c.ToAirChannel())
            .ToDictionary( x => x.ChannelType);

        _selectedType = ChannelTypes.Straight;

        _channels = [];

        //messaging
        WeakReferenceMessenger.Default.Register<AddAirChannelMessage>(this, async (r,m) => await AddChannel(m.Channel));
        WeakReferenceMessenger.Default.Register<ClearAirChannelsMessage>(this, async (r, m) => await ClearChannels());
        WeakReferenceMessenger.Default.Register<RemoveAirChannelMessage>(this, async (r, m) => await RemoveChannel(m.Channel));
        WeakReferenceMessenger.Default.Register<SetChannelTypeMessage>(this, async (r, m) => await SetType(m.Type));
        WeakReferenceMessenger.Default.Register<SetChannelSettingsMessage>(this, async (r, m) => await UpdateSettings(m.Settings));
        WeakReferenceMessenger.Default.Register<SetSelectedChannelMessage>(this, async (r,m) => await SetSelectedChannel(m.Channel));

        WeakReferenceMessenger.Default.Register<AirChannelsRequestMessage>(this, (r, m) => m.Reply(_channels.Values.ToArray()));
        WeakReferenceMessenger.Default.Register<ChannelsSettingsRequestMessage>(this, (r, m) => m.Reply(Preview));
    }

    private async Task AddChannel(AirChannel channel) {
        _channels.Add(channel.GUID, channel);
        await OnChannelsUpdated();
    }

    private async Task ClearChannels() {
        _channels.Clear();
        await OnChannelsUpdated();
    }

    private async Task RemoveChannel(AirChannel channel) {
        if (!_channels.ContainsKey(channel.GUID)) { throw new ArgumentNullException("air channel was not found"); }
    
        _channels.Remove(channel.GUID);
        await OnChannelsUpdated();
    }

    private async Task UpdateSettings(AirChannel channel) {
        //see if channel already exists
        //if channel id matches, it's a selected channel, not just settings update
        if ( _selectedChannel is not null) {
            var newChannel = channel with {
                Anchor = _channels[_selectedChannel.Value].Anchor,
                GUID = _selectedChannel.Value,
            };
            _channels[_selectedChannel.Value] = newChannel;
            await OnSettingsChanged();
            await OnChannelsUpdated();
        }

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
        WeakReferenceMessenger.Default.Send(new AirChannelsUpdatedMessage(_channels.Values.ToArray()));  
    }

    private async Task OnSettingsChanged() {
        WeakReferenceMessenger.Default.Send(new ChannelSettingsUpdatedMessage(Preview));
    }
}
