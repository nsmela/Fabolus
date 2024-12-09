using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Common.Scene;
using static Fabolus.Wpf.Bolus.BolusStore;
using Fabolus.Wpf.Features.Channels;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System.Windows.Input;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;
using Material = HelixToolkit.Wpf.SharpDX.Material;

namespace Fabolus.Wpf.Pages.Channels;

public class ChannelsSceneManager : SceneManager {

    private Dictionary<Guid, AirChannel> _channels = [];
    private Material _channelSkin = DiffuseMaterials.Emerald;
    private Material _selectedSkin = DiffuseMaterials.LightGreen;

    private MeshGeometry3D Sphere(Vector3 point, double radius) {
        var builder = new MeshBuilder();
        builder.AddSphere(point, radius);
        return builder.ToMeshGeometry3D();
    }

    private Guid? _selectedAirChannel;
    private Guid? _bolusId;

    public ChannelsSceneManager() : base() {

    }

    protected override void OnMouseDown(object? sender, Mouse3DEventArgs args) {
        if (_bolusId is null) { throw new NullReferenceException("Bolus id is null!"); }

        var e = (MouseEventArgs)args.OriginalInputEventArgs;
        if (e.LeftButton == MouseButtonState.Released) { return; }

        var meshHit = (MeshGeometryModel3D)args.HitTestResult.ModelHit;
        var meshId = meshHit.Geometry.GUID;

        //hit the mesh
        if (meshId == _bolusId) {
            var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
            var point = args.HitTestResult.PointHit;
            var channel = new AirChannel(
                type: Core.AirChannel.ChannelTypes.Straight,
                depth: 2.0f,
                diameter: 5.0f,
                height:bolus.Response.Geometry.Bound.Height,
                origin: point);

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

    protected override void OnMouseMove(object? sender, Mouse3DEventArgs args) {
        base.OnMouseMove(sender, args);
    }

    protected override void OnMouseUp(object? sender, Mouse3DEventArgs args) {
        base.OnMouseUp(sender, args);
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
