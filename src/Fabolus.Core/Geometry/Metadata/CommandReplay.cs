using Fabolus.Core.Common;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Geometry.Metadata;

/// <summary>
/// Reconstructs a mesh by replaying an ordered list of commands against a base mesh. Used to
/// revert a mesh after removing one of its commands (Reset/Clear features), and eventually to
/// rebuild a mesh from a save file (base mesh geometry + its Commands list).
/// </summary>
public static class CommandReplay {
    public static Result<IMesh> Apply(IGeometryEngine engine, IMesh baseMesh, IEnumerable<IMeshCommand> commands) {
        IMesh current = baseMesh;
        foreach (var command in commands) {
            var result = command.Apply(engine, current);
            if (result.IsFailure) return result.Error;

            current = result.Value;
        }

        return Result<IMesh>.Success(current);
    }

    /// <summary>
    /// Computes the mesh exactly as it was at the specified pipeline stage, by replaying only
    /// commands up to that priority level. If no higher-priority commands exist, returns the
    /// mesh unmodified. Callers who dispose the result must check ReferenceEquals against the
    /// input mesh to avoid destroying the active workspace instance.
    /// </summary>
    public static Result<IMesh> GetMeshAtStage(IGeometryEngine engine, IMesh currentMesh, int priorityLevel) {
        if (!currentMesh.Metadata.Commands.Any(c => c.Priority > priorityLevel)) {
            return Result<IMesh>.Success(currentMesh);
        }

        var allowedCommands = currentMesh.Metadata.Commands.Where(c => c.Priority <= priorityLevel).ToList();
        return Apply(engine, currentMesh.Metadata.BaseMesh.Value, allowedCommands);
    }
}
