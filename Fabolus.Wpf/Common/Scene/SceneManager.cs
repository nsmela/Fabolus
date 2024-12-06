using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Mesh;
using static Fabolus.Wpf.Bolus.BolusStore;
using HelixToolkit.Wpf.SharpDX;
using System.Windows.Media.Media3D;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;
using Material = HelixToolkit.Wpf.SharpDX.Material;

namespace Fabolus.Wpf.Common.Scene;

public class SceneManager : IDisposable   {

    protected virtual Material _skin { get; } = PhongMaterials.Gray; 

    public SceneManager() {
        //messaging
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this,  (r, m) => UpdateDisplay(m.Bolus));

        var bolus = WeakReferenceMessenger.Default.Send(new BolusRequestMessage()).Response;
        UpdateDisplay(bolus);

    }

    protected virtual void UpdateDisplay(BolusModel? bolus) {
        if (bolus is null || bolus.Geometry is null || bolus.Geometry.Positions is null || bolus.Geometry.Positions.Count == 0) {
            WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessasge([]));
            return;
        }

        var display = new DisplayModel3D {
            Geometry = bolus.Geometry,
            Transform = MeshHelper.TransformEmpty,
            Skin = _skin
        };

        WeakReferenceMessenger.Default.Send(new MeshDisplayUpdatedMessasge([display]));
    }

    public void Dispose() {
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}

