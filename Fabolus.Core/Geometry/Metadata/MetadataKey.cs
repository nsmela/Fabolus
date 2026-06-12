namespace Fabolus.Core.Geometry.Metadata;

/// <summary>
/// A type-safe key that enforces the value type at compile time.
/// Prevents runtime casting errors when accessing dynamic metadata.
/// </summary>
/// <typeparam name="T">The required type of the value associated with this key.</typeparam>
public sealed record MetadataKey<T> {
    /// <summary>
    /// Gets the underlying string representation of the key.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataKey{T}"/> class.
    /// </summary>
    /// <param name="name">The unique string identifier for this key.</param>
    /// <exception cref="ArgumentException">Thrown when the name is null or whitespace.</exception>
    public MetadataKey(string name) {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Key name cannot be empty.", nameof(name));

        Name = name.Trim();
    }
}
