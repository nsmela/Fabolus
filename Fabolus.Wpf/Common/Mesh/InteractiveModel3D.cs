using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using HelixToolkit.Wpf.SharpDX;
using System.Windows;

namespace Fabolus.Wpf.Common.Mesh;

public sealed class InteractiveModel3D : GroupModel3D, IHitable, ISelectable {
    public bool IsSelected { get; set; }

    public InteractiveModel3D() {
        this.Mouse3DDown += OnMouseDown;
        this.Mouse3DMove += OnMouseMove;
        this.Mouse3DUp += OnMouseUp;
    }

    private void OnMouseDown(object? sender, MouseDown3DEventArgs e) =>
        WeakReferenceMessenger.Default.Send(new MeshMouseDownMessage(sender, e));

    private void OnMouseMove(object? sender, MouseMove3DEventArgs e) =>
        WeakReferenceMessenger.Default.Send(new MeshMouseMoveMessage(sender, e));

    private void OnMouseUp(object? sender, MouseUp3DEventArgs e) =>
        WeakReferenceMessenger.Default.Send(new MeshMouseUpMessage(sender, e));

}


