using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Features;
using Fabolus.Wpf.Features.Channels;

namespace Fabolus.Wpf.Pages.Channels;

public abstract class BaseChannelsViewModel : ObservableObject {
    protected AirChannelsCollection _channels = [];
    protected AirChannelSettings _settings;
    protected IAirChannel _activeChannel;
    protected bool _isBusy = false; //used to lock object when sending messages to prevent reading message it sent

    protected bool IsActiveChannelSelected => _channels.ContainsKey(_activeChannel.GUID);

    protected BaseChannelsViewModel() {
        SetMessaging();

        var activeChannel = WeakReferenceMessenger.Default.Send(new ActiveChannelRequestMessage()).Response;
        ActiveChannelUpdated(activeChannel);

        var channels = WeakReferenceMessenger.Default.Send(new AirChannelsRequestMessage()).Response;
        ChannelsUpdated(channels);

        var settings = WeakReferenceMessenger.Default.Send(new ChannelsSettingsRequestMessage()).Response;
        SettingsUpdated(settings);

    }

    protected virtual void SetMessaging() {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<AirChannelsUpdatedMessage>(this, async (r, m) => await ChannelsUpdated(m.Channels));
        WeakReferenceMessenger.Default.Register<ChannelSettingsUpdatedMessage>(this, async (r,m) => await SettingsUpdated(m.Settings));
        WeakReferenceMessenger.Default.Register<ActiveChannelUpdatedMessage>(this, async (r, m) => await ActiveChannelUpdated(m.Channel));
    }

    // virtual methods for subclasses to modify
    protected virtual async Task ActiveChannelUpdated(IAirChannel channel) => _activeChannel = channel;

    protected virtual async Task ChannelsUpdated(AirChannelsCollection channels) => _channels = channels;

    protected virtual async Task SettingsUpdated(AirChannelSettings settings) => _settings = settings;
}
