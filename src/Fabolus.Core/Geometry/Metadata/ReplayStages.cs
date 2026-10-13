using BasicResults;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Geometry.Metadata;

/// <summary>
/// The meshes a replay has passed through so far, by the stage that produced them - what an
/// <see cref="IStagedMeshCommand"/> reads to reach geometry further back than the mesh in front of it.
/// </summary>
public sealed class ReplayStages {
    private readonly IMesh _baseMesh;
    private readonly List<(int Priority, IMesh Mesh)> _applied = new();

    public ReplayStages(IMesh baseMesh) {
        _baseMesh = baseMesh;
    }

    /// <summary>
    /// The mesh as it stood once every command up to <paramref name="priority"/> had run - the same
    /// answer <see cref="CommandReplay.GetMeshAtStage"/> gives from a record - or the base mesh when no
    /// command that early has run.
    /// </summary>
    public IMesh MeshAt(int priority) {
        for (int i = _applied.Count - 1; i >= 0; i--) {
            if (_applied[i].Priority <= priority) {
                return _applied[i].Mesh;
            }
        }

        return _baseMesh;
    }

    /// <summary>Whether a command at exactly <paramref name="priority"/> has run.</summary>
    public bool Passed(int priority) => _applied.Any(stage => stage.Priority == priority);

    internal void Record(int priority, IMesh mesh) => _applied.Add((priority, mesh));
}

/// <summary>
/// A command that builds on an earlier stage of the history it sits in, not only on the mesh in
/// front of it. A mould split is one: it is applied to the mould, but the parting line it cuts along
/// is traced on the body the mould was built around, which is several commands back.
/// </summary>
public interface IStagedMeshCommand : IMeshCommand {
    /// <summary>
    /// Applies this command to <paramref name="mesh"/>, with <paramref name="stages"/> holding every
    /// mesh the replay produced before it.
    /// </summary>
    Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh, ReplayStages stages);

    /// <summary>
    /// Refused: without the history there is nothing earlier to build on. Replay through
    /// <see cref="CommandReplay"/>, which hands the stages over.
    /// </summary>
    Result<IMesh> IMeshCommand.Apply(IGeometryEngine engine, IMesh mesh) => MetadataErrors.NeedsEarlierStages;
}
