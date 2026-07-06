using Fabolus.Core.Common;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Geometry.Metadata;

/// <summary>
/// Reconstructs a mesh by replaying an ordered list of commands against a base mesh. Used to
/// revert a mesh after removing one of its commands (Reset/Clear features), and eventually to
/// rebuild a mesh from a save file (base mesh geometry + its Commands list).
/// Ownership contract: every mesh returned from here is owned by the caller and must be
/// disposed - shared instances never cross this boundary.
/// </summary>
public static class CommandReplay {
    /// <summary>
    /// Replays commands against <paramref name="baseMesh"/>, taking ownership of it: it is
    /// either consumed (disposed once the first command produces a new mesh) or returned as
    /// the result (when there are no commands to apply). Intermediates are disposed as the
    /// chain advances. Pass an owned copy (e.g. from GetBaseMeshCopy), never a shared instance.
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
    /// Computes the mesh exactly as it was at the specified pipeline stage, by replaying only
    /// commands up to that priority level against a copy of the base mesh. Always returns a
    /// mesh the caller owns and must dispose - never the input mesh or the stored BaseMesh.
    /// </summary>
    public static Result<IMesh> GetMeshAtStage(IGeometryEngine engine, IMesh currentMesh, int priorityLevel) {
        if (!currentMesh.Metadata.Commands.Any(c => c.Priority > priorityLevel)) {
            return Result<IMesh>.Success(currentMesh);
        }

        var baseCopy = currentMesh.Metadata.GetBaseMesh();
        if (baseCopy.HasNoValue) {
            return MetadataErrors.MissingBaseMesh;
        }

        var allowedCommands = currentMesh.Metadata.Commands.Where(c => c.Priority <= priorityLevel).ToList();
        return Apply(engine, baseCopy.Value, allowedCommands);
    }
}
