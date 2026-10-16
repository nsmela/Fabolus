
using Fabolus.Core.Geometry;

namespace Fabolus.Wpf.Features.Viewport;

public sealed record WorkspaceChangedMessage(Workspace Workspace);

/// <summary>
/// The named entries' geometry now matches a file on disk - just imported from it, or just
/// exported to it - so closing the app would lose nothing of them.
/// </summary>
public sealed record MeshesSavedMessage(Workspace Workspace, IReadOnlyCollection<Guid> Ids);
public sealed class SwitchToMeshManagerMessage { }
