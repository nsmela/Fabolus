using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.MeshIO;

/// <summary>
/// Which entries in a workspace hold work that exists nowhere but in the app.
/// </summary>
/// <remarks>
/// An entry is safe once its geometry matches a file: the one it was imported from, or the last
/// one it was exported to. Meshes never change in place - every edit hands the workspace a new
/// one - so "matches" is reference equality with the mesh noted at that moment, and anything the
/// user has done since shows up as a different mesh. An entry with nothing noted at all (a split
/// piece, say) was made in the app and has never been written anywhere.
/// </remarks>
public sealed class UnsavedWork {
    private readonly Dictionary<Guid, IMesh> _saved = new();

    /// <summary>Notes the given entries' current geometry as matching a file on disk.</summary>
    public void MarkSaved(Workspace workspace, IEnumerable<Guid> ids) {
        foreach (var id in ids) {
            var mesh = workspace.GetMesh(id);
            if (mesh.IsSuccess) _saved[id] = mesh.Value;
        }
    }

    /// <summary>The entries of <paramref name="workspace"/> whose geometry matches no file.</summary>
    public IReadOnlyList<MeshRecord> Unsaved(Workspace workspace) =>
        workspace.Records
            .Where(record => workspace.GetMesh(record.Id) is { IsSuccess: true } mesh
                && !(_saved.TryGetValue(record.Id, out var saved) && ReferenceEquals(saved, mesh.Value)))
            .ToList();
}
