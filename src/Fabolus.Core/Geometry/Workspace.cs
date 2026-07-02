using Fabolus.Core.Common;

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
    /// Number of meshes in the workspace.
    /// </summary>
    public int MeshCount => _meshes.Count;

    private Workspace(
        IReadOnlyDictionary<Guid, IMesh> meshes,
        Guid? activeMeshId = null)
    {
        var updatedMeshes = new Dictionary<Guid, IMesh>();
        foreach (var kvp in meshes)
        {
            updatedMeshes[kvp.Key] = kvp.Value;
        }

        _meshes = updatedMeshes;
        ActiveMeshId = activeMeshId ?? Guid.Empty;
    }

    /// <summary>
    /// Creates a new empty workspace.
    /// </summary>
    public static Workspace CreateEmpty() =>
        new(new Dictionary<Guid, IMesh>());

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

        // Establish BaseMesh exactly once, here, at the single point every mesh enters a
        // Workspace - so every command (Smooth, Rotate, Translate, Mould) can rely on it
        // already being present instead of each lazily cloning it on its own first command.
        // Must clone (not just point at itself): this same mesh object will eventually be
        // disposed by UpdateMesh/RemoveMesh when a command replaces it.
        if (mesh.Metadata.BaseMesh.HasNoValue)
            mesh = mesh.WithMetadata(mesh.Metadata.WithBaseMesh(mesh.Clone()));

        var newMeshes = new Dictionary<Guid, IMesh>(_meshes) { [meshId] = mesh };

        var activeId = setActive ? meshId : ActiveMeshId;

        return new Workspace(newMeshes, activeId);
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
        if (newMeshes.TryGetValue(meshId, out var existingMesh))
        {
            existingMesh.Dispose();
            newMeshes.Remove(meshId);
        }

        var newActiveMeshId = meshId == ActiveMeshId ? Guid.Empty : ActiveMeshId;
        return new Workspace(newMeshes, newActiveMeshId);
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

        var newMeshes = new Dictionary<Guid, IMesh>(_meshes);
        if (newMeshes.TryGetValue(meshId, out var existingMesh))
        {
            existingMesh.Dispose();
        }
        newMeshes[meshId] = updatedMesh;
        return new Workspace(newMeshes, ActiveMeshId);
    }

    /// <summary>
    /// Sets the active mesh for editing.
    /// </summary>
    public Result<Workspace> SetActiveMesh(Guid? meshId)
    {
        if (meshId is null || meshId == Guid.Empty)
            return new Workspace(_meshes, null);

        if (!_meshes.ContainsKey(meshId.Value))
            return WorkspaceErrors.MeshNotFound(meshId.Value);

        return new Workspace(_meshes, meshId);
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
    /// Checks if a mesh exists.
    /// </summary>
    public bool ContainsMesh(Guid meshId) => _meshes.ContainsKey(meshId);

}
