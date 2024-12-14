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

namespace Fabolus.Wpf.Pages.Channels;

public class ChannelsSceneManager : SceneManager {

    private BolusModel _bolus;
    private Dictionary<Guid, AirChannel> _channels = [];
    private AirChannel _preview;
    private MeshGeometry3D? _previewMesh;
    private Material _channelSkin = DiffuseMaterials.Emerald;
    private Material _selectedSkin = DiffuseMaterials.Turquoise;

    private Guid? _selectedAirChannel;
    private Guid? BolusId => _bolus?.Geometry?.GUID;
    private float MaxHeight => _bolus?.Geometry?.Bound.Height ?? 50.0f;

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

        var preview = WeakReferenceMessenger.Default.Send(new ChannelsSettingsRequestMessage()).Response;
        _preview = preview;

        var channels = WeakReferenceMessenger.Default.Send(new AirChannelsRequestMessage()).Response;
        _channels = channels.ToDictionary(x => x.GUID);

        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage()).Response;
        BolusUpdated(bolus);
    }

    private async Task BolusUpdated(BolusModel? bolus) {
        _bolus = bolus;
        UpdateDisplay(null);
    }

    private async Task ChannelsUpdated(AirChannel[] channels) {
        _channels = channels.ToDictionary(x => x.GUID);
        UpdateDisplay(null);
    }

    private async Task SettingsUpdated(AirChannel settings) {
        _preview = settings;
    }

    protected override void OnMouseDown(List<HitTestResult> hits, InputEventArgs args) {
        _previewMesh = null;
        if (hits is null || hits.Count() == 0) { return; }

        var mouse = args as MouseButtonEventArgs;
        if (mouse.RightButton == MouseButtonState.Pressed
            || mouse.MiddleButton == MouseButtonState.Pressed) {
            return;
        }

        //check if clicked on an air channel
        var id = hits[0].Geometry.GUID;
        var channelHit = _channels.ContainsKey(id)
            ? _channels[id]
            : null;

        if (channelHit is not null) {
            _selectedAirChannel = id;
            UpdateDisplay(null);
            return;
        }

        //check if clicked on the bolus
        var bolusHit = hits.FirstOrDefault(x => x.Geometry.GUID == BolusId);
        if (bolusHit is not null) {
            var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
            var channel = (_preview with { Height = MaxHeight }).WithHit(bolusHit);
            _selectedAirChannel = channel.Geometry.GUID;

            WeakReferenceMessenger.Default.Send(new AddAirChannelMessage(channel));
            UpdateDisplay(bolus);
            return;
        }
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

        //check if clicked on an air channel
        var id = hits[0].Geometry.GUID;
        var channelHit = _channels.ContainsKey(id)
            ? _channels[id]
            : null;

        if (channelHit is not null) {
            UpdateDisplay(null);
            return;
        }

        //check if clicked on the bolus
        var bolusHit = hits.FirstOrDefault(x => x.Geometry.GUID == BolusId);
        if (bolusHit is not null) {
            var channel = (_preview with { Height = MaxHeight }).WithHit(bolusHit);

            _previewMesh = channel.Geometry;
            UpdateDisplay(null);
            return;
        }

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
                Skin = channel.GUID == _selectedAirChannel
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
}
