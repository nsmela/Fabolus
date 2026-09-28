using Fabolus.Core.Geometry;
using System.Numerics;

namespace Fabolus.Core.Features.Transforms;

/// <summary>
/// Reading and updating the net rotation and translation recorded on a workspace entry.
/// </summary>
public static class TransformRecordExtensions {
    public static MeshRecord WithoutRotation(this MeshRecord record) =>
        record.WithoutCommand<RotateCommand>();

    public static MeshRecord WithoutTranslate(this MeshRecord record) =>
        record.WithoutCommand<TranslateCommand>();

    public static MeshRecord WithRotation(this MeshRecord record, Quaternion q) =>
        record.WithCommand(new RotateCommand(q));

    public static MeshRecord WithTranslate(this MeshRecord record, Vector3 v) =>
        record.WithCommand(new TranslateCommand(v));

    /// <summary>The net rotation applied to this entry, or null if it has never been rotated.</summary>
    public static Quaternion? Rotation(this MeshRecord record) =>
        record.Command<RotateCommand>()?.Rotation;

    /// <summary>The net translation applied to this entry, or null if it has never been moved.</summary>
    public static Vector3? Translation(this MeshRecord record) =>
        record.Command<TranslateCommand>()?.Translation;
}
