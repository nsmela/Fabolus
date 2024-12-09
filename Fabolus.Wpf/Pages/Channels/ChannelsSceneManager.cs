using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Channels;

public class ChannelsSceneManager : SceneManager {

    private List<Vector3> _points = [];
    private HelixToolkit.Wpf.SharpDX.MeshGeometry3D Sphere(Vector3 point, double radius) {
        var builder = new MeshBuilder();
        builder.AddSphere(point, radius);
        return builder.ToMeshGeometry3D();
    }

    public ChannelsSceneManager() : base() {

    }

    protected override void OnMouseDown(object? sender, Mouse3DEventArgs args) {
        var e = (MouseEventArgs)args.OriginalInputEventArgs;
        if (e.LeftButton == MouseButtonState.Released) { return; }

        _points.Add(args.HitTestResult.PointHit);
        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage());
        UpdateDisplay(bolus);
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

        models.Add( new DisplayModel3D {
            Geometry = bolus.Geometry,
            Transform = MeshHelper.TransformEmpty,
            Skin = _skin
        });

        _points.ForEach(x => models.Add(new DisplayModel3D {
            Geometry = Sphere(x, 2.0),
            Transform = MeshHelper.TransformEmpty,
            Skin = DiffuseMaterials.Emerald
        }));

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessage(models));
    }
}
