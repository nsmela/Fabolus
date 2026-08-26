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

    /// <summary>
    /// Plain (non-3D) mouse move, hit-tested by the caller. HelixToolkit's MouseMove3D routed
    /// event only fires while a mouse button is held, which breaks hover-based interactions such
    /// as previewing or placing items without dragging - so the viewport hit-tests itself and
    /// hands the results over.
    /// </summary>
    /// <param name="hits">
    /// Everything under the cursor, nearest first, and empty when the cursor is over nothing.
    /// The whole list rather than just the nearest, because a manager often wants a specific
    /// model (dragging across the target mesh, say) that another visual may be sitting in front of.
    /// </param>
    bool OnMouseMove(IList<HitTestResult> hits);
}