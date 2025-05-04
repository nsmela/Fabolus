using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Common.Scene;
using static Fabolus.Wpf.Bolus.BolusStore;
using Fabolus.Wpf.Features.Channels;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using HelixToolkit.Wpf.SharpDX;
using System.Windows.Input;
using Material = HelixToolkit.Wpf.SharpDX.Material;
using Fabolus.Wpf.Features;
using System;
using Fabolus.Core.AirChannel;

namespace Fabolus.Wpf.Pages.Channels;

public class ChannelsSceneManager : SceneManager {

    private BolusModel _bolus;
    private AirChannelsCollection _channels = [];
    private AirChannelSettings _settings;
    private MeshGeometry3D? _previewMesh;
    private IAirChannel _activeChannel;
    private Material _channelSkin = DiffuseMaterials.Emerald;
    private Material _selectedSkin = DiffuseMaterials.Turquoise;

    private Guid? BolusId => _bolus?.Geometry?.GUID;
    private float MaxHeight => (float)_bolus.Mesh.Height;

    private IAirChannel? GetChannelByGeometryId(Guid id) {
        if (!_channels.Any(c => c.Value.Geometry.GUID == id)) {
            return null;
        }

        return _channels.FirstOrDefault(c => c.Value.Geometry.GUID == id).Value;
    }

    public ChannelsSceneManager() {
        SetMessaging();
    }

    protected override void SetMessaging() {
        WeakReferenceMessenger.Default.UnregisterAll(this);

        //bolus
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, async (r, m) => await BolusUpdated(m.Bolus));

        //mouse actions
        WeakReferenceMessenger.Default.Register<MeshMouseDownMessage>(this, (r, m) => OnMouseDown(m.Hits, m.OriginalEventArgs));
        WeakReferenceMessenger.Default.Register<MeshMouseMoveMessage>(this, (r, m) => OnMouseMove(m.Hits, m.OriginalEventArgs));
        WeakReferenceMessenger.Default.Register<MeshMouseUpMessage>(this, (r, m) => OnMouseUp(m.Hits, m.OriginalEventArgs));

        WeakReferenceMessenger.Default.Register<ChannelSettingsUpdatedMessage>(this, async (r, m) => await SettingsUpdated(m.Settings));
        WeakReferenceMessenger.Default.Register<AirChannelsUpdatedMessage>(this, async (r, m) => await ChannelsUpdated(m.Channels));
        WeakReferenceMessenger.Default.Register<ActiveChannelUpdatedMessage>(this, async (r, m) => await ActiveAirChannelUpdated(m.Channel));

        _settings = WeakReferenceMessenger.Default.Send(new ChannelsSettingsRequestMessage()).Response;
        _activeChannel = WeakReferenceMessenger.Default.Send(new ActiveChannelRequestMessage()).Response;
        _channels = WeakReferenceMessenger.Default.Send(new AirChannelsRequestMessage()).Response;
 
        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage()).Response;
        BolusUpdated(bolus);
    }

    private async Task ActiveAirChannelUpdated(IAirChannel channel) {
        _activeChannel = channel;
        UpdateDisplay(null);
    }
    
    private async Task BolusUpdated(BolusModel? bolus) {
        _bolus = bolus;
        UpdateDisplay(null);
    }

    private async Task ChannelsUpdated(AirChannelsCollection channels) {
        _channels = channels;
        UpdateDisplay(null);
    }

    private async Task SettingsUpdated(AirChannelSettings settings) {
        _settings = settings;
        UpdateDisplay(null);
    }

    protected override void OnMouseDown(List<HitTestResult> hits, InputEventArgs args) {
        //catch and ignored mouse buttons and exit
        var mouse = args as MouseButtonEventArgs;
        if (mouse.RightButton == MouseButtonState.Pressed
            || mouse.MiddleButton == MouseButtonState.Pressed) {
            return;
        }

        //clear if nothing was hit
        _previewMesh = null;
        if (hits is null || hits.Count() == 0) {
            //if nothing hit, then deselect the channel
            UpdateSelectedChannel(null);
            return;
        }

        //check if clicked on an air channel
        var id = hits[0].Geometry.GUID;
        var channelHit = GetChannelByGeometryId(id);

        if (channelHit is not null) {
            UpdateSelectedChannel(channelHit);
            return;
        }

        //check if clicked on the bolus
        var bolusHit = hits.FirstOrDefault(x => x.Geometry.GUID == BolusId);
        AddChannel(bolusHit); // will also update display via response from messaging

    }

    protected override void OnMouseMove(List<HitTestResult> hits, InputEventArgs args) {
        _previewMesh = null;
        if (hits is null || hits.Count() == 0) {
            UpdateDisplay(null);
            return; 
        }

        var mouse = args as MouseEventArgs;
        if (mouse.RightButton == MouseButtonState.Pressed
            || mouse.MiddleButton == MouseButtonState.Pressed
            || mouse.LeftButton == MouseButtonState.Pressed) {
            UpdateDisplay(null);
            return;
        }

        //check if over an air channel
        var id = hits[0].Geometry.GUID;
        var channelHit = GetChannelByGeometryId(id);

        if (channelHit is not null) {
            UpdateDisplay(null);
            return;
        }

        //check if clicked on the bolus
        var bolusHit = hits.FirstOrDefault(x => x.Geometry.GUID == BolusId);
        SetPreviewChannel(bolusHit);
    }

    protected override void UpdateDisplay(BolusModel? bolus) {
        if (_bolus is null || _bolus.Geometry is null || _bolus.Geometry.Positions is null || _bolus.Geometry.Positions.Count == 0) {
            WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage([]));
            return;
        }

        var models = new List<DisplayModel3D>();

        models.Add( new DisplayModel3D {
            Geometry = _bolus.Geometry,
            Transform = MeshHelper.TransformEmpty,
            Skin = _skin
        });

        foreach(var channel in _channels.Values) {
            models.Add(new DisplayModel3D {
                Geometry = channel.Geometry,
                Transform = MeshHelper.TransformEmpty,
                Skin = channel.GUID == _activeChannel.GUID
                 ? _selectedSkin
                 : _channelSkin
            });
        }

        if (_previewMesh is not null) {
            models.Add(new DisplayModel3D {
                Geometry = _previewMesh,
                Transform = MeshHelper.TransformEmpty,
                Skin = DiffuseMaterials.Ruby
            });
        }

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage(models));
    }

    private async Task UpdateSelectedChannel(IAirChannel? channel) {
        if (channel is null) {
            var newChannel = _settings[_activeChannel.ChannelType];
            WeakReferenceMessenger.Default.Send(new ActiveChannelUpdatedMessage(newChannel));
            return;
        }

        _settings[channel.ChannelType] = channel;
        WeakReferenceMessenger.Default.Send(new ChannelSettingsUpdatedMessage(_settings));
        WeakReferenceMessenger.Default.Send(new ActiveChannelUpdatedMessage(channel));
    }

    private async Task AddChannel(HitTestResult? hit) {
        if (hit is null) { return; }
        var channel = _settings[_activeChannel.ChannelType].New();
        channel.Height = MaxHeight;
        channel = channel.WithHit(hit);
        _channels.Add(channel);

        WeakReferenceMessenger.Default.Send(new AirChannelsUpdatedMessage(_channels));
        WeakReferenceMessenger.Default.Send(new ActiveChannelUpdatedMessage(channel));
    }

    private async Task SetPreviewChannel(HitTestResult? hit) {
        if (hit is null) { 
            _channels.PreviewChannel = null;
        }
        else {
            var channel = _settings[_activeChannel.ChannelType].New();
            channel.Height = MaxHeight;
            channel = channel.WithHit(hit, true);
            _channels.PreviewChannel = channel;
            _previewMesh = channel.Geometry;
        }

        UpdateDisplay(null);
    }
}
