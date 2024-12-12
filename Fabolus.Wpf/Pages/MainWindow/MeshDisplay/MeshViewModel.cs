using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Common.Mouse;
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
    [ObservableProperty] private FillMode _fillMode = SharpDX.Direct3D11.FillMode.Solid;
    [ObservableProperty] private bool _shadows = false;

    //models
    [ObservableProperty] private LineGeometryModel3D _grid = new LineGeometryModel3D();

    //mouse commands
    [ObservableProperty] private ICommand _leftMouseCommand = ViewportCommands.Pan;
    [ObservableProperty] private ICommand _middleMouseCommand = ViewportCommands.Zoom;
    [ObservableProperty] private ICommand _rightMouseCommand = ViewportCommands.Rotate;

    //meshing testing
    private SynchronizationContext context = SynchronizationContext.Current;
    public ObservableElement3DCollection CurrentModel { get; init; } = new ObservableElement3DCollection();
    [ObservableProperty] private Transform3D _mainTransform = MeshHelper.TransformEmpty;

    public MeshViewModel() {
        Grid.Geometry = GenerateGrid();
        WeakReferenceMessenger.Default.Register<MeshDisplayUpdatedMessage>(this, (r, m) => UpdateDisplay(m.models));
        WeakReferenceMessenger.Default.Register<MeshSetInputBindingsMessage>(this, (r, m) => UpdateInputBindings(m.LeftMouseButton, m.MiddleMouseButton, m.RightMouseButton));
    }

    private void UpdateDisplay(IList<DisplayModel3D> models) {
        CurrentModel.Clear();
        if (models.Count() < 1) { return; }

        context.Post((o) => {
            foreach (var model in models) {
                model.Geometry.UpdateOctree();
                model.Geometry.UpdateBounds();
                CurrentModel.Add(new MeshGeometryModel3D {
                    Geometry = model.Geometry,
                    Material = model.Skin,
                    Transform = model.Transform,
                    CullMode = CullMode.Back,
                });
            }
        }, null);

    }

    private void UpdateInputBindings(RoutedCommand left, RoutedCommand middle, RoutedCommand right) {
        LeftMouseCommand = left;
        MiddleMouseCommand = middle;
        RightMouseCommand = right;
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

    [RelayCommand]
    private void OnMouseMove() {
        var text = string.Empty; //test
    }
}

