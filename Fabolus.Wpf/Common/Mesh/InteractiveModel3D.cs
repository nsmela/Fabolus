using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common.Mouse;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using HelixToolkit.Wpf.SharpDX;
using System.Windows;
using System.Windows.Input;

namespace Fabolus.Wpf.Common.Mesh;

public sealed class InteractiveModel3D : GroupModel3D, IHitable, ISelectable {
    public bool IsSelected { get; set; }

}


