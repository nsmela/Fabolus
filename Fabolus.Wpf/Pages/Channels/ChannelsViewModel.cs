using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.AirChannel;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Helpers;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Features;
using Fabolus.Wpf.Features.Channels;
using Fabolus.Wpf.Pages.MainWindow;
using System.Windows.Controls;

namespace Fabolus.Wpf.Pages.Channels;

public partial class ChannelsViewModel : BaseViewModel {
    public override string TitleText => "channels";

    [ObservableProperty] private BaseChannelsViewModel? _currentChannelViewModel;
    [ObservableProperty] private string[] _channelNames = [];
    [ObservableProperty] private int _activeToolIndex = 0;

    private bool _autoload_channels;

    private ChannelTypes CurrentType {
        get => (ChannelTypes)ActiveToolIndex;
        set => ActiveToolIndex = (int)value;
    }

    partial void OnActiveToolIndexChanged(int value) {
    // the type of channel the channels settings view model will display 
    // a change here assumes the new value doesn't match old one
        if (_isBusy) { return; }

        var channelType = (ChannelTypes)value;
        var activeChannel = _settings[channelType];
        WeakReferenceMessenger.Default.Send(new ActiveChannelUpdatedMessage(activeChannel));
    }

    private AirChannelsCollection _channels = [];
    private AirChannelSettings _settings;
    private IAirChannel? _activeChannel;
    private bool _isBusy = false;

    protected override void RegisterMessages() {
        WeakReferenceMessenger.Default.Register<ChannelSettingsUpdatedMessage>(this, async (r, m) => await SettingsUpdated(r, m.Settings));
        WeakReferenceMessenger.Default.Register<ActiveChannelUpdatedMessage>(this, async (r, m) => await ActiveChannelChanged(m.Channel));
        WeakReferenceMessenger.Default.Register<AirChannelsUpdatedMessage>(this, (r, m) => _channels = m.Channels);
    }

    public ChannelsViewModel() : base(new ChannelsSceneManager()) {
        RegisterMessages();
        
        ChannelNames = EnumHelper.GetEnumDescriptions<ChannelTypes>().ToArray();

        _channels = WeakReferenceMessenger.Default.Send(new AirChannelsRequestMessage()).Response;
        _settings = WeakReferenceMessenger.Default.Send(new ChannelsSettingsRequestMessage()).Response;
        _activeChannel = WeakReferenceMessenger.Default.Send(new ActiveChannelRequestMessage()).Response;

        CurrentType = _settings.SelectedType; //set active index and generates view model
        CurrentChannelViewModel = _settings.SelectedType.ToViewModel(); //create the view model with the settings

        WeakReferenceMessenger.Default.Send(new MeshInfoSetMessage(string.Empty));
    }

    private async Task ActiveChannelChanged(IAirChannel channel) {

        _isBusy = true;
        ActiveToolIndex = (int)channel.ChannelType;
        _isBusy = false;

        CurrentChannelViewModel = null;
        CurrentChannelViewModel = channel.ChannelType.ToViewModel(); //create the view model with the settings

        _activeChannel = channel;
    }

    private async Task SettingsUpdated(object sender, AirChannelSettings settings) {
        if (settings.SelectedType != _settings.SelectedType) {
            ActiveToolIndex = (int)settings.SelectedType;
            CurrentChannelViewModel = settings.SelectedType.ToViewModel(); //create the view model with the settings
        }

        _settings = settings;
    }

    [RelayCommand]
    private void ClearChannels() {
        WeakReferenceMessenger.Default.Send(new AirChannelsUpdatedMessage([]));

        var activeChannel = _settings[_activeChannel.ChannelType];
        WeakReferenceMessenger.Default.Send(new ActiveChannelUpdatedMessage(activeChannel));
    }

    [RelayCommand]
    private void DeleteChannel() {
        if (_activeChannel is null) { return; }
        if (!_channels.ContainsKey(_activeChannel.GUID)) { return; }
        _channels.Remove(_activeChannel);

        WeakReferenceMessenger.Default.Send(new AirChannelsUpdatedMessage(_channels));

        var activeChannel = _settings[_activeChannel.ChannelType];
        WeakReferenceMessenger.Default.Send(new ActiveChannelUpdatedMessage(activeChannel));
    }



}
