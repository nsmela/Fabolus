using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Features.AppPreferences;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using SharpDX.Direct3D11;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using Camera = HelixToolkit.Wpf.SharpDX.Camera;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace Fabolus.Wpf.Pages.MainWindow.MeshDisplay;

public partial class MeshViewModel : ObservableObject {

    [ObservableProperty] private Camera _camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera();
    [ObservableProperty] private IEffectsManager _effectsManager = new DefaultEffectsManager();

    [ObservableProperty] private Color _directionalLightColor = Colors.White;
    [ObservableProperty] private Color _ambientLightColor = Colors.GhostWhite;

    //mesh settings
    [ObservableProperty] private bool _shadows = false;
    [ObservableProperty] private bool _renderWireframe = false;

    //models
    [ObservableProperty] private LineGeometryModel3D _grid = new LineGeometryModel3D();
    private IList<DisplayModel3D> _models = [];

    //mouse commands
    [ObservableProperty] private ICommand _leftMouseCommand;
    [ObservableProperty] private ICommand _middleMouseCommand = ViewportCommands.Pan;
    [ObservableProperty] private ICommand _rightMouseCommand = ViewportCommands.Rotate;

    // cross section
    [ObservableProperty] private Plane _plane1 = new Plane(new Vector3(0, 0, 1), 0.0f);
    [ObservableProperty] private MeshGeometryModel3D _cuttingModel;
    [ObservableProperty] private HelixToolkit.Wpf.SharpDX.Material _cuttingMaterial = PhongMaterials.White;

    //meshing testing
    private SynchronizationContext context = SynchronizationContext.Current;

    public ObservableElement3DCollection CurrentModel { get; init; } = new ObservableElement3DCollection();
    [ObservableProperty] private Transform3D _mainTransform = MeshHelper.TransformEmpty;

    //printbed dimensions
    private float _printbedWidth;
    private float _printbedDepth;

    public MeshViewModel() {

        // Register messages
        WeakReferenceMessenger.Default.Register<MeshDisplayUpdatedMessage>(this, async (r, m) => await UpdateModels(m.models));
        WeakReferenceMessenger.Default.Register<MeshSetInputBindingsMessage>(this, (r, m) => UpdateInputBindings(m.LeftMouseButton, m.MiddleMouseButton, m.RightMouseButton));
        WeakReferenceMessenger.Default.Register<WireframeToggleMessage>(this, async (r, m) => await ToggleWireframe());

        WeakReferenceMessenger.Default.Register<PreferencesSetPrintbedDepthMessage>(this, (r, m) => {
                _printbedDepth = m.Depth;
                Grid.Geometry = GenerateGrid((-_printbedWidth / 2), (_printbedWidth / 2), (-_printbedDepth / 2), (_printbedDepth / 2));
        });

        WeakReferenceMessenger.Default.Register<PreferencesSetPrintbedWidthMessage>(this, (r, m) => {
            _printbedWidth = m.Width;
            Grid.Geometry = GenerateGrid((-_printbedWidth / 2), (_printbedWidth / 2), (-_printbedDepth / 2), (_printbedDepth / 2));
        });

        ResetCamera();

        _printbedWidth = WeakReferenceMessenger.Default.Send<PreferencesPrintbedWidthRequest>().Response;
        _printbedDepth = WeakReferenceMessenger.Default.Send<PreferencesPrintbedDepthRequest>().Response;
        Grid.Geometry = GenerateGrid((-_printbedWidth/2), (_printbedWidth / 2), (-_printbedDepth / 2), (_printbedDepth / 2));

        UpdateDisplay();
    }

    private async Task ToggleWireframe() {
        RenderWireframe = !RenderWireframe;
        await UpdateDisplay();
    }

    private async Task UpdateDisplay() {

        context.Post((o) => {
            if (_models.Count != 0) {
                _models[0].Geometry.UpdateOctree();
                _models[0].Geometry.UpdateBounds();
                CuttingModel = new MeshGeometryModel3D {
                    Geometry = _models[0].Geometry,
                };
            }

            CurrentModel.Clear();

            foreach (var model in _models) {
                model.Geometry.UpdateOctree();
                model.Geometry.UpdateBounds();
                var geometry = new MeshGeometryModel3D {
                    Geometry = model.Geometry,
                    Material = model.Skin,
                    Transform = model.Transform,
                    CullMode = model.IsTransparent ? CullMode.None : CullMode.Back,
                    IsTransparent = model.IsTransparent,
                    RenderWireframe = RenderWireframe,
                    WireframeColor = Colors.Black,
                    FillMode = model.ShowWireframe ? FillMode.Wireframe : FillMode.Solid,
                };
                CurrentModel.Add(geometry);
            }


        }, null);
    }

    private void UpdateInputBindings(RoutedCommand left, RoutedCommand middle, RoutedCommand right) {
        LeftMouseCommand = left;
        MiddleMouseCommand = middle;
        RightMouseCommand = right;
    }

    private async Task UpdateModels(IList<DisplayModel3D> models) {
        _models = models;
        await UpdateDisplay();
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

    protected void ResetCamera() {
        Camera.LookDirection = new Vector3D(-183.576f, 186.809f, -180.0f);
        Camera.UpDirection = new Vector3D(-0.397f, 0.404f, 0.824f);
        Camera.Position = new Point3D(183.556f, -185.847f, 179.5f);
        Camera.LookAt(new Point3D(0, 0, 0), 2.0f);
    }
}

