using Fabolus.Core.Common;
using System.Collections.ObjectModel;

namespace Fabolus.Core.Geometry;

/// <summary>
/// Immutable aggregate root representing a CAD workspace.
/// Manages meshes and maintains consistency.
/// </summary>
public sealed class Workspace
{
    private readonly IReadOnlyDictionary<Guid, IMesh> _meshes;

    /// <summary>
    /// All meshes currently loaded in the workspace.
    /// </summary>
    public IReadOnlyDictionary<Guid, IMesh> Meshes => _meshes;

    /// <summary>
    /// ID of the currently active (selected) mesh.
    /// Null if no mesh is selected.
    /// </summary>
    public Guid ActiveMeshId { get; }

    /// <summary>
    /// Workspace-level metadata (e.g., patient info, study details).
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; }

    /// <summary>
    /// Number of meshes in the workspace.
    /// </summary>
    public int MeshCount => _meshes.Count;

    private Workspace(
        IReadOnlyDictionary<Guid, IMesh> meshes,
        Guid? activeMeshId = null,
        IReadOnlyDictionary<string, object>? metadata = null)
    {
        var updatedMeshes = new Dictionary<Guid, IMesh>();
        foreach (var kvp in meshes)
        {
            updatedMeshes[kvp.Key] = kvp.Value;
        }

        _meshes = updatedMeshes;
        ActiveMeshId = activeMeshId ?? Guid.Empty;
        Metadata = metadata ?? new Dictionary<string, object>();
    }

    /// <summary>
    /// Creates a new empty workspace.
    /// </summary>
    public static Workspace CreateEmpty() =>
        new(new Dictionary<Guid, IMesh>());

    /// <summary>
    /// Creates a workspace with initial metadata.
    /// </summary>
    public static Workspace CreateEmpty(IReadOnlyDictionary<string, object> metadata) =>
        new(new Dictionary<Guid, IMesh>(), null, metadata);

    /// <summary>
    /// Adds a mesh to the workspace.
    /// Mesh ID comes from IMesh.Id property.
    /// </summary>
    public Result<Workspace> AddMesh(IMesh mesh, bool setActive = true)
    {
        if (mesh is null)
            return WorkspaceErrors.NullMesh;

        var meshId = mesh.Metadata.Id;
        if (meshId == Guid.Empty)
            return WorkspaceErrors.InvalidId;

        if (_meshes.ContainsKey(meshId))
            return WorkspaceErrors.DuplicateMesh(mesh.Metadata.Name);

        var newMeshes = new Dictionary<Guid, IMesh>(_meshes) { [meshId] = mesh };

        var activeId = setActive ? meshId : ActiveMeshId;

        return new Workspace(newMeshes, activeId, Metadata);
    }

    /// <summary>
    /// Removes a mesh from the workspace.
    /// Clears active selection if the removed mesh was active.
    /// </summary>
    public Result<Workspace> RemoveMesh(Guid meshId)
    {
        if (!_meshes.ContainsKey(meshId))
            return WorkspaceErrors.MeshNotFound(meshId);

        var newMeshes = new Dictionary<Guid, IMesh>(_meshes);
        newMeshes.Remove(meshId);

        var newActiveMeshId = meshId == ActiveMeshId ? Guid.Empty : ActiveMeshId;
        return new Workspace(newMeshes, newActiveMeshId, Metadata);
    }

    /// <summary>
    /// Updates an existing mesh.
    /// </summary>
    public Result<Workspace> UpdateMesh(IMesh updatedMesh)
    {
        if (updatedMesh is null)
            return WorkspaceErrors.NullMesh;

        var meshId = updatedMesh.Metadata.Id;
        if (!_meshes.ContainsKey(meshId))
            return WorkspaceErrors.MeshNotFound(updatedMesh.Metadata.Name);

        var newMeshes = new Dictionary<Guid, IMesh>(_meshes) { [meshId] = updatedMesh };
        return new Workspace(newMeshes, ActiveMeshId, Metadata);
    }

    /// <summary>
    /// Sets the active mesh for editing.
    /// </summary>
    public Result<Workspace> SetActiveMesh(Guid? meshId)
    {
        if (meshId is null || !_meshes.ContainsKey(meshId.Value))
            return WorkspaceErrors.CannotSetActive(meshId.Value);

        return new Workspace(_meshes, meshId, Metadata);
    }

    /// <summary>
    /// Gets the currently active mesh.
    /// </summary>
    public Result<IMesh> GetActiveMesh()
    {
        if (ActiveMeshId == Guid.Empty)
            return WorkspaceErrors.NoActiveMesh;

        if (!_meshes.TryGetValue(ActiveMeshId, out var mesh))
            return WorkspaceErrors.ActiveMeshNotFound;

        return Result.Success(mesh);
    }

    /// <summary>
    /// Gets a mesh by ID.
    /// </summary>
    public Result<IMesh> GetMesh(Guid meshId)
    {
        if (_meshes.TryGetValue(meshId, out var mesh))
            return Result.Success(mesh);

        return WorkspaceErrors.MeshNotFound(meshId);
    }

    /// <summary>
    /// Updates workspace metadata.
    /// </summary>
    public Workspace WithMetadata(string key, object value)
    {
        var newMetadata = new Dictionary<string, object>(Metadata) { [key] = value };
        return new Workspace(_meshes, ActiveMeshId, newMetadata);
    }

    /// <summary>
    /// Checks if a mesh exists.
    /// </summary>
    public bool ContainsMesh(Guid meshId) => _meshes.ContainsKey(meshId);

}
