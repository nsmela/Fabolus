using Fabolus.Core.Geometry;
using Fabolus.Wpf.Features.Viewport;

namespace Fabolus.Wpf.Features;

public interface IViewState {
    void Activate(Workspace workspace);
    Workspace Deactivate();

    ISceneManager SceneManager { get; }
}
