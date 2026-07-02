using Fabolus.Core.Common;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Geometry.Metadata;

/// <summary>
/// A serializable record of an operation that was applied to produce a mesh's current state.
/// Stored in metadata as an ordered list; replaying the list against a base mesh reconstructs
/// the final result.
/// </summary>
public interface IMeshCommand {
    /// <summary>
    /// Static per-type pipeline stage (see <see cref="CommandPriority"/>). Recording a command
    /// clears any existing commands with a strictly greater priority, since they depended on
    /// state this command just changed. Commands sharing a priority don't clear each other.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Applies this command to the given mesh, returning the resulting mesh.
    /// Does not touch the Workspace or decide fork-vs-in-place - callers (the feature's
    /// Execute/orchestrator class) own that decision.
    /// </summary>
    Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh);
}
