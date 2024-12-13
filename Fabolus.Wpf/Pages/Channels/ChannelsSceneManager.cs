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
    private Material _channelSkin = DiffuseMaterials.Emerald;
    private Material _selectedSkin = DiffuseMaterials.LightGreen;

    private Guid? _selectedAirChannel;
    private Guid? _bolusId;

    public ChannelsSceneManager() : base() {
    }

    protected override void SetDefaultInputBindings() => WeakReferenceMessenger.Default.Send(new MeshSetInputBindingsMessage(
    LeftMouseButton: ViewportCommands.Pan,
    MiddleMouseButton: ViewportCommands.Zoom,
    RightMouseButton: ViewportCommands.Rotate));

    protected override void OnMouseDown(object? sender, Mouse3DEventArgs args) {
        if (_bolusId is null) { throw new NullReferenceException("Bolus id is null!"); }

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

            _channels.Clear();
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

    protected override void OnMouseMove(List<HitTestResult> hits, InputEventArgs args) {
        if (hits is null || hits.Count() == 0) { return; }

        var meshHit = hits[0];
        var meshId = meshHit.Geometry.GUID;
        if (meshId == _bolusId) {
            var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
            var point = meshHit.PointHit;
            var channel = new AngledAirChannel(
                depth: 2.0f,
                diameter: 5.0f,
                height: bolus.Response.Geometry.Bound.Height,
                origin: point,
                normal: meshHit.NormalAtHit);

            _channels.Clear();
            _channels.Add(channel.GUID, channel);
            _selectedAirChannel = channel.GUID;
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

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage(models));
    }
}
