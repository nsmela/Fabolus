using CommunityToolkit.Mvvm.ComponentModel;
using Fabolus.Wpf.Common.Mesh;
using HelixToolkit.Wpf.SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;

namespace Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
//messages
public sealed record MeshDisplayUpdatedMessage(List<DisplayModel3D> models);

public sealed record MeshSetInputBindingsMessage(
    RoutedCommand LeftMouseButton,
    RoutedCommand MiddleMouseButton,
    RoutedCommand RightMouseButton);
public sealed record MeshDisplayInputsMessage(InputBindingCollection inputs);

public sealed record MeshMouseDownMessage(List<HitTestResult> Hits, InputEventArgs OriginalEventArgs);
public sealed record MeshMouseMoveMessage(List<HitTestResult> Hits, InputEventArgs OriginalEventArgs);
public sealed record MeshMouseUpMessage(List<HitTestResult> Hits, InputEventArgs OriginalEventArgs);

public static class MeshDisplay {

    public static InputBindingCollection DefaultBindings => new InputBindingCollection { 
        // mouse controls
        new MouseBinding(ViewportCommands.Rotate, new MouseGesture(MouseAction.RightClick, ModifierKeys.None)),
        new MouseBinding(ViewportCommands.Pan, new MouseGesture(MouseAction.MiddleClick, ModifierKeys.None)),

        // key commands
        new KeyBinding(){ Command = ViewportCommands.BackView, Key = Key.B },
        new KeyBinding(){ Command = ViewportCommands.BottomView, Key = Key.D },
        new KeyBinding(){ Command = ViewportCommands.FrontView, Key = Key.F },
        new KeyBinding(){ Command = ViewportCommands.Reset, Key = Key.H },
        new KeyBinding(){ Command = ViewportCommands.LeftView, Key = Key.L },
        new KeyBinding(){ Command = ViewportCommands.RightView, Key = Key.R },
        new KeyBinding(){ Command = ViewportCommands.TopView, Key = Key.T },
        new KeyBinding(){ Command = new RoutedCommand(), Key = Key.Delete },
    };

}