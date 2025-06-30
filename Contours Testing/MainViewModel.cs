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
using static Fabolus.Core.Meshes.PolygonTools.PolygonTools;
using static g3.SetGroupBehavior;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;

namespace Contours_Testing;

public partial class MainViewModel  : ObservableObject
{
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.MeshGeometry3D _model = new();
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.MeshGeometry3D _smoothModel = new();

    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.MeshGeometry3D _unionMesh = new();
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.MeshGeometry3D _bodyMesh = new();
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.MeshGeometry3D _toolMesh = new();

    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.Material _unionMaterial = PhongMaterials.Green;
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.Material _bodyMaterial = PhongMaterials.Blue;
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.Material _toolMaterial = PhongMaterials.Red;


    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.Material _modelMaterial = PhongMaterials.Blue;
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.Material _rawMaterial = DiffuseMaterials.Ruby;
    [ObservableProperty] private System.Windows.Media.Media3D.Transform3D _modelTransform;
    [ObservableProperty] private int _zLayer = -1;
    [ObservableProperty] private float _minZHeight = 0.0f;
    [ObservableProperty] private float _maxZHeight = 0.0f;
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.Camera? _mainCamera;
    [ObservableProperty] private IEffectsManager? _effectsManager;
    [ObservableProperty] private System.Windows.Media.Color _directionalLightColor = Colors.White;
    [ObservableProperty] private System.Windows.Media.Color _ambientLightColor = Colors.GhostWhite;
    [ObservableProperty] private LineGeometryModel3D _gridModel = new LineGeometryModel3D();
    [ObservableProperty] private string _title = "Contours Testing";
    [ObservableProperty] private Plane _plane1 = new(new Vector3(0, 0, -1), 8);
    [ObservableProperty] private Plane _plane2 = new(new Vector3(0, 0, -1), 8.01f);

    public ObservableElement3DCollection CurrentModel { get; init; } = new ObservableElement3DCollection();
    private SynchronizationContext context = SynchronizationContext.Current;

    private MeshModel _raw_mesh;
    private MeshModel _smooth_mesh;
    private Dictionary<int, ComparitivePolygon> _contours = [];

    partial void OnZLayerChanged(int value) {
        //Plane1 = new(new Vector3(0, 0, -1), -value);
        //Plane2 = new Plane(new Vector3(0, 0, -1), -value - 1f);
        SetLayer(value);
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

        ModelTransform = new TranslateTransform3D(0, 0, 0);

        // import smoothed model
        const string smooth_model_path = """C:\Users\nsmela\source\repos\nsmela\Fabolus\files\ear_bolus_smoothed.stl""";
        _smooth_mesh = MeshModel.FromFile(smooth_model_path).Result;
        _smooth_mesh = MeshTools.OrientationCentre(_smooth_mesh);
        SmoothModel = ToGeometry(_smooth_mesh);

        // import model
        const string raw_model_path = """C:\Users\nsmela\source\repos\nsmela\Fabolus\files\ear_bolus.stl""";
        _raw_mesh = MeshModel.FromFile(raw_model_path).Result;
        _raw_mesh = MeshTools.OrientationCentre(_raw_mesh);
        Model = ToGeometry(_raw_mesh);

        SetContourMesh();
    }

    private void SetContourMesh() {
        MinZHeight = (int)(SmoothModel.Bound.Minimum.Z + 1 );
        MaxZHeight = (int)(SmoothModel.Bound.Maximum.Z - 1);

        int count = (int)(MaxZHeight - MinZHeight);

        _contours.Clear();
        double height = 0.0;
        ComparitivePolygon? polygon;
        for(int i = 0; i < MaxZHeight; i ++) {
            try {
                height = MinZHeight + i ;
                polygon = MeshTools.Contouring.ContourMesh(_smooth_mesh, _raw_mesh, height);
                if (polygon is null) { continue; }

                _contours[i] = polygon;
                
            } catch (Exception ex) { } // ignore empty contours

        }

        ZLayer = count / 2;
        SetLayer(ZLayer);
    }

    private void SetLayer(int layer) {
        if (_contours.TryGetValue(layer, out var polygon)) {
            UnionMesh = ToGeometry(polygon.UnionMesh);
            BodyMesh = ToGeometry(polygon.BodyMesh);
            ToolMesh = ToGeometry(polygon.ToolMesh);

        } else {
            UnionMesh = new();
            BodyMesh = new();
            ToolMesh = new();
        }
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
