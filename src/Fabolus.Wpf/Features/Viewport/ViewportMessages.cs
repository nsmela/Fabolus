
using Fabolus.Core.Geometry;

namespace Fabolus.Wpf.Features.Viewport;

public sealed record ActiveSceneManagerChangedMessage(ISceneManager SceneManager);
public sealed record WorkspaceChangedMessage(Workspace Workspace);
public sealed class SwitchToMeshManagerMessage { }
