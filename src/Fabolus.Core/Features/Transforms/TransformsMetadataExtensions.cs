using Fabolus.Core.Common;
using Fabolus.Core.Geometry.Metadata;
using System.Collections.Immutable;
using System.Numerics;

namespace Fabolus.Core.Features.Transforms;

/// <summary>
/// Metadata keys specific to the transformation feature.
/// </summary>
public static class TransformKeys {
    /// <summary>
    /// Tracks the sequential history of all spatial transformations applied to a mesh.
    /// </summary>
    public static readonly MetadataKey<Quaternion> Rotation = new("Transform.Rotate");
    public static readonly MetadataKey<Vector3> Translation = new("Transform.Translate");
}


/// <summary>
/// Helpers for safely appending transformation history.
/// </summary>
public static class TransformMetadataExtensions {
    public static MeshMetadata WithoutRotation(this MeshMetadata metadata) =>
    metadata.WithoutProperty(TransformKeys.Rotation);

    public static MeshMetadata WithoutTranslate(this MeshMetadata metadata) =>
        metadata.WithoutProperty(TransformKeys.Translation);

    public static MeshMetadata WithRotation(this MeshMetadata metadata, Quaternion q) =>
        metadata.WithProperty(TransformKeys.Rotation, q);

    public static MeshMetadata WithTranslate(this MeshMetadata metadata, Vector3 v) =>
        metadata.WithProperty(TransformKeys.Translation, v);

    public static Maybe<Quaternion> Rotation(this MeshMetadata metadata) => metadata.GetProperty(TransformKeys.Rotation);
    public static Maybe<Vector3> Translation(this MeshMetadata metadata) => metadata.GetProperty(TransformKeys.Translation);
}
