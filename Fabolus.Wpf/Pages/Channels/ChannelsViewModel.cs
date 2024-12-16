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

    private bool _isBusy = false;
    partial void OnActiveToolIndexChanged(int value) {
        if (_isBusy) { return; }
        _isBusy = true;

        var settings = _settings;
        settings.SetSelectedType((ChannelTypes)value);
        WeakReferenceMessenger.Default.Send(new ChannelSettingsUpdatedMessage(settings));
        
        _isBusy = false;
    }

    private AirChannelsCollection _channels = [];
    private AirChannelSettings _settings;

    public ChannelsViewModel() {
        ChannelNames = EnumHelper.GetEnumDescriptions<ChannelTypes>().ToArray();

        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<AirChannelsUpdatedMessage>(this, async (r, m) => await ChannelsUpdated(m.Channels));
        WeakReferenceMessenger.Default.Register<ChannelSettingsUpdatedMessage>(this, async (r, m) => await SettingsUpdated(m.Settings));

        var settings = WeakReferenceMessenger.Default.Send(new ChannelsSettingsRequestMessage()).Response;
        SettingsUpdated(settings);

        var channels = WeakReferenceMessenger.Default.Send(new AirChannelsRequestMessage()).Response;
        ChannelsUpdated(channels);
    }

    private async Task ChannelsUpdated(AirChannelsCollection channels) {
        _channels = channels;
    }

    private async Task SettingsUpdated(AirChannelSettings settings) {
        if (_settings is null || _settings.SelectedType != settings.SelectedType) {
            CurrentChannelViewModel = settings.SelectedType.ToViewModel(); //create the view model with the settings
            if ((int)settings.SelectedType != ActiveToolIndex) { ActiveToolIndex = (int)settings.SelectedType; }
        }

        _settings = settings;
    }

    #region Commands

    [RelayCommand]
    private void ClearChannels() {
        WeakReferenceMessenger.Default.Send(new AirChannelsUpdatedMessage(_channels.Clear()));
    }

    [RelayCommand]
    private void DeleteChannel() {
        if (_channels.GetActiveChannel is null) { return; }

        _channels.RemoveActiveChannel();
        WeakReferenceMessenger.Default.Send(new AirChannelsUpdatedMessage(_channels));
    }

    #endregion
}
