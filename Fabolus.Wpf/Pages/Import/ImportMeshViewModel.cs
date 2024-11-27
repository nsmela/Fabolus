using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Mesh;
using HelixToolkit.Wpf.SharpDX;
using System.Windows.Media;
using SharpDX;
using static Fabolus.Wpf.Stores.BolusStore;
using Fabolus.Core.Bolus;
using Transform3D = System.Windows.Media.Media3D.Transform3D;
using Point3D = System.Windows.Media.Media3D.Point3D;
using Vector3D = System.Windows.Media.Media3D.Vector3D;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Fabolus.Wpf.Pages.Import;
public partial class ImportMeshViewModel : BaseMeshViewModel {
    [ObservableProperty] public MeshGeometry3D _model;
    public LineGeometry3D Grid { get; private set; }

    public PhongMaterial RedMaterial => PhongMaterials.Red;

    public SharpDX.Color GridColor { get; private set; }

    public Transform3D Model1Transform { get; private set; }

    public Transform3D GridTransform { get; private set; }

    public System.Windows.Media.Color DirectionalLightColor => Colors.White;
    public System.Windows.Media.Color AmbientLightColor => Colors.GhostWhite;

    public ImportMeshViewModel(bool? zoom = false) {
        // titles
        Title = "";
        SubTitle = "";

        // camera setup
        Camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera {
            Position = new Point3D(45, -75, 30),
            LookDirection = new Vector3D(65, 75, -30),
            UpDirection = new Vector3D(0, 0, 1)
        };

        EffectsManager = new DefaultEffectsManager();

        // floor plane grid
        Grid = GenerateGrid(-175, 175, -175, 175, 10);
        GridColor = SharpDX.Color.Blue;
        GridTransform = new TranslateTransform3D(0, 0, 0);

        // model trafos
        Model1Transform = new TranslateTransform3D(0, 0, 0);

        //messages
        WeakReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => Update(m.bolus) );

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
