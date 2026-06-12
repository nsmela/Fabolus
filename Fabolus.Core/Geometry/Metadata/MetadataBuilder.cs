using System.Collections.Immutable;

namespace Fabolus.Core.Geometry.Metadata;

/// <summary>
/// A temporary, mutable builder for batch-updating metadata safely.
/// Allows multiple properties to be set while allocating only a single new dictionary.
/// </summary>
public sealed class MetadataBuilder {
    private readonly ImmutableDictionary<string, object>.Builder _builder;

    internal MetadataBuilder(ImmutableDictionary<string, object>.Builder builder) {
        _builder = builder;
    }

    /// <summary>
    /// Type-safe setter for batch updates.
    /// </summary>
    /// <typeparam name="T">The type of the value being set.</typeparam>
    /// <param name="key">The strongly-typed metadata key.</param>
    /// <param name="value">The value to associate with the key.</param>
    /// <returns>The current builder instance to allow fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the provided value is null.</exception>
    public MetadataBuilder Set<T>(MetadataKey<T> key, T value) {
        ArgumentNullException.ThrowIfNull(value);

        // The builder acts like a standard mutable dictionary
        _builder[key.Name] = value;

        // Returning 'this' allows for fluent chaining if desired
        return this;
    }

    /// <summary>
    /// Removes a property safely during a batch update.
    /// </summary>
    /// <typeparam name="T">The type of the value associated with the key.</typeparam>
    /// <param name="key">The strongly-typed metadata key to remove.</param>
    /// <returns>The current builder instance to allow fluent chaining.</returns>
    public MetadataBuilder Remove<T>(MetadataKey<T> key) {
        _builder.Remove(key.Name);
        return this;
    }
}
