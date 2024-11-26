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
    //mesh to store visible model from messager
    [ObservableProperty] protected Model3DGroup _displayMesh;

    //Camera controls
    [ObservableProperty] protected PerspectiveCamera? _camera;
    [ObservableProperty] protected bool? _zoomWhenLoaded = false;

    public MeshViewModelBase(bool? zoom = false) {
        DisplayMesh = new Model3DGroup();
        ZoomWhenLoaded = zoom;
    }

    public void OnClose() {
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

}
