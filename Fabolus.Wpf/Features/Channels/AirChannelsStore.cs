using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.AirChannel;

namespace Fabolus.Wpf.Features.Channels;
public class AirChannelsStore {
    private AirChannelSettings _settings = []; //saved channel settings
    private AirChannelsCollection _channels = [];
    private IAirChannel _activeChannel; //as air channel because requests may want the type or the GUID

    public AirChannelsStore() {
        _settings = AirChannelSettings.Initialize();

        _channels = [];

        _activeChannel = _settings[new ChannelTypes()];

        //messaging
        WeakReferenceMessenger.Default.Register<ActiveChannelUpdatedMessage>(this, (r,m) => _activeChannel = m.Channel);
        WeakReferenceMessenger.Default.Register<AirChannelsUpdatedMessage>(this, (r, m) => _channels = m.Channels);
        WeakReferenceMessenger.Default.Register<ChannelSettingsUpdatedMessage>(this, (r, m) => _settings = m.Settings);

        //requests
        WeakReferenceMessenger.Default.Register<ActiveChannelRequestMessage>(this, (r, m) => m.Reply(_activeChannel));
        WeakReferenceMessenger.Default.Register<AirChannelsRequestMessage>(this, (r, m) => m.Reply(_channels));
        WeakReferenceMessenger.Default.Register<ChannelsSettingsRequestMessage>(this, (r, m) => m.Reply(_settings));
    }

}
