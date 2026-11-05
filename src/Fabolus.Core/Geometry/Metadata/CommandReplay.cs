using System.Runtime.CompilerServices;
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
///
/// <para>
/// Replays are remembered against their base mesh, so the same history replayed twice hands back
/// the same meshes rather than computing them again. That is about more than time: smoothing is not
/// repeatable - the engine's level-set offset meshes in parallel and comes back a little different
/// on every call, down to its vertex count - so a body replayed for the parting line was not the
/// body the mould had been built around, and everything traced on it (rims, the line, the flange)
/// moved from one run to the next. Remembered, every feature that replays a history sees the one
/// result the history first produced.
/// </para>
/// </summary>
public static class CommandReplay {
    private static readonly ConditionalWeakTable<IMesh, ReplayMemo> Memos = new();

    /// <summary>
    /// Replays commands against <paramref name="baseMesh"/>, returning it unchanged when there is
    /// nothing to apply. Each result is kept by its command's stage as it is made, so a command that
    /// needs an earlier one - see <see cref="IStagedMeshCommand"/> - can be handed it. Picks up from
    /// the longest stretch of the same history replayed before on the same base.
    /// </summary>
    public static Result<IMesh> Apply(IGeometryEngine engine, IMesh baseMesh, IEnumerable<IMeshCommand> commands) {
        var list = commands as IReadOnlyList<IMeshCommand> ?? commands.ToList();
        var memo = Memos.GetValue(baseMesh, _ => new ReplayMemo());

        var applied = memo.LongestPrefix(engine, list);
        IMesh current = applied.Count > 0 ? applied[^1] : baseMesh;
        var stages = new ReplayStages(baseMesh);
        for (int i = 0; i < applied.Count; i++) {
            stages.Record(list[i].Priority, applied[i]);
        }

        var produced = new List<IMesh>(applied);
        for (int i = applied.Count; i < list.Count; i++) {
            var command = list[i];
            var result = command is IStagedMeshCommand staged
                ? staged.Apply(engine, current, stages)
                : command.Apply(engine, current);
            if (result.IsFailure) {
                return result.Error;
            }

            current = result.Value;
            stages.Record(command.Priority, current);
            produced.Add(current);
        }

        memo.Remember(engine, list, produced);
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

    /// <summary>
    /// The histories replayed on one base mesh, each with the mesh every command in it produced.
    /// Commands are matched by value, so a history rebuilt from a record matches the one that made
    /// it; a command without value equality only ever matches itself, which costs a recompute and
    /// nothing worse. Only the few most recent are kept - a history is edited at its end, so the
    /// latest ones are the prefixes the next replay will want.
    /// </summary>
    private sealed class ReplayMemo {
        private const int Capacity = 4;
        private readonly List<(IGeometryEngine Engine, IMeshCommand[] Commands, IMesh[] Meshes)> _entries = new();

        public IReadOnlyList<IMesh> LongestPrefix(IGeometryEngine engine, IReadOnlyList<IMeshCommand> commands) {
            lock (_entries) {
                IMesh[] best = Array.Empty<IMesh>();
                foreach (var entry in _entries) {
                    if (!ReferenceEquals(entry.Engine, engine)) continue;

                    int shared = 0;
                    int limit = Math.Min(entry.Commands.Length, commands.Count);
                    while (shared < limit && Equals(entry.Commands[shared], commands[shared])) shared++;

                    if (shared > best.Length) best = entry.Meshes[..shared];
                }
                return best;
            }
        }

        public void Remember(IGeometryEngine engine, IReadOnlyList<IMeshCommand> commands, IReadOnlyList<IMesh> meshes) {
            if (commands.Count == 0) return;

            lock (_entries) {
                _entries.RemoveAll(e => ReferenceEquals(e.Engine, engine) && e.Commands.SequenceEqual(commands));
                _entries.Add((engine, commands.ToArray(), meshes.ToArray()));
                if (_entries.Count > Capacity) _entries.RemoveAt(0);
            }
        }
    }
}
