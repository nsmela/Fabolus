using Fabolus.Core.Geometry;
using HelixToolkit.Wpf.SharpDX;
using System.Windows.Input;

namespace Fabolus.Wpf.Features.Viewport;


public interface ISceneManager {
    event Action<Element3D> VisualAddedOrUpdated;
    event Action<Guid> VisualRemovedById;
    event Action VisualsCleared;

    void OnActivated();
    void OnDeactivated();

    bool OnKeyDown(Key key);
    bool OnKeyUp(Key key);

    bool OnMouseDown(MouseDown3DEventArgs eventArgs);
    bool OnMouseUp(MouseUp3DEventArgs eventArgs);

    // Plain (non-3D) mouse move, hit-tested manually by the caller. HelixToolkit's
    // MouseMove3D routed event only fires while a mouse button is held, which breaks
    // hover-based interactions (e.g. previewing/placing items without dragging).
    bool OnMouseMove(HitTestResult? hit);

}