using Fabolus.Core.Geometry;

namespace Fabolus.Core.Geometry.Metadata;

/// <summary>
/// Defines the standard, strongly-typed keys used across the core geometry domain.
/// </summary>
public static class CoreKeys {
    /// <summary>
    /// The unique identifier key for a mesh.
    /// </summary>
    public static readonly MetadataKey<Guid> Id = new("Id");

    /// <summary>
    /// The display name key for a mesh.
    /// </summary>
    public static readonly MetadataKey<string> Name = new("Name");

    /// <summary>
    /// The key representing the parent mesh ID if this mesh was derived from another.
    /// </summary>
    public static readonly MetadataKey<Guid> DerivedFrom = new("Derived From");

    /// <summary>
    /// The key representing the operation or user that created this mesh.
    /// </summary>
    public static readonly MetadataKey<string> CreatedBy = new("Created By"); // TODO: should kvp value instead be a class or feature?

    /// <summary>
    /// The ordered list of commands applied to <see cref="BaseMesh"/> to produce this mesh's current state.
    /// </summary>
    public static readonly MetadataKey<IReadOnlyList<IMeshCommand>> Commands = new("Commands");

    /// <summary>
    /// The pristine mesh this one was derived from, before any of <see cref="Commands"/> were applied.
    /// Held as a live mesh (not a Workspace lookup) so a command can be edited/removed and the
    /// result rebuilt without needing another live mesh instance elsewhere.
    /// </summary>
    public static readonly MetadataKey<IMesh> BaseMesh = new("Base Mesh");
}
