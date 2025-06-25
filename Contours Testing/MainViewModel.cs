using CommunityToolkit.Mvvm.ComponentModel;
using HelixToolkit;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using SharpDX.Direct2D1.Effects;
using SharpDX.Direct3D11;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;


namespace Contours_Testing;

public partial class MainViewModel  : ObservableObject
{
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.MeshGeometry3D _model = new();
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.MeshGeometry3D _dragModel = new();
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.Material _modelMaterial = PhongMaterials.Blue;
    [ObservableProperty] private System.Windows.Media.Media3D.Transform3D _modelTransform;
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.Camera? _mainCamera;
    [ObservableProperty] private IEffectsManager? _effectsManager;
    [ObservableProperty] private System.Windows.Media.Color _directionalLightColor = Colors.White;
    [ObservableProperty] private System.Windows.Media.Color _ambientLightColor = Colors.GhostWhite;
    [ObservableProperty] private LineGeometryModel3D _gridModel = new LineGeometryModel3D();
    [ObservableProperty] private string _title = "Contours Testing";
    [ObservableProperty] private Plane _plane1 = new(new Vector3(0, 0, 1), -8);

    private SynchronizationContext context = SynchronizationContext.Current;

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

        MeshBuilder builder = new();
        builder.AddSphere(new Vector3(0, 0, 0), 20.0f);
        Model = builder.ToMeshGeometry3D();


        builder = new();
        builder.AddBox(new Vector3(0, 0, 0), 10.0f, 10.0f, 10.0f);
        DragModel = builder.ToMeshGeometry3D();
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
}
