using CommunityToolkit.Mvvm.ComponentModel;
using Fabolus.Core.Extensions;
using Fabolus.Core.Meshes;
using Fabolus.Core.Meshes.MeshTools;
using g3;
using HelixToolkit;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using SharpDX.Direct2D1.Effects;
using SharpDX.Direct3D11;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Xml.Serialization;
using static g3.SetGroupBehavior;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;

namespace Contours_Testing;

public partial class MainViewModel  : ObservableObject
{
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.MeshGeometry3D _model = new();
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.MeshGeometry3D _dragModel = new();
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.Material _modelMaterial = PhongMaterials.Blue;
    [ObservableProperty] private System.Windows.Media.Media3D.Transform3D _modelTransform;
    [ObservableProperty] private float _zHeight = 0.0f;
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.Camera? _mainCamera;
    [ObservableProperty] private IEffectsManager? _effectsManager;
    [ObservableProperty] private System.Windows.Media.Color _directionalLightColor = Colors.White;
    [ObservableProperty] private System.Windows.Media.Color _ambientLightColor = Colors.GhostWhite;
    [ObservableProperty] private LineGeometryModel3D _gridModel = new LineGeometryModel3D();
    [ObservableProperty] private string _title = "Contours Testing";
    [ObservableProperty] private Plane _plane1 = new(new Vector3(0, 0, -1), 8);

    public ObservableElement3DCollection CurrentModel { get; init; } = new ObservableElement3DCollection();
    private SynchronizationContext context = SynchronizationContext.Current;

    partial void OnZHeightChanged(float value) {
        Plane1 = new(new Vector3(0, 0, -1), value);
    }

    protected HelixToolkit.Wpf.SharpDX.OrthographicCamera defaultOrthographicCamera = new() {
        Position = new Point3D(0, 0, 5),
        LookDirection = new Vector3D(-0, -0, -5),
        UpDirection = new Vector3D(0, 1, 0),
        NearPlaneDistance = 1,
        FarPlaneDistance = 100
    };

    public static System.Windows.Media.Media3D.Transform3D TransformFromAxis(Vector3D axis, float angle) {
        var rotation = new AxisAngleRotation3D(axis, angle);
        var rotate = new RotateTransform3D(rotation);
        return new Transform3DGroup { Children = [rotate] };
    }

    public MainViewModel() {
        // Initialize any properties or commands here
        // For example, you could set default values or load initial data
        
        MainCamera = defaultOrthographicCamera;
        GridModel.Geometry = GenerateGrid();
        EffectsManager = new DefaultEffectsManager();

        ModelTransform = new TranslateTransform3D(0, 0, ZHeight);

        // import model
        const string raw_model_path = """C:\Users\nsmela\source\repos\nsmela\Fabolus\files\ear_bolus.stl""";
        var mesh = MeshModel.FromFile(raw_model_path).Result;
        mesh = MeshTools.OrientationCentre(mesh);
        var model = ToGeometry(mesh);
        Model = model;

        context.Post((o) => {
            CurrentModel.Clear();

            model.UpdateOctree();
            model.UpdateBounds();
            CurrentModel.Add(new MeshGeometryModel3D {
                Geometry = model,
                Material = _modelMaterial,
                Transform = _modelTransform,
            });

        }, null);
    }

    protected LineGeometry3D GenerateGrid(float minX = -100, float maxX = 100, float minY = -100, float maxY = 100, float spacing = 10) {
        var grid = new LineBuilder();

        var x_spacing = maxX - minX;
        var y_spacing = maxY - minY;

        for (int i = 0; i <= x_spacing / spacing; i++) {
            grid.AddLine(
                new Vector3(minX + spacing * i, minY, 0),
                new Vector3(minX + spacing * i, maxY, 0));
        }

        for (int i = 0; i <= y_spacing / spacing; i++) {
            grid.AddLine(
                new Vector3(minX, minY + spacing * i, 0),
                new Vector3(maxX, minY + spacing * i, 0));
        }

        return grid.ToLineGeometry3D();
    }

    public static HelixToolkit.Wpf.SharpDX.MeshGeometry3D ToGeometry(MeshModel mesh) {
        if (mesh.IsEmpty()) { return new MeshGeometry3D(); }

        var geometry = new MeshBuilder(true, false, false);
        geometry.Append(
            mesh.Vectors().Select(values => ToVector3(values)).ToList(), //3d vert positions
            mesh.Triangles().ToList(), //index of each triangle's vertex
            mesh.Normals().Select(values => ToVector3(values)).ToList(), //normals
            null); // texture coordinates

        return geometry.ToMeshGeometry3D();
    }

    public static Vector3 ToVector3(double[] values) =>
        new((float)values[0], (float)values[1], (float)values[2]);
}
