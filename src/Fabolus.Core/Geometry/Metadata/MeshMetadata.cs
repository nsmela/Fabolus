using Fabolus.Core.Common;
using System.Collections.Immutable;

namespace Fabolus.Core.Geometry.Metadata;

/// <summary>
/// Metadata associated with a mesh.
/// Immutable value object for mesh identification and display properties.
/// </summary>
public sealed record MeshMetadata {
    private ImmutableDictionary<string, object> Properties { get; init; } = ImmutableDictionary<string, object>.Empty;

    /// <summary>
    /// Uses a MetadataKey to ensure the proper property is set in a type-safe manner.
    /// </summary>
    /// <typeparam name="T">The type of the value being set.</typeparam>
    /// <param name="key">The strongly-typed metadata key.</param>
    /// <param name="value">The value to associate with the key.</param>
    /// <returns>A new <see cref="MeshMetadata"/> instance containing the updated property.</returns>
    /// <exception cref="ArgumentNullException">Thrown if the value is null.</exception>
    public MeshMetadata WithProperty<T>(MetadataKey<T> key, T value) {
        ArgumentNullException.ThrowIfNull(value);
        return this with { Properties = Properties.SetItem(key.Name, value) };
    }

    /// <summary>
    /// Executes a batch of type-safe updates, allocating only a single new dictionary for performance.
    /// </summary>
    /// <param name="configure">An action providing access to the mutable metadata builder.</param>
    /// <returns>A new <see cref="MeshMetadata"/> instance containing all batched updates.</returns>
    public MeshMetadata WithProperties(Action<MetadataBuilder> configure) {
        // 1. Create a mutable clone of the current dictionary
        var builder = Properties.ToBuilder();

        // 2. Wrap it in our type-safe sandbox
        var context = new MetadataBuilder(builder);

        // 3. Let the caller apply all their changes
        configure(context);

        // 4. Freeze it back into a new immutable record
        return this with { Properties = builder.ToImmutable() };
    }

    /// <summary>
    /// Retrieves a value safely using its MetadataKey, returning a Result pattern instead of throwing.
    /// </summary>
    /// <typeparam name="T">The expected type of the value.</typeparam>
    /// <param name="key">The strongly-typed metadata key to retrieve.</param>
    /// <returns>A successful Result containing the value, or a KeyNotFound error if missing or improperly typed.</returns>
    public Maybe<T> GetProperty<T>(MetadataKey<T> key) =>
        Properties.TryGetValue(key.Name, out var rawValue) && rawValue is T typedValue
            ? Maybe<T>.Some(typedValue)
            : Maybe<T>.None();

    /// <summary>
    /// Retrieves a required value using its MetadataKey, throwing an exception if it does not exist.
    /// </summary>
    /// <typeparam name="T">The expected type of the value.</typeparam>
    /// <param name="key">The strongly-typed metadata key to retrieve.</param>
    /// <returns>The strongly-typed value associated with the key.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the property doesn't exist or is of the wrong type.</exception>
    public T GetRequired<T>(MetadataKey<T> key) {
        if (Properties.TryGetValue(key.Name, out var value) && value is T typedValue)
            return typedValue;

        throw new KeyNotFoundException($"Metadata missing required key: {key.Name}");
    }

    public MeshMetadata WithoutProperty<T>(MetadataKey<T> key) =>
        this with { Properties = Properties.Remove(key.Name) };

    // --- HELPER METHODS --- //

    /// <summary>
    /// Gets the unique identifier for the mesh.
    /// Throws an exception if the ID has not been set.
    /// </summary>
    public Guid Id => GetRequired(CoreKeys.Id);

    /// <summary>
    /// Creates a new metadata instance with the specified unique identifier.
    /// </summary>
    /// <param name="id">The Guid to assign. If null, a new Guid is generated.</param>
    /// <returns>A new <see cref="MeshMetadata"/> instance with the updated ID.</returns>
    public MeshMetadata WithId(Guid? id = null) => WithProperty(CoreKeys.Id, id ?? Guid.NewGuid());

    /// <summary>
    /// Gets the display name for the mesh (e.g., filename, operation result).
    /// Throws an exception if the Name has not been set.
    /// </summary>
    public string Name => GetRequired(CoreKeys.Name);

    /// <summary>
    /// Creates a new metadata instance with the specified display name.
    /// </summary>
    /// <param name="name">The name to assign. If null, an empty string is used.</param>
    /// <returns>A new <see cref="MeshMetadata"/> instance with the updated Name.</returns>
    public MeshMetadata WithName(string name) => WithProperty(CoreKeys.Name, name ?? string.Empty);

    /// <summary>
    /// Gets the ID of the parent mesh if this was derived, returning a Result pattern.
    /// </summary>
    public Maybe<Guid> DerivedFrom => GetProperty(CoreKeys.DerivedFrom);

    /// <summary>
    /// Creates a new metadata instance recording the parent mesh identifier.
    /// </summary>
    /// <param name="guid">The Guid of the parent mesh.</param>
    /// <returns>A new <see cref="MeshMetadata"/> instance with the updated DerivedFrom property.</returns>
    public MeshMetadata WithDerivedFrom(Guid guid) => WithProperty(CoreKeys.DerivedFrom, guid);

    /// <summary>
    /// Gets the operation or user that created this mesh, returning a Result pattern.
    /// </summary>
    public Maybe<string> CreatedBy => GetProperty(CoreKeys.CreatedBy);

    /// <summary>
    /// Creates a new metadata instance recording the creator or operation.
    /// </summary>
    /// <param name="value">The string representing the creator or operation.</param>
    /// <returns>A new <see cref="MeshMetadata"/> instance with the updated CreatedBy property.</returns>
    public MeshMetadata WithCreatedBy(string value) => WithProperty(CoreKeys.CreatedBy, value ?? string.Empty);

    /// <summary>
    /// Creates a base metadata instance from a given file path (typically used for imports).
    /// Automatically generates an ID and sets the CreatedBy property to "Import".
    /// </summary>
    /// <param name="filePath">The full or relative path of the imported file.</param>
    /// <returns>A newly initialized <see cref="MeshMetadata"/> instance.</returns>
    public static MeshMetadata FromFileName(string filePath) {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var metadata = new MeshMetadata();

        return metadata.WithProperties(m =>
            m.Set(CoreKeys.Id, Guid.NewGuid())
            .Set(CoreKeys.Name, fileName)
            .Set(CoreKeys.CreatedBy, "Import")
        );
    }
}