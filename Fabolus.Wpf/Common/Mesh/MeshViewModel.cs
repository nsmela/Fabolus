using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using SharpDX;
using HelixToolkit.Wpf.SharpDX;
using System.Windows.Media;

namespace Fabolus.Wpf.Common.Mesh;
public class MeshViewModel : BaseMeshViewModel
{
    public HelixToolkit.Wpf.SharpDX.MeshGeometry3D Model { get; private set; }
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

    public MeshViewModel(bool? zoom = false)
    {
        // titles
        Title = "";
        SubTitle = "";

        // camera setup
        Camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera
        {
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
        //WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => { Update(m.bolus.Geometry); });

        //BolusModel bolus = WeakReferenceMessenger.Default.Send<BolusRequestMessage>();
        //Update(bolus.Geometry);
    }

    private void Update(System.Windows.Media.Media3D.MeshGeometry3D bolus)
    {
        DisplayMesh.Children.Clear();

        if (bolus is null || bolus.TriangleIndices.Count < 1) { return; }

        //building geometry model
        var model = MeshSkins.SkinModel(bolus, MeshSkins.Bolus);
        DisplayMesh.Children.Add(model);
    }

}

