using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Features.Channels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Pages.Channels;
public abstract class BaseChannelsViewModel : ObservableObject {
    protected AirChannel[] _channels = [];
    protected AirChannel _settings;

    protected BaseChannelsViewModel() {
        SetMessaging();
        var preview = WeakReferenceMessenger.Default.Send(new ChannelsSettingsRequestMessage()).Response;
        SettingsUpdated(preview);
    }

    protected virtual void SetMessaging() {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<AirChannelsUpdatedMessage>(this, async (r, m) => await ChannelsUpdated(m.Channels));
        WeakReferenceMessenger.Default.Register<ChannelSettingsUpdatedMessage>(this, async (r,m) => await SettingsUpdated(m.Settings));
    }

    protected virtual async Task ChannelsUpdated(AirChannel[]? channels) {
        _channels = channels;
    }

    protected virtual async Task SettingsUpdated(AirChannel? preview) {
        _settings = preview;
    }
}
