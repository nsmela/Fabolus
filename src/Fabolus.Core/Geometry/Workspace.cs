using BasicResults;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Geometry;

/// <summary>
/// Immutable aggregate root representing a CAD workspace.
/// Manages meshes and maintains consistency.
///
/// Every entry is a mesh paired with the <see cref="MeshRecord"/> naming it: the workspace owns
/// the identity and history, and the mesh is only the geometry currently filling that entry. That
/// split is what lets a feature replace an entry's geometry with the output of a boolean - which
/// carries none of the original's identity - without the entry losing track of what it is.
///
/// Ownership contract: the Workspace owns its stored meshes (and each record's BaseMesh) for the
/// duration of that entry's lifetime. Meshes passed in (AddMesh/UpdateMesh) are consumed; meshes
/// handed out (GetMesh/GetActiveMesh) are owned copies the caller must dispose. Read-only info
/// paths should use <see cref="GetRecord"/> instead - a record is a value object with nothing to
/// dispose.
/// </summary>
public sealed class Workspace
{
    private readonly IReadOnlyDictionary<Guid, Entry> _entries;

    private readonly record struct Entry(IMesh Mesh, MeshRecord Record);

    /// <summary>
    /// The records of all meshes currently loaded, for listing and display. Safe to hold - no
    /// geometry crosses this boundary. Use <see cref="GetMesh"/> when geometry is needed.
    /// </summary>
    public IReadOnlyList<MeshRecord> Records => _entries.Values.Select(e => e.Record).ToList();

    /// <summary>
    /// ID of the currently active (selected) mesh.
    /// <see cref="Guid.Empty"/> if no mesh is selected.
    /// </summary>
    public Guid ActiveMeshId { get; }

    /// <summary>
    /// Number of meshes in the workspace.
    /// </summary>
    public int MeshCount => _entries.Count;

    private Workspace(IReadOnlyDictionary<Guid, Entry> entries, Guid? activeMeshId = null)
    {
        _entries = entries;
        ActiveMeshId = activeMeshId ?? Guid.Empty;
    }

    /// <summary>
    /// Creates a new empty workspace.
    /// </summary>
    public static Workspace CreateEmpty() =>
        new(new Dictionary<Guid, Entry>());

    /// <summary>
    /// Adds a mesh under the given record, which the workspace takes ownership of - the caller
    /// must not dispose the mesh afterward. The record's BaseMesh is established here if it does
    /// not already have one, so every entry can replay its history from the moment it is added.
    /// </summary>
    public Result<Workspace> AddMesh(IMesh mesh, MeshRecord record, bool setActive = true)
    {
        if (mesh is null)
            return WorkspaceErrors.NullMesh;

        if (record is null || record.Id == Guid.Empty)
            return WorkspaceErrors.InvalidId;

        if (_entries.ContainsKey(record.Id))
            return WorkspaceErrors.DuplicateMesh(record.Name);

        if (record.BaseMesh is null)
            record = record.WithBaseMesh(mesh);

        var entries = new Dictionary<Guid, Entry>(_entries) { [record.Id] = new(mesh, record) };

        return new Workspace(entries, setActive ? record.Id : ActiveMeshId);
    }

    /// <summary>
    /// Removes a mesh from the workspace.
    /// Clears active selection if the removed mesh was active.
    /// </summary>
    public Result<Workspace> RemoveMesh(Guid meshId)
    {
        if (!_entries.ContainsKey(meshId))
            return WorkspaceErrors.MeshNotFound(meshId);

        var entries = new Dictionary<Guid, Entry>(_entries);
        entries.Remove(meshId);

        return new Workspace(entries, meshId == ActiveMeshId ? Guid.Empty : ActiveMeshId);
    }

    /// <summary>
    /// Replaces an entry's geometry, and its record when one is given. The ID is passed rather
    /// than read off the mesh precisely because the new geometry may have come from an operation
    /// that knows nothing about this workspace - a boolean result, say.
    /// </summary>
    public Result<Workspace> UpdateMesh(Guid meshId, IMesh mesh, MeshRecord? record = null)
    {
        if (mesh is null)
            return WorkspaceErrors.NullMesh;

        if (!_entries.TryGetValue(meshId, out var existing))
            return WorkspaceErrors.MeshNotFound(meshId);

        if (record is not null && record.Id != meshId)
            return WorkspaceErrors.InvalidId;

        var updated = record ?? existing.Record;
        if (updated.BaseMesh is null)
            updated = updated.WithBaseMesh(mesh);

        var entries = new Dictionary<Guid, Entry>(_entries) { [meshId] = new(mesh, updated) };
        return new Workspace(entries, ActiveMeshId);
    }

    /// <summary>
    /// Replaces only an entry's record, leaving its geometry alone.
    /// </summary>
    public Result<Workspace> UpdateRecord(MeshRecord record)
    {
        if (record is null)
            return WorkspaceErrors.InvalidId;

        if (!_entries.TryGetValue(record.Id, out var existing))
            return WorkspaceErrors.MeshNotFound(record.Id);

        var entries = new Dictionary<Guid, Entry>(_entries) { [record.Id] = existing with { Record = record } };
        return new Workspace(entries, ActiveMeshId);
    }

    /// <summary>
    /// Sets the active mesh for editing.
    /// </summary>
    public Result<Workspace> SetActiveMesh(Guid? meshId)
    {
        if (meshId is null || meshId == Guid.Empty)
            return new Workspace(_entries, null);

        if (!_entries.ContainsKey(meshId.Value))
            return WorkspaceErrors.MeshNotFound(meshId.Value);

        return new Workspace(_entries, meshId);
    }

    /// <summary>
    /// Gets the currently active mesh.
    /// </summary>
    public Result<IMesh> GetActiveMesh()
    {
        if (ActiveMeshId == Guid.Empty)
            return WorkspaceErrors.NoActiveMesh;

        if (!_entries.TryGetValue(ActiveMeshId, out var entry))
            return WorkspaceErrors.ActiveMeshNotFound;

        return Result.Success(entry.Mesh);
    }

    /// <summary>
    /// Gets a mesh by ID.
    /// </summary>
    public Result<IMesh> GetMesh(Guid meshId)
    {
        if (_entries.TryGetValue(meshId, out var entry))
            return Result.Success(entry.Mesh);

        return WorkspaceErrors.MeshNotFound(meshId);
    }

    /// <summary>
    /// Gets an entry's record - a value object, safe to hold.
    /// </summary>
    public Result<MeshRecord> GetRecord(Guid meshId)
    {
        if (_entries.TryGetValue(meshId, out var entry))
            return Result.Success(entry.Record);

        return WorkspaceErrors.MeshNotFound(meshId);
    }

    /// <summary>
    /// Gets the record of the currently active mesh - a value object, safe to hold.
    /// </summary>
    public Result<MeshRecord> GetActiveRecord()
    {
        if (ActiveMeshId == Guid.Empty)
            return WorkspaceErrors.NoActiveMesh;

        if (!_entries.TryGetValue(ActiveMeshId, out var entry))
            return WorkspaceErrors.ActiveMeshNotFound;

        return Result.Success(entry.Record);
    }

    /// <summary>
    /// Checks if a mesh exists.
    /// </summary>
    public bool ContainsMesh(Guid meshId) => _entries.ContainsKey(meshId);
}
