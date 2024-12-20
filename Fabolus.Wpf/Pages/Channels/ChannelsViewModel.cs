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
    private ChannelTypes CurrentType {
        get => (ChannelTypes)ActiveToolIndex;
        set => ActiveToolIndex = (int)value;
    }

    partial void OnActiveToolIndexChanged(int value) {
        if (_isBusy) { return; }
        _isBusy = true;

        _channels.SetActiveChannel(null); //clears current selected channel when selecting a new channel type
        WeakReferenceMessenger.Default.Send(new AirChannelsUpdatedMessage(_channels));

        _settings.SetSelectedType((ChannelTypes)value);
        WeakReferenceMessenger.Default.Send(new ChannelSettingsUpdatedMessage(_settings));
        CurrentChannelViewModel = _settings.SelectedType.ToViewModel(); //create the view model with the settings
       
        _isBusy = false;
    }

    private AirChannelsCollection _channels = [];
    private AirChannelSettings _settings;

    public ChannelsViewModel() {
        ChannelNames = EnumHelper.GetEnumDescriptions<ChannelTypes>().ToArray();

        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<AirChannelsUpdatedMessage>(this, async (r, m) => await ChannelsUpdated(m.Channels));
        WeakReferenceMessenger.Default.Register<ChannelSettingsUpdatedMessage>(this, async (r, m) => await SettingsUpdated(m.Settings));

        _settings = WeakReferenceMessenger.Default.Send(new ChannelsSettingsRequestMessage()).Response;

        var channels = WeakReferenceMessenger.Default.Send(new AirChannelsRequestMessage()).Response;
        _channels.SetActiveChannel(null);
        _channels = channels;

        CurrentType = _settings.SelectedType; //set active index and generates view model
        CurrentChannelViewModel = _settings.SelectedType.ToViewModel(); //create the view model with the settings
    }

    private async Task ChannelsUpdated(AirChannelsCollection channels) {
        _channels = channels;
        if (channels.GetActiveChannel is null) { return; }

        var activeChannel = channels.GetActiveChannel;

        var type = activeChannel.ChannelType; 

        if (type != CurrentType) {
            _isBusy = true;
            ActiveToolIndex = (int)type;
            _isBusy = false;

            CurrentChannelViewModel = type.ToViewModel(); //create the view model with the settings
        }
        
    }

    private async Task SettingsUpdated(AirChannelSettings settings) {
        if (_isBusy) { return; }

        _settings = settings;
    }

    #region Commands

    [RelayCommand]
    private void ClearChannels() {
        WeakReferenceMessenger.Default.Send(new AirChannelsUpdatedMessage(_channels.Clear()));

        //clearing path points in settings
        _settings.RemoveActiveChannel();
        WeakReferenceMessenger.Default.Send(new ChannelSettingsUpdatedMessage(_settings));
    }

    [RelayCommand]
    private void DeleteChannel() {
        if (_channels.GetActiveChannel is null) { return; }

        _channels.RemoveActiveChannel();
        WeakReferenceMessenger.Default.Send(new AirChannelsUpdatedMessage(_channels));

        //clearing path points in settings
        _settings.RemoveActiveChannel();
        WeakReferenceMessenger.Default.Send(new ChannelSettingsUpdatedMessage(_settings));
    }

    #endregion
}
