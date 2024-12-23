using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.AirChannel;

namespace Fabolus.Wpf.Features.Channels;
public class AirChannelsStore {
    private AirChannelSettings _settings = []; //saved channel settings
    private AirChannelsCollection _channels = [];
    private ChannelTypes? _selectedType;

    public AirChannelsStore() {
        _settings = AirChannelSettings.Initialize();

        _channels = [];

        //messaging
        WeakReferenceMessenger.Default.Register<ActiveChannelSetMessage>(this, async (r,m) => await SetActiveChannel(m.ChannelId));
        WeakReferenceMessenger.Default.Register<ChannelAddMessage> (this, async (r, m) => await AddChannel(m.Channel));
        WeakReferenceMessenger.Default.Register<ChannelSettingsSetMessage>(this, async (r, m) => await SetChannelSettings(m.Settings));
        WeakReferenceMessenger.Default.Register<ChannelTypeSetMessage>(this, async (r, m) => await SetChannelType(m.Type));
        WeakReferenceMessenger.Default.Register<ChannelRemoveMessage>(this, async (r, m) => await RemoveChannel(m.Id));
        WeakReferenceMessenger.Default.Register<ChannelClearMessage>(this, async (r, m) => await ClearChannels());

        WeakReferenceMessenger.Default.Register<AirChannelsRequestMessage>(this, (r, m) => m.Reply(_channels));
        WeakReferenceMessenger.Default.Register<ChannelsSettingsRequestMessage>(this, (r, m) => m.Reply(_settings));
    }

    private async Task AddChannel(IAirChannel channel) {
        _channels.Add(channel); //add channel and set it as active
        WeakReferenceMessenger.Default.Send(new AirChannelsUpdatedMessage(_channels));
    }

    private async Task ClearChannels() {
        _channels = _channels.Clear();
        WeakReferenceMessenger.Default.Send(new AirChannelsUpdatedMessage(_channels));
    }

    private async Task RemoveChannel(Guid? id) {
        _channels.RemoveActiveChannel();
        WeakReferenceMessenger.Default.Send(new AirChannelsUpdatedMessage(_channels));
    }

    private async Task SetActiveChannel(Guid? id) {
        if (id != null) {
            var type = _channels[id.Value].ChannelType;
            _settings.SetSelectedType(type);
            WeakReferenceMessenger.Default.Send(new ChannelSettingsUpdatedMessage(_settings));
        }

        _channels.SetActiveChannel(id);
        WeakReferenceMessenger.Default.Send(new AirChannelsUpdatedMessage(_channels));
    }

    private async Task SetChannelSettings(AirChannelSettings? settings) {
        if (settings is null) { return; }

        _settings = settings;

        //TODO handle updating anything related and send update messages
        WeakReferenceMessenger.Default.Send(new ChannelSettingsUpdatedMessage(_settings));

        //TODO update 
        var channel = _channels.GetActiveChannel;
        if (channel is null || channel.ChannelType != _selectedType) { return; }

        _channels[channel.GUID] = channel.ApplySettings(_settings);
        WeakReferenceMessenger.Default.Send(new AirChannelsUpdatedMessage(_channels));
    }

    private async Task SetChannelType(ChannelTypes? type) {
        if (!type.HasValue || type.Value == _settings.SelectedType) { return; }

        _settings.SetSelectedType(type ?? ChannelTypes.Straight);

        //TODO handle updating anything related and send update messages
        WeakReferenceMessenger.Default.Send(new ChannelSettingsUpdatedMessage(_settings));

        _channels.SetActiveChannel(null);
        WeakReferenceMessenger.Default.Send(new AirChannelsUpdatedMessage(_channels));
    }
}
