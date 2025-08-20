using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Features;
using Fabolus.Wpf.Features.Channels;

namespace Fabolus.Wpf.Pages.Channels;

public abstract class BaseChannelsViewModel : ObservableObject {
    protected readonly IMessenger _messenger = WeakReferenceMessenger.Default;

    protected AirChannelsCollection _channels = [];
    protected AirChannelSettings _settings;
    protected IAirChannel _activeChannel;
    protected bool _isBusy = false; //used to lock object when sending messages to prevent reading message it sent

    protected bool IsActiveChannelSelected => _channels.ContainsKey(_activeChannel.GUID);

    protected BaseChannelsViewModel() {
        SetMessaging();

        var activeChannel = _messenger.Send(new ActiveChannelRequestMessage()).Response;
        ActiveChannelUpdated(activeChannel);

        var channels = _messenger.Send(new AirChannelsRequestMessage()).Response;
        ChannelsUpdated(channels);

        var settings = _messenger.Send(new ChannelsSettingsRequestMessage()).Response;
        SettingsUpdated(settings);

    }

    protected virtual void SetMessaging() {
        _messenger.Register<AirChannelsUpdatedMessage>(this, (r, m) => ChannelsUpdated(m.Channels));
        _messenger.Register<ChannelSettingsUpdatedMessage>(this, (r,m) => SettingsUpdated(m.Settings));
        _messenger.Register<ActiveChannelUpdatedMessage>(this, (r, m) => ActiveChannelUpdated(m.Channel));
    }

    // virtual methods for subclasses to modify
    protected virtual void ActiveChannelUpdated(IAirChannel channel) => _activeChannel = channel;

    protected virtual void ChannelsUpdated(AirChannelsCollection channels) => _channels = channels;

    protected virtual void SettingsUpdated(AirChannelSettings settings) => _settings = settings;
}
