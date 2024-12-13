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

public sealed record MeshMouseDownMessage(List<HitTestResult> Hits, InputEventArgs OriginalEventArgs);
public sealed record MeshMouseMoveMessage(List<HitTestResult> Hits, InputEventArgs OriginalEventArgs);
public sealed record MeshMouseUpMessage(List<HitTestResult> Hits, InputEventArgs OriginalEventArgs);
