using CommunityToolkit.Mvvm.ComponentModel;
using HelixToolkit.Wpf.SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Wpf.Common.Mesh;

public sealed class InteractiveModel3D : GroupModel3D, IHitable, ISelectable {
    public bool IsSelected { get; set; }

    public InteractiveModel3D() {
        this.Mouse3DMove += OnMouseMove;
    }

    private void OnMouseMove(object? sender, MouseMove3DEventArgs e) {
        var text = e.ToString();
    }
}


