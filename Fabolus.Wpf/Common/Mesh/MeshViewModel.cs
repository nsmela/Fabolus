﻿using System;
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
using CommunityToolkit.Mvvm.Messaging;
using SharpDX.Direct3D11;

namespace Fabolus.Wpf.Common.Mesh;

//messages
public sealed record MeshDisplayUpdatedMessasge(List<DisplayModel3D> models);

public partial class MeshViewModel : ObservableObject
{
    [ObservableProperty] private Camera _camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera();
    [ObservableProperty] private IEffectsManager _effectsManager = new DefaultEffectsManager();

    [ObservableProperty] private Color _directionalLightColor = Colors.White;
    [ObservableProperty] private Color _ambientLightColor = Colors.GhostWhite;

    //mesh settings
    [ObservableProperty] private FillMode _fillMode = FillMode.Solid;
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

    public MeshViewModel()
    {
        Grid.Geometry = GenerateGrid();
        WeakReferenceMessenger.Default.Register<MeshDisplayUpdatedMessasge>(this, (r, m) => UpdateDisplay(m.models));
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

