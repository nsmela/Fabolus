using Fabolus.Core.Common;

namespace Fabolus.Core.Geometry.Metadata;

/// <summary>
/// Defines standard error results related to metadata operations.
/// </summary>
public static class MetadataErrors {
    /// <summary>
    /// Error returned when a requested metadata key is not present in the dictionary.
    /// </summary>
    public static readonly Error KeyNotFound = new("Metadata.NotFound", "The key used is not found in the metadata");
}
