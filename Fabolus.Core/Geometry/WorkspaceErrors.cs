using Fabolus.Core.Common;

namespace Fabolus.Core.Geometry;

/// <summary>
/// Centralized registry of Workspace domain errors.
/// </summary>
public static class WorkspaceErrors
{
    // --- STATIC ERRORS --- //
    public static readonly Error NullMesh =
        new("Workspace.NullMesh", "Mesh cannot be null.");

    public static readonly Error InvalidId =
        new("Workspace.InvalidId", "Mesh must have a valid ID.");

    public static readonly Error NoActiveMesh =
        new("Workspace.NoActiveMesh", "No mesh is currently selected.");

    public static readonly Error ActiveMeshNotFound =
        new("Workspace.ActiveMeshNotFound", "Active mesh no longer exists.");

    // --- FACTORY ERRORS (dynamic message, stable Code) --- //

    public static Error DuplicateMesh(string name) =>
        new("Workspace.DuplicateMesh", $"Mesh '{name}' already exists.");

    public static Error MeshNotFound(Guid meshId) =>
        new("Workspace.MeshNotFound", $"Mesh '{meshId}' not found.");

    public static Error MeshNotFound(string name) =>
        new("Workspace.MeshNotFound", $"Mesh '{name}' not found.");

    public static Error CannotSetActive(Guid meshId) =>
        new("Workspace.MeshNotFound", $"Cannot set active: mesh '{meshId}' not found.");
}
