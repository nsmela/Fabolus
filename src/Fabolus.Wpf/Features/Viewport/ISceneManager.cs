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
    bool OnMouseMove(MouseMove3DEventArgs eventArgs);

}