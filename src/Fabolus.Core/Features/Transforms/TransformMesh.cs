using BasicResults;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using System.Numerics;

namespace Fabolus.Core.Features.Transforms;

/// <summary>
/// Feature workflow for transforming meshes in the workspace.
/// </summary>
public sealed class TransformMesh {
    private readonly IGeometryEngine _engine;

    public TransformMesh(IGeometryEngine engine) {
        _engine = engine;
    }

    /// <summary>
    /// Translates in place: composes the new delta with any existing net translation, then
    /// replays the full updated Commands list against BaseMesh - not just re-translating
    /// whatever the current geometry happens to be, which would be wrong if another command
    /// (e.g. a generated Mould) now sits on top of this one.
    /// </summary>
    public Result<Workspace> Translate(Workspace workspace, Guid meshId, float deltaX, float deltaY, float deltaZ) {
        var recordResult = workspace.GetRecord(meshId);
        if (recordResult.IsFailure)
            return recordResult.Error;

        var record = recordResult.Value;

        var vector = new Vector3(deltaX, deltaY, deltaZ);
        if (record.Translation() is { } existing) {
            vector += existing; // add vectors to stack
        }

        return Replay(workspace, record.WithTranslate(vector));
    }

    /// <summary>
    /// Rotates in place: composes the new rotation with any existing net rotation, then
    /// replays the full updated Commands list against BaseMesh - not just re-rotating
    /// whatever the current geometry happens to be, which would be wrong if another command
    /// (e.g. a generated Mould) now sits on top of this one.
    /// </summary>
    public Result<Workspace> Rotate(Workspace workspace, Guid meshId, float angleRadians, Vector3 axis) {
        var recordResult = workspace.GetRecord(meshId);
        if (recordResult.IsFailure)
            return recordResult.Error;

        var record = recordResult.Value;

        var numAxis = new System.Numerics.Vector3((float)axis.X, (float)axis.Y, (float)axis.Z);
        var quaternion = Quaternion.CreateFromAxisAngle(numAxis, angleRadians);

        if (record.Rotation() is { } existing) {
            quaternion = quaternion * existing;
        }

        return Replay(workspace, record.WithRotation(quaternion));
    }

    /// <summary>
    /// Undoes rotation in place: replays this mesh's own Commands (minus RotateCommand, and
    /// anything higher-priority that depended on it, e.g. a generated Mould) against its
    /// BaseMesh. Inverting the current geometry directly would be wrong once something can sit
    /// on top of a rotation (e.g. a Mould shell) - that would rotate the shell back, not recover
    /// the pre-rotation solid.
    /// </summary>
    public Result<Workspace> ClearRotation(Workspace workspace, Guid meshId) {
        var recordResult = workspace.GetRecord(meshId);
        if (recordResult.IsFailure)
            return recordResult.Error;

        var record = recordResult.Value;
        if (record.Rotation() is null) {
            return workspace; // no rotation to remove
        }

        // Dropping a command can drop higher-priority ones with it (a generated Mould), so the
        // result is not merely the old geometry un-rotated.
        return Replay(workspace, record.WithoutRotation());
    }

    /// <summary>
    /// Rebuilds an entry's geometry from its base mesh and updated command list, and stores both.
    ///
    /// Measuring the result is free while the history is rigid motions alone: the base mesh is
    /// measured at import, and the engine hands its topology audit and statistics through every
    /// translation and rotation. Only a command that rebuilds the surface - which replay re-runs
    /// anyway, at far greater cost - leaves anything to measure.
    /// </summary>
    private Result<Workspace> Replay(Workspace workspace, MeshRecord record) {
        if (record.BaseMesh is null)
            return MetadataErrors.MissingBaseMesh;

        var replayResult = CommandReplay.Apply(_engine, record.BaseMesh, record.Commands);
        if (replayResult.IsFailure) return replayResult.Error;

        return workspace.UpdateMesh(record.Id, replayResult.Value.Measured(_engine), record);
    }
}
