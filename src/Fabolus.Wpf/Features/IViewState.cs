using Fabolus.Core.Geometry;
using Fabolus.Wpf.Features.Viewport;

namespace Fabolus.Wpf.Features;

public interface IViewState {
    Task ActivateAsync(Workspace workspace);
    Task<Workspace> DeactivateAsync();

    /// <summary>
    /// The workspace as this view has it now, including anything applied since it was activated.
    /// Unlike <see cref="DeactivateAsync"/> it leaves the view running, so it can be asked when
    /// the app may yet stay open.
    /// </summary>
    Workspace CurrentWorkspace { get; }

    ISceneManager SceneManager { get; }
}
