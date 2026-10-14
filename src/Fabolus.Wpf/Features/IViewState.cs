using Fabolus.Core.Geometry;
using Fabolus.Wpf.Features.Viewport;

namespace Fabolus.Wpf.Features;

public interface IViewState {
    Task ActivateAsync(Workspace workspace);
    Task<Workspace> DeactivateAsync();

    ISceneManager SceneManager { get; }
}
