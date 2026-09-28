using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Geometry;

/// <summary>
/// A workspace entry: who a mesh is, what has been done to it, and the pristine geometry that
/// history replays against. The <see cref="Workspace"/> holds one of these beside every mesh it
/// stores, and it is the reason a mesh keeps its identity through operations that rebuild its
/// geometry from scratch.
///
/// None of this rides on the mesh. A boolean hands back geometry that is neither operand, an
/// import hands back geometry the engine named itself, and in both cases anything travelling on
/// the mesh would be describing something that no longer exists - which is exactly what used to
/// happen. Derived facts about the geometry itself travel on the mesh instead, as
/// <see cref="FabolusAnnotations"/>.
/// </summary>
public sealed record MeshRecord {
    public required Guid Id { get; init; }

    /// <summary>What the mesh is called in the UI: the imported file's name, or a derived label.</summary>
    public required string Name { get; init; }

    /// <summary>The feature or action that produced this entry.</summary>
    public string CreatedBy { get; init; } = string.Empty;

    /// <summary>
    /// The ordered list of commands applied to <see cref="BaseMesh"/> to produce this entry's
    /// current geometry. Empty for a mesh nothing has been applied to yet.
    /// </summary>
    public IReadOnlyList<IMeshCommand> Commands { get; init; } = Array.Empty<IMeshCommand>();

    /// <summary>
    /// The pristine geometry this entry was built from, before any of <see cref="Commands"/> were
    /// applied. Held as a live mesh so a command can be edited or removed and the result rebuilt
    /// without needing another copy from somewhere else.
    /// </summary>
    public IMesh? BaseMesh { get; init; }

    /// <summary>
    /// Mould settings the user was still editing when they last left this mesh, distinct from a
    /// <see cref="MouldDefinition"/> in <see cref="Commands"/> - which means the mesh *is* a
    /// generated mould. Kept here rather than in the Moulding view model so it survives the
    /// feature being deactivated and another one updating the mesh.
    ///
    /// This is the one feature-owned value on this record, and it is here because it is per-mesh
    /// draft state with nowhere better to live; anything a feature bakes in belongs in
    /// <see cref="Commands"/> instead.
    /// </summary>
    public MouldDefinition? PendingMould { get; init; }

    /// <summary>
    /// A fresh entry for a just-imported mesh. <paramref name="name"/> is the one the engine
    /// derived from the file, which already accounts for a multi-component import naming its
    /// parts separately.
    /// </summary>
    public static MeshRecord ForImport(string name) => new() {
        Id = Guid.NewGuid(),
        Name = name,
        CreatedBy = "Import",
    };

    /// <summary>
    /// Records that <paramref name="command"/> was applied. Any existing command of the same
    /// runtime type is replaced (not stacked) and the new one moved to the end of the order -
    /// this mirrors the "one net value per feature" behaviour (e.g. rotations compose into a
    /// single net Quaternion) while still preserving relative order across different features.
    /// Also clears any existing command with a strictly greater <see cref="IMeshCommand.Priority"/>
    /// - it depended on geometry this command just changed, so it is now stale (e.g. recording a
    /// new Rotate clears a previously-generated Mould).
    /// </summary>
    public MeshRecord WithCommand(IMeshCommand command) {
        var updated = Commands
            .Where(c => c.GetType() != command.GetType())
            .Where(c => c.Priority <= command.Priority)
            .ToList();
        updated.Add(command);
        return this with { Commands = updated };
    }

    /// <summary>
    /// Removes any command of the given runtime type from the ordered list, cascading to also
    /// remove anything with a strictly greater <see cref="IMeshCommand.Priority"/> (it depended
    /// on geometry this removal just changed). A no-op if the type is not present.
    /// </summary>
    public MeshRecord WithoutCommand<TCommand>() where TCommand : IMeshCommand {
        var removed = Commands.OfType<TCommand>().FirstOrDefault();
        if (removed is null) return this;

        return this with {
            Commands = Commands
                .Where(c => c is not TCommand)
                .Where(c => c.Priority <= removed.Priority)
                .ToList(),
        };
    }

    /// <summary>The first recorded command of the given type, if there is one.</summary>
    public TCommand? Command<TCommand>() where TCommand : class, IMeshCommand =>
        Commands.OfType<TCommand>().FirstOrDefault();

    public MeshRecord WithName(string name) => this with { Name = name ?? string.Empty };

    public MeshRecord WithBaseMesh(IMesh mesh) => this with { BaseMesh = mesh };
}
