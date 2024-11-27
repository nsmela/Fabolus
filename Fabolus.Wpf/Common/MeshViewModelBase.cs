using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;

namespace Fabolus.Wpf.Common;
public abstract partial class MeshViewModelBase : ObservableObject
{
    public const string Orthographic = "Orthographic Camera";
    public const string Perspective = "Perspective Camera";

    protected OrthographicCamera defaultOrthographicCamera = 
        new OrthographicCamera { 
            Position = new System.Windows.Media.Media3D.Point3D(0, 0, 5), 
            LookDirection = new System.Windows.Media.Media3D.Vector3D(-0, -0, -5), 
            UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, 0), 
            NearPlaneDistance = 1, 
            FarPlaneDistance = 100 
        };

    protected PerspectiveCamera defaultPerspectiveCamera = 
        new PerspectiveCamera { 
            Position = new System.Windows.Media.Media3D.Point3D(0, 0, 5), 
            LookDirection = new System.Windows.Media.Media3D.Vector3D(-0, -0, -5), 
            UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, 0), 
            NearPlaneDistance = 0.5, 
            FarPlaneDistance = 150 
        };

    private string cameraModel;

    [ObservableProperty] private Camera _camera;

    [ObservableProperty] private string _title;
    [ObservableProperty] private string _subTitle;

    //mesh to store visible model from messager
    [ObservableProperty] protected Model3DGroup _displayMesh;

    //Camera controls
    [ObservableProperty] protected bool? _zoomWhenLoaded = false;

    public MeshViewModelBase(bool? zoom = false) {
        DisplayMesh = new Model3DGroup();
        ZoomWhenLoaded = zoom;
        _camera = defaultPerspectiveCamera;
    }

    public void OnClosing() { 
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

}
