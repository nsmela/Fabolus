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
    public LineGeometry3D Lines { get; private set; }
    public LineGeometry3D Grid { get; private set; }

    public PhongMaterial RedMaterial { get; private set; }
    public PhongMaterial GreenMaterial { get; private set; }
    public PhongMaterial BlueMaterial { get; private set; }
    public SharpDX.Color GridColor { get; private set; }

    public Transform3D Model1Transform { get; private set; }
    public Transform3D Model2Transform { get; private set; }
    public Transform3D Model3Transform { get; private set; }
    public Transform3D GridTransform { get; private set; }

    public System.Windows.Media.Color DirectionalLightColor { get; private set; }
    public System.Windows.Media.Color AmbientLightColor { get; private set; }

    public ImportMeshViewModel(bool? zoom = false) {
        // titles
        Title = "";
        SubTitle = "";

        // camera setup
        Camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera {
            Position = new Point3D(3, 3, 5),
            LookDirection = new Vector3D(-3, -3, -5),
            UpDirection = new Vector3D(0, 1, 0)
        };

        EffectsManager = new DefaultEffectsManager();

        // setup lighting            
        AmbientLightColor = Colors.GhostWhite;
        DirectionalLightColor = Colors.White;

        // floor plane grid
        Grid = LineBuilder.GenerateGrid();
        GridColor = SharpDX.Color.Blue;
        GridTransform = new TranslateTransform3D(0, 0, 0);

        // scene model3d
        var b1 = new MeshBuilder();
        b1.AddSphere(new Vector3(0, 0, 0), 0.5);
        b1.AddBox(new Vector3(0, 0, 0), 1, 0.5, 2, BoxFaces.All);

        var meshGeometry = b1.ToMeshGeometry3D();
        meshGeometry.Colors = new Color4Collection(meshGeometry.TextureCoordinates.Select(x => x.ToColor4()));
        Model = meshGeometry;

        // lines model3d
        var e1 = new LineBuilder();
        e1.AddBox(new Vector3(0, 0, 0), 1, 0.5, 2);
        Lines = e1.ToLineGeometry3D();

        // model trafos
        Model1Transform = new TranslateTransform3D(0, 0, 0);
        Model2Transform = new TranslateTransform3D(-2, 0, 0);
        Model3Transform = new TranslateTransform3D(+2, 0, 0);

        // model materials
        RedMaterial = PhongMaterials.Red;
        GreenMaterial = PhongMaterials.Green;
        BlueMaterial = PhongMaterials.Blue;

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
