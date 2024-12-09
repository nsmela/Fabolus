using HelixToolkit.Wpf.SharpDX;

namespace Fabolus.Wpf.Common.Mesh;

public sealed class InteractiveModel3D : GroupModel3D, IHitable, ISelectable {
    public bool IsSelected { get; set; }

    public InteractiveModel3D() {
        this.Mouse3DDown += OnMouseDown;
        this.Mouse3DMove += OnMouseMove;
        this.Mouse3DUp += OnMouseUp;
    }

    private void OnMouseDown(object? sender, MouseDown3DEventArgs e) {
        var text = e.ToString();
    }

    private void OnMouseMove(object? sender, MouseMove3DEventArgs e) {
        var text = e.ToString();
    }

    private void OnMouseUp(object? sender, MouseUp3DEventArgs e) {
        var text = e.ToString();
    }

}


