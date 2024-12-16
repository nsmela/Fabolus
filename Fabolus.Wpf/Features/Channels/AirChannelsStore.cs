using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.AirChannel;

namespace Fabolus.Wpf.Features.Channels;
public class AirChannelsStore {
    private AirChannelSettings _settings = []; //saved channel settings
    private AirChannelsCollection _channels = [];

    public AirChannelsStore() {
        _settings = AirChannelSettings.Initialize();

        _channels = [];

        //messaging
        WeakReferenceMessenger.Default.Register<AirChannelsUpdatedMessage>(this, async (r,m) => await UpdateChannels(m.Channels));
        WeakReferenceMessenger.Default.Register<ChannelSettingsUpdatedMessage>(this, async (r, m) => await UpdateSettings(m.Settings));

        WeakReferenceMessenger.Default.Register<AirChannelsRequestMessage>(this, (r, m) => m.Reply(_channels));
        WeakReferenceMessenger.Default.Register<ChannelsSettingsRequestMessage>(this, (r, m) => m.Reply(_settings));
    }

    private async Task UpdateSettings(AirChannelSettings settings) {
        _settings = settings;
    }

    private async Task UpdateChannels(AirChannelsCollection channels) {
        _channels = channels;
    }
}
