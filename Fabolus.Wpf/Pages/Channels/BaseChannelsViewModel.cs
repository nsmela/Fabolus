using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Features;
using Fabolus.Wpf.Features.Channels;

namespace Fabolus.Wpf.Pages.Channels;

public abstract class BaseChannelsViewModel : ObservableObject {
    protected AirChannelsCollection _channels = [];
    protected AirChannelSettings _settings;
    protected bool _isBusy = false; //used to lock object when sending messages to prevent reading message it sent

    protected BaseChannelsViewModel() {
        SetMessaging();

        var channels = WeakReferenceMessenger.Default.Send(new AirChannelsRequestMessage()).Response;
        _channels = channels;

        var settings = WeakReferenceMessenger.Default.Send(new ChannelsSettingsRequestMessage()).Response;
        SettingsUpdated(settings);
    }

    protected virtual void SetMessaging() {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<AirChannelsUpdatedMessage>(this, async (r, m) => await ChannelsUpdated(m.Channels));
        WeakReferenceMessenger.Default.Register<ChannelSettingsUpdatedMessage>(this, async (r,m) => await SettingsUpdated(m.Settings));
    }

    protected virtual async Task ChannelsUpdated(AirChannelsCollection channels) {
        _channels = channels;
    }

    protected virtual async Task SettingsUpdated(AirChannelSettings settings) {
        _settings = settings;
    }
}
