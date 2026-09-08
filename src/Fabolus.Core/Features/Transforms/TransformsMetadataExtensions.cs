using Fabolus.Core.Common;
using Fabolus.Core.Geometry.Metadata;
using System.Linq;
using System.Numerics;

namespace Fabolus.Core.Features.Transforms;

/// <summary>
/// Helpers for reading/updating the net rotation and translation recorded on a mesh.
/// </summary>
public static class TransformMetadataExtensions {
    public static MeshMetadata WithoutRotation(this MeshMetadata metadata) =>
        metadata.WithoutCommand<RotateCommand>();

    public static MeshMetadata WithoutTranslate(this MeshMetadata metadata) =>
        metadata.WithoutCommand<TranslateCommand>();

    public static MeshMetadata WithRotation(this MeshMetadata metadata, Quaternion q) =>
        metadata.WithCommand(new RotateCommand(q));

    public static MeshMetadata WithTranslate(this MeshMetadata metadata, Vector3 v) =>
        metadata.WithCommand(new TranslateCommand(v));

    public static Maybe<Quaternion> Rotation(this MeshMetadata metadata) {
        var command = metadata.Commands.OfType<RotateCommand>().FirstOrDefault();
        return command is null ? Maybe<Quaternion>.None() : Maybe<Quaternion>.Some(command.Rotation);
    }

    public static Maybe<Vector3> Translation(this MeshMetadata metadata) {
        var command = metadata.Commands.OfType<TranslateCommand>().FirstOrDefault();
        return command is null ? Maybe<Vector3>.None() : Maybe<Vector3>.Some(command.Translation);
    }
}
