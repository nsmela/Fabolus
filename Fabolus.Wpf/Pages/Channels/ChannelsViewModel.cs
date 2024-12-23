using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.AirChannel;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Helpers;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Features;
using Fabolus.Wpf.Features.Channels;
using System.Windows.Controls;

namespace Fabolus.Wpf.Pages.Channels;

public partial class ChannelsViewModel : BaseViewModel {
    public override string TitleText => "channels";

    public override SceneManager GetSceneManager => new ChannelsSceneManager();

    [ObservableProperty] private BaseChannelsViewModel? _currentChannelViewModel;
    [ObservableProperty] private string[] _channelNames = [];
    [ObservableProperty] private int _activeToolIndex = 0;

    private ChannelTypes CurrentType {
        get => (ChannelTypes)ActiveToolIndex;
        set => ActiveToolIndex = (int)value;
    }

    partial void OnActiveToolIndexChanged(int value) {
        WeakReferenceMessenger.Default.Send(new ChannelTypeSetMessage((ChannelTypes)value));
    }

    private AirChannelsCollection _channels = [];
    private AirChannelSettings _settings;

    public ChannelsViewModel() {
        ChannelNames = EnumHelper.GetEnumDescriptions<ChannelTypes>().ToArray();

        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<AirChannelsUpdatedMessage>(this, async (r, m) => await ChannelsUpdated(r, m.Channels));
        WeakReferenceMessenger.Default.Register<ChannelSettingsUpdatedMessage>(this, async (r, m) => await SettingsUpdated(r, m.Settings));

        _settings = WeakReferenceMessenger.Default.Send(new ChannelsSettingsRequestMessage()).Response;

        var channels = WeakReferenceMessenger.Default.Send(new AirChannelsRequestMessage()).Response;
        _channels.SetActiveChannel(null);
        _channels = channels;

        CurrentType = _settings.SelectedType; //set active index and generates view model
        CurrentChannelViewModel = _settings.SelectedType.ToViewModel(); //create the view model with the settings
    }

    private async Task ChannelsUpdated(object sender, AirChannelsCollection channels) {
        if (sender.GetType() == typeof(ChannelsViewModel)) { return; } // if this sent the msg, ignore it
        _channels = channels;
        if (channels.GetActiveChannel is null) { return; }

        var activeChannel = channels.GetActiveChannel;
        var type = activeChannel.ChannelType; 

        if (type != CurrentType) {
            ActiveToolIndex = (int)type;
            CurrentChannelViewModel = type.ToViewModel(); //create the view model with the settings
        }
        
    }

    private async Task SettingsUpdated(object sender, AirChannelSettings settings) {
        if (settings.SelectedType != _settings.SelectedType) {
            ActiveToolIndex = (int)settings.SelectedType;
            CurrentChannelViewModel = settings.SelectedType.ToViewModel(); //create the view model with the settings
        }

        _settings = settings;
    }

    #region Commands

    [RelayCommand]
    private void ClearChannels() {
        WeakReferenceMessenger.Default.Send(new ChannelClearMessage());
    }

    [RelayCommand]
    private void DeleteChannel() {
        if (_channels.GetActiveChannel is null) { return; }

        WeakReferenceMessenger.Default.Send(new ChannelRemoveMessage(_channels.GetActiveChannel.GUID));
    }

    #endregion
}
