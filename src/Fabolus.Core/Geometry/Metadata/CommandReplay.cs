using BasicResults;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Geometry.Metadata;

/// <summary>
/// Reconstructs a mesh by replaying an ordered list of commands against a base mesh. Used to
/// revert a mesh after removing one of its commands (Reset/Clear features), and eventually to
/// rebuild a mesh from a save file (base mesh geometry + its Commands list).
///
/// Meshes are immutable values, so nothing here copies defensively and a caller is free to hold
/// whatever it gets back. Where there is nothing to replay the input is returned as-is, which is
/// safe for the same reason - this used to matter a great deal, when a mesh owned native memory
/// and handing out a shared instance let a caller dispose the workspace's own geometry.
/// </summary>
public static class CommandReplay {
    /// <summary>
    /// Replays commands against <paramref name="baseMesh"/>, returning it unchanged when there is
    /// nothing to apply.
    /// </summary>
    public static Result<IMesh> Apply(IGeometryEngine engine, IMesh baseMesh, IEnumerable<IMeshCommand> commands) {
        IMesh current = baseMesh;
        foreach (var command in commands) {
            var result = command.Apply(engine, current);
            if (result.IsFailure) {
                return result.Error;
            }

            current = result.Value;
        }

        return Result<IMesh>.Success(current);
    }

    /// <summary>
    /// The mesh exactly as it was at the given pipeline stage, by replaying only the commands up
    /// to that priority level against <paramref name="record"/>'s base mesh. Returns
    /// <paramref name="currentMesh"/> itself when nothing outranks the requested stage, and the
    /// record's base mesh when the stage admits no commands at all; both are immutable values
    /// that the caller may hold freely.
    /// </summary>
    public static Result<IMesh> GetMeshAtStage(
        IGeometryEngine engine,
        IMesh currentMesh,
        MeshRecord record,
        int priorityLevel) {
        if (!record.Commands.Any(c => c.Priority > priorityLevel)) {
            return Result<IMesh>.Success(currentMesh);
        }

        if (record.BaseMesh is null) {
            return MetadataErrors.MissingBaseMesh;
        }

        var allowed = record.Commands.Where(c => c.Priority <= priorityLevel).ToList();
        return Apply(engine, record.BaseMesh, allowed);
    }
}
