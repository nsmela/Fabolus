using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.AirChannel;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Helpers;
using Fabolus.Wpf.Common.Scene;
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

        WeakReferenceMessenger.Default.Send(new SetChannelTypeMessage((ChannelTypes)value));
        
        _isBusy = false;
    }

    private AirChannel _previewChannel;
    private AirChannel[] _channelsList = [];

    public ChannelsViewModel() {
        ChannelNames = EnumHelper.GetEnumDescriptions<ChannelTypes>().ToArray();

        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<AirChannelsUpdatedMessage>(this, async (r, m) => await ChannelsUpdated(m.Channels));
        WeakReferenceMessenger.Default.Register<ChannelSettingsUpdatedMessage>(this, async (r, m) => await PreviewUpdated(m.Settings));
        WeakReferenceMessenger.Default.Register<SetSelectedChannelMessage>(this, async (r, m) => await SetSelectedChannel(m.Channel));

        var preview = WeakReferenceMessenger.Default.Send(new ChannelsSettingsRequestMessage()).Response;
        PreviewUpdated(preview);
    }

    private async Task ChannelsUpdated(AirChannel[] channels) {
        _channelsList = channels;
        //updating listing
    }

    private async Task PreviewUpdated(AirChannel? preview) {
        //preview acts as the settings holder
        //update which type of channel view to show
        if (preview is null) { return; }
        if (_previewChannel is not null && preview.ChannelType == _previewChannel.ChannelType) { return; }

        //state machine, end previous view and start new view
        _previewChannel = preview;
        CurrentChannelViewModel = preview.ChannelType.ToViewModel();

        if ((int)preview.ChannelType != ActiveToolIndex) { ActiveToolIndex = (int)preview.ChannelType; }
    }

    protected async Task SetSelectedChannel(AirChannel? settings) {
        //if null, use the preview channel instead
        if (settings is null) { return; }

        //preview matches selected channel
        if (_previewChannel is not null && settings.ChannelType != _previewChannel.ChannelType) {
            CurrentChannelViewModel = settings.ChannelType.ToViewModel(settings); //create the view model with the settings
            if ((int)settings.ChannelType != ActiveToolIndex) { ActiveToolIndex = (int)settings.ChannelType; }
        }

        _previewChannel = settings;


    }

    #region Commands

    [RelayCommand]
    private void ClearChannels() {
        WeakReferenceMessenger.Default.Send(new ClearAirChannelsMessage());
    }

    #endregion
}
