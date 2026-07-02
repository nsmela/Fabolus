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
}
