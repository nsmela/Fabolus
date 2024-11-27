using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Bolus;
using Fabolus.Wpf.Common.Mesh;
using HelixToolkit.Wpf.SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Fabolus.Wpf.Stores.BolusStore;
using System.Windows.Media.Media3D;
using System.Windows.Media;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;

namespace Fabolus.Wpf.Pages.Rotate;
public partial class RotateMeshViewModel : BaseMeshViewModel {
    [ObservableProperty] public MeshGeometry3D _model;
    public LineGeometry3D Grid { get; private set; }

    public PhongMaterial RedMaterial => PhongMaterials.Red;

    public SharpDX.Color GridColor { get; private set; }

    public Transform3D Model1Transform { get; private set; }

    public Transform3D GridTransform { get; private set; }

    public System.Windows.Media.Color DirectionalLightColor => Colors.White;
    public System.Windows.Media.Color AmbientLightColor => Colors.GhostWhite;

    public RotateMeshViewModel(BaseMeshViewModel? oldMeshViewModel, bool zoomWhenLaoded = false) : base(oldMeshViewModel, zoomWhenLaoded) {
        // camera setup
        if (oldMeshViewModel is null) {
            Camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera {
                Position = new Point3D(45, -75, 30),
                LookDirection = new Vector3D(65, 75, -30),
                UpDirection = new Vector3D(0, 0, 1)
            };
        }

        // floor plane grid
        Grid = GenerateGrid(-175, 175, -175, 175, 10);
        GridColor = SharpDX.Color.Blue;
        GridTransform = new TranslateTransform3D(0, 0, 0);

        // model trafos
        Model1Transform = new TranslateTransform3D(0, 0, 0);

        //messages
        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => Update(m.bolus));

        BolusModel bolus = WeakReferenceMessenger.Default.Send<BolusRequestMessage>();

        Update(bolus);
    }

    private void Update(BolusModel? bolus) {
        if (bolus is null || bolus.Geometry is null) { return; }
        var geometry = bolus.Geometry;

        if (geometry is null || geometry.TriangleIndices.Count < 1) { return; }

        //building geometry model
        Model = geometry;
    }
}
