using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using HelixToolkit.Wpf.SharpDX;
using Media3D = System.Windows.Media.Media3D;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpDX;

namespace Fabolus.Wpf.Common.Mesh;
public abstract partial class BaseMeshViewModel : ObservableObject, IDisposable
{
    [ObservableProperty] private Camera _camera;

    [ObservableProperty] private string _title;
    [ObservableProperty] private string _subTitle;

    //Camera controls
    [ObservableProperty] protected bool? _zoomWhenLoaded = false;

    public BaseMeshViewModel(BaseMeshViewModel? oldViewModel, bool zoomWhenLoaded = false) {
        Title = "test";
        SubTitle = "subtitle test";

        Camera = oldViewModel is not null ? oldViewModel.Camera : new PerspectiveCamera();
        ZoomWhenLoaded = zoomWhenLoaded;
        EffectsManager = new DefaultEffectsManager();
    }

    public void Dispose() { 
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    [ObservableProperty] private IEffectsManager _effectsManager;

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
