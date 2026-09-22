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

        return Replay(workspace, record.WithTranslate(vector), rigid: true);
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

        return Replay(workspace, record.WithRotation(quaternion), rigid: true);
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
        // result is not merely the old geometry un-rotated and the topology has to be re-read.
        return Replay(workspace, record.WithoutRotation(), rigid: false);
    }

    /// <summary>
    /// Rebuilds an entry's geometry from its base mesh and updated command list, and stores both.
    /// </summary>
    /// <param name="rigid">
    /// True when the only thing that changed is a rigid transform. Every other command in the list
    /// is then unchanged and the new one leaves connectivity alone, so the topology audit taken
    /// before this call still reads the same and is carried across rather than recomputed - it
    /// would walk every edge to learn what the entry already knew. Only the bounds move.
    /// </param>
    private Result<Workspace> Replay(Workspace workspace, MeshRecord record, bool rigid) {
        if (record.BaseMesh is null)
            return MetadataErrors.MissingBaseMesh;

        var previous = workspace.GetMesh(record.Id);

        var replayResult = CommandReplay.Apply(_engine, record.BaseMesh, record.Commands);
        if (replayResult.IsFailure) return replayResult.Error;

        var mesh = replayResult.Value;

        mesh = rigid && previous.IsSuccess && previous.Value.Topology() is { } topology
            ? mesh.WithAnnotations(new FabolusAnnotations(Topology: topology)).WithRefreshedStats(_engine)
            : mesh.WithMeasurements(_engine);

        return workspace.UpdateMesh(record.Id, mesh, record);
    }
}
