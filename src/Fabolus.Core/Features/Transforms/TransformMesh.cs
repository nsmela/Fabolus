using Fabolus.Core.Common;
using Fabolus.Core.Features.MeshIO;
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
        var getMeshResult = workspace.GetMesh(meshId);
        if (getMeshResult.IsFailure)
            return getMeshResult.Error;

        var mesh = getMeshResult.Value;

        var vector = new Vector3(deltaX, deltaY, deltaZ);
        var translateResult = mesh.Metadata.Translation();
        if (translateResult.HasValue) {
            vector += translateResult.Value; // add vectors to stack
        }

        // BaseMesh is guaranteed present - Workspace.AddMesh establishes it for every mesh
        // the moment it enters the workspace - and carries forward automatically below since
        // updatedMetadata is built from mesh.Metadata, which already has it. The copy is
        // consumed by the replay.
        var baseMesh = mesh.Metadata.GetBaseMesh().Value;
        var updatedMetadata = mesh.Metadata.WithCommand(new TranslateCommand(vector));

        var replayResult = CommandReplay.Apply(_engine, baseMesh, updatedMetadata.Commands);
        if (replayResult.IsFailure) return replayResult.Error;

        var transformedMesh = replayResult.Value;

        // Rigid transforms preserve topology (no need to re-validate) but move the bounding
        // box - refresh Stats so anything sized from it (e.g. the rotation axis gizmo) sees
        // the mesh's new extents.
        var stats = _engine.Evaluators.GetStatistics(transformedMesh).Value;
        var metadata = updatedMetadata.WithProperties(m => m.Set(MeshIOKeys.Stats, stats));
        transformedMesh = transformedMesh.WithMetadata(metadata);

        return workspace.UpdateMesh(transformedMesh);
    }

    /// <summary>
    /// Rotates in place: composes the new rotation with any existing net rotation, then
    /// replays the full updated Commands list against BaseMesh - not just re-rotating
    /// whatever the current geometry happens to be, which would be wrong if another command
    /// (e.g. a generated Mould) now sits on top of this one.
    /// </summary>
    public Result<Workspace> Rotate(Workspace workspace, Guid meshId, float angleRadians, Vector3 axis) {
        var getMeshResult = workspace.GetMesh(meshId);
        if (getMeshResult.IsFailure)
            return getMeshResult.Error;

        var mesh = getMeshResult.Value;

        var quaternion = Quaternion.CreateFromAxisAngle(axis, angleRadians);

        var rotationResult = mesh.Metadata.Rotation();
        if (rotationResult.HasValue) {
            quaternion = quaternion * rotationResult.Value;
        }

        // BaseMesh is guaranteed present - Workspace.AddMesh establishes it for every mesh
        // the moment it enters the workspace - and carries forward automatically below since
        // updatedMetadata is built from mesh.Metadata, which already has it. The copy is
        // consumed by the replay.
        var baseMesh = mesh.Metadata.GetBaseMesh().Value;
        var updatedMetadata = mesh.Metadata.WithCommand(new RotateCommand(quaternion));

        var replayResult = CommandReplay.Apply(_engine, baseMesh, updatedMetadata.Commands);
        if (replayResult.IsFailure) return replayResult.Error;

        var transformedMesh = replayResult.Value;

        // Rigid transforms preserve topology (no need to re-validate) but move the bounding
        // box - refresh Stats so anything sized from it (e.g. the rotation axis gizmo) sees
        // the mesh's new extents.
        var stats = _engine.Evaluators.GetStatistics(transformedMesh).Value;
        var metadata = updatedMetadata.WithProperties(m => m.Set(MeshIOKeys.Stats, stats));
        transformedMesh = transformedMesh.WithMetadata(metadata);

        return workspace.UpdateMesh(transformedMesh);
    }

    /// <summary>
    /// Undoes rotation in place: replays this mesh's own Commands (minus RotateCommand, and
    /// anything higher-priority that depended on it, e.g. a generated Mould) against its
    /// BaseMesh. Inverting the current geometry directly would be wrong once something can sit
    /// on top of a rotation (e.g. a Mould shell) - that would rotate the shell back, not recover
    /// the pre-rotation solid.
    /// </summary>
    public Result<Workspace> ClearRotation(Workspace workspace, Guid meshId) {
        var getMeshResult = workspace.GetMesh(meshId);
        if (getMeshResult.IsFailure)
            return getMeshResult.Error;

        var mesh = getMeshResult.Value;

        var rotationResult = mesh.Metadata.Rotation();
        if (rotationResult.HasNoValue) {
            return workspace; // no rotation to remove
        }

        var baseMesh = mesh.Metadata.GetBaseMesh().Value;
        var revertedMetadata = mesh.Metadata.WithoutCommand<RotateCommand>();

        var replayResult = CommandReplay.Apply(_engine, baseMesh, revertedMetadata.Commands);
        if (replayResult.IsFailure) return replayResult.Error;

        var currentMesh = replayResult.Value;

        var topology = _engine.Evaluators.ValidateTopology(currentMesh).Value;
        var stats = _engine.Evaluators.GetStatistics(currentMesh).Value;
        var metadata = revertedMetadata.WithProperties(m => m
            .Set(MeshIOKeys.Stats, stats)
            .Set(MeshIOKeys.Topology, topology));

        var finalMesh = currentMesh.WithMetadata(metadata);
        return workspace.UpdateMesh(finalMesh);
    }

}
