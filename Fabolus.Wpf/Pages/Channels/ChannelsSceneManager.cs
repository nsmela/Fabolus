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

using Fabolus.Wpf.Features.Channels.Angled;

namespace Fabolus.Wpf.Pages.Channels;

public class ChannelsSceneManager : SceneManager {

    private Dictionary<Guid, AirChannel> _channels = [];
    private AirChannel _preview;
    private MeshGeometry3D? _previewMesh;
    private Material _channelSkin = DiffuseMaterials.Emerald;
    private Material _selectedSkin = DiffuseMaterials.LightGreen;

    private Guid? _selectedAirChannel;
    private Guid? _bolusId;
    private float _maxHeight;

    public ChannelsSceneManager() : base() {
        WeakReferenceMessenger.Default.Register<ChannelSettingsUpdatedMessage>(this, async (r, m) => await SettingsUpdated(m.Settings));
        var preview = WeakReferenceMessenger.Default.Send(new ChannelsSettingsRequestMessage()).Response;
        SettingsUpdated(preview);
    }

    private async Task SettingsUpdated(AirChannel settings) {
        _preview = settings;
    }

    protected override void SetDefaultInputBindings() => WeakReferenceMessenger.Default.Send(new MeshSetInputBindingsMessage(
    LeftMouseButton: ViewportCommands.Pan,
    MiddleMouseButton: ViewportCommands.Zoom,
    RightMouseButton: ViewportCommands.Rotate));

    protected override void OnMouseDown(object? sender, Mouse3DEventArgs args) {

    }

    protected override void OnMouseMove(List<HitTestResult> hits, InputEventArgs args) {
        _previewMesh = null;
        if (hits is null || hits.Count() == 0) { return; }

        //check if clicked on an air channel
        var id = hits[0].Geometry.GUID;
        var channelHit = _channels.ContainsKey(id) 
            ? _channels[id]
            : null;

        if (channelHit is not null) {

            return;
        }

        //check if clicked on the bolus
        var bolusHit = hits.FirstOrDefault(x => x.Geometry.GUID == _bolusId);
        if (bolusHit is not null) {
            var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
            var channel = (_preview with { Height = _maxHeight }).WithHit(bolusHit);

            _previewMesh = channel.Geometry;
            UpdateDisplay(bolus);
            return;
        }

    }

    protected override void OnMouseUp(object? sender, Mouse3DEventArgs args) {
        if (_bolusId is null) { throw new NullReferenceException("Bolus id is null!"); }

        var e = (MouseEventArgs)args.OriginalInputEventArgs;
        if (e.LeftButton == MouseButtonState.Pressed) { return; }

        var meshHit = (MeshGeometryModel3D)args.HitTestResult.ModelHit;
        var meshId = meshHit.Geometry.GUID;
        var hitNormal = args.HitTestResult.NormalAtHit;

        //hit the mesh
        if (meshId == _bolusId) {
            var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
            var point = args.HitTestResult.PointHit;
            var channel = new AngledAirChannel(
                depth: 2.0f,
                diameter: 5.0f,
                height: bolus.Response.Geometry.Bound.Height,
                origin: point,
                normal: hitNormal);

            _channels.Add(channel.GUID, channel);
            _selectedAirChannel = channel.GUID;
            UpdateDisplay(bolus);
            return;
        }

        //hit an air channel
        if (_channels.ContainsKey(meshId)) {
            _selectedAirChannel = meshId;
            var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
            UpdateDisplay(bolus);
            return;
        }
    }

    protected override void UpdateDisplay(BolusModel? bolus) {
        if (bolus is null || bolus.Geometry is null || bolus.Geometry.Positions is null || bolus.Geometry.Positions.Count == 0) {
            WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage([]));
            return;
        }

        var models = new List<DisplayModel3D>();

        _bolusId = bolus.Geometry.GUID;
        _maxHeight = bolus.Geometry.Bound.Height + 10.0f;

        models.Add( new DisplayModel3D {
            Geometry = bolus.Geometry,
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
