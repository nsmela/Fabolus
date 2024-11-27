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

namespace Fabolus.Wpf.Common;
public class MeshViewModel : MeshViewModelBase 
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
            this.Title = "";
            this.SubTitle = "";

            // camera setup
            this.Camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera
            {
                Position = new Point3D(3, 3, 5), LookDirection = new Vector3D(-3, -3, -5),
                UpDirection = new Vector3D(0, 1, 0)
            };

            EffectsManager = new DefaultEffectsManager();

            // setup lighting            
            this.AmbientLightColor = Colors.GhostWhite;
            this.DirectionalLightColor = Colors.White;

            // floor plane grid
            this.Grid = LineBuilder.GenerateGrid();
            this.GridColor = SharpDX.Color.Blue;
            this.GridTransform = new TranslateTransform3D(0, 0, 0);

            // scene model3d
            var b1 = new MeshBuilder();
            b1.AddSphere(new Vector3(0, 0, 0), 0.5);
            b1.AddBox(new Vector3(0, 0, 0), 1, 0.5, 2, BoxFaces.All);

            var meshGeometry = b1.ToMeshGeometry3D();
            meshGeometry.Colors = new Color4Collection(meshGeometry.TextureCoordinates.Select(x => x.ToColor4()));
            this.Model = meshGeometry;

            // lines model3d
            var e1 = new LineBuilder();
            e1.AddBox(new Vector3(0, 0, 0), 1, 0.5, 2);
            this.Lines = e1.ToLineGeometry3D();

            // model trafos
            this.Model1Transform = new TranslateTransform3D(0, 0, 0);
            this.Model2Transform = new TranslateTransform3D(-2, 0, 0);
            this.Model3Transform = new TranslateTransform3D(+2, 0, 0);

            // model materials
            this.RedMaterial = PhongMaterials.Red;
            this.GreenMaterial = PhongMaterials.Green;
            this.BlueMaterial = PhongMaterials.Blue;

        //messages
        WeakReferenceMessenger.Default.UnregisterAll(this);
        //WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => { Update(m.bolus.Geometry); });

        //BolusModel bolus = WeakReferenceMessenger.Default.Send<BolusRequestMessage>();
        //Update(bolus.Geometry);
    }

    private void Update(System.Windows.Media.Media3D.MeshGeometry3D bolus) {
        DisplayMesh.Children.Clear();

        if (bolus is null || bolus.TriangleIndices.Count < 1) { return; }

        //building geometry model
        var model = MeshSkins.SkinModel(bolus, MeshSkins.Bolus);
        DisplayMesh.Children.Add(model);
    }
}

