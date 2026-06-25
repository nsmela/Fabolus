using Fabolus.Core.Geometry.Metadata;
using System.Collections.Immutable;

namespace Fabolus.Core.Features.Transforms;

/// <summary>
/// A single entry in a mesh's transformation history.
/// </summary>
public sealed record TransformRecord(string Operation, DateTime Timestamp);

/// <summary>
/// Metadata keys specific to the transformation feature.
/// </summary>
public static class TransformKeys {
    /// <summary>
    /// Tracks the sequential history of all spatial transformations applied to a mesh.
    /// </summary>
    public static readonly MetadataKey<ImmutableList<TransformRecord>> History = new("Transform.History");
}


/// <summary>
/// Helpers for safely appending transformation history.
/// </summary>
public static class TransformMetadataExtensions {
    public static MeshMetadata WithTransformRecord(this MeshMetadata metadata, string operationDescription) {
        var newRecord = new TransformRecord(operationDescription, DateTime.UtcNow);

        // Safely get existing history or start a new list
        var historyResult = metadata.GetProperty(TransformKeys.History);
        var currentHistory = historyResult.HasValue ? historyResult.Value : ImmutableList<TransformRecord>.Empty;

        return metadata.WithProperty(TransformKeys.History, currentHistory.Add(newRecord));
    }
}
