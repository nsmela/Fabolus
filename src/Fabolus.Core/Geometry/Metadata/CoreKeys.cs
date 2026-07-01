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
}
