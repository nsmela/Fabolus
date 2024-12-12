using Fabolus.Wpf.Common.Mesh;
using HelixToolkit.Wpf.SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
//messages
public sealed record MeshDisplayUpdatedMessage(List<DisplayModel3D> models);

public sealed record MeshSetInputBindingsMessage(
    RoutedCommand LeftMouseButton,
    RoutedCommand MiddleMouseButton,
    RoutedCommand RightMouseButton);

public sealed record MeshMouseDownMessage(object? sender, MouseDown3DEventArgs args);
public sealed record MeshMouseMoveMessage(object? sender, MouseMove3DEventArgs args);
public sealed record MeshMouseUpMessage(object? sender, MouseUp3DEventArgs args);
