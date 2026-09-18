using BasicResults;
using Fabolus.Core.Geometry.Metadata;
using System.Linq;
using System.Numerics;

namespace Fabolus.Core.Features.Transforms;

/// <summary>
/// Helpers for reading/updating the net rotation and translation recorded on a mesh.
/// </summary>
public static class TransformMetadataExtensions {
    public static MeshMetadata WithoutRotation(this GeometryEngine.Core.Geometry.MeshMetadata metadataBase) =>
        metadataBase.AsFabolus().WithoutCommand<RotateCommand>();

    public static MeshMetadata WithoutTranslate(this GeometryEngine.Core.Geometry.MeshMetadata metadataBase) =>
        metadataBase.AsFabolus().WithoutCommand<TranslateCommand>();

    public static MeshMetadata WithRotation(this GeometryEngine.Core.Geometry.MeshMetadata metadataBase, Quaternion q) =>
        metadataBase.AsFabolus().WithCommand(new RotateCommand(q));

    public static MeshMetadata WithTranslate(this GeometryEngine.Core.Geometry.MeshMetadata metadataBase, Vector3 v) =>
        metadataBase.AsFabolus().WithCommand(new TranslateCommand(v));

    public static Maybe<Quaternion> Rotation(this GeometryEngine.Core.Geometry.MeshMetadata metadataBase) {
        var command = metadataBase.AsFabolus().Commands.OfType<RotateCommand>().FirstOrDefault();
        return command is null ? Maybe<Quaternion>.None() : Maybe<Quaternion>.Some(command.Rotation);
    }

    public static Maybe<Vector3> Translation(this GeometryEngine.Core.Geometry.MeshMetadata metadataBase) {
        var command = metadataBase.AsFabolus().Commands.OfType<TranslateCommand>().FirstOrDefault();
        return command is null ? Maybe<Vector3>.None() : Maybe<Vector3>.Some(command.Translation);
    }
}
