using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using HelixToolkit.Wpf.SharpDX;
using Camera = HelixToolkit.Wpf.SharpDX.Camera;
using Color = System.Windows.Media.Color;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;
using Material = HelixToolkit.Wpf.SharpDX.Material;
using SharpDX;
using System.Windows.Media;
using System.Windows.Input;

namespace Fabolus.Wpf.Common.Mesh;

public partial class MeshViewModel : ObservableObject
{
    [ObservableProperty] private Camera _camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera();
    [ObservableProperty] private IEffectsManager _effectsManager = new DefaultEffectsManager();

    [ObservableProperty] private Color _directionalLightColor = Colors.White;
    [ObservableProperty] private Color _ambientLightColor = Colors.GhostWhite;

    //models
    [ObservableProperty] private LineGeometryModel3D _grid = new LineGeometryModel3D();
    [ObservableProperty] private SceneNodeGroupModel3D _modelGroup;
    //testing
    [ObservableProperty] private IList<BatchedMeshGeometryConfig> _batchedMeshes;
    [ObservableProperty] private IList<Material> _batchedMaterials;
    [ObservableProperty] private Transform3D _mainTransform = new ScaleTransform3D(1.0, 1.0, 1.0);
    [ObservableProperty] private Material _mainMaterial = PhongMaterials.White;
    //mouse commands
    [ObservableProperty] private ICommand _leftMouseCommand = ViewportCommands.Pan;
    [ObservableProperty] private ICommand _middleMouseCommand = ViewportCommands.Zoom;
    [ObservableProperty] private ICommand _rightMouseCommand = ViewportCommands.Rotate;



    public MeshViewModel()
    {
        Grid.Geometry = GenerateGrid();
    }

    public void SetModel(Object3D model) {
        var models = new List<BatchedMeshGeometryConfig>();
        model.Geometry.UpdateOctree();
        models.Add(new BatchedMeshGeometryConfig(model.Geometry, MainTransform.ToMatrix(), 0));
        var materials = new Material[] { MainMaterial };

        BatchedMeshes = models;
        BatchedMaterials = materials;
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

