using Fabolus.Core.Common;
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

        var targetMesh = mesh;
        var derivedResult = mesh.Metadata.DerivedFrom;
        if (translateResult.HasValue && derivedResult.HasValue) {
            getMeshResult = workspace.GetMesh(derivedResult.Value);
            if (getMeshResult.IsFailure)
                return getMeshResult.Error;

            targetMesh = getMeshResult.Value;
        }

        var transformedResult = _engine.Transforms.Translate(targetMesh, vector.X, vector.Y, vector.Z);
        if (transformedResult.IsFailure)
            return transformedResult.Error;

        var transformedMesh = transformedResult.Value;
        var metadata = targetMesh.Metadata.WithProperties(m =>
            m.Set(CoreKeys.DerivedFrom, targetMesh.Metadata.Id)
        );
        metadata = metadata.WithCommand(new TranslateCommand(vector));
        transformedMesh = transformedMesh.WithMetadata(metadata);

        return workspace.UpdateMesh(transformedMesh);
    }

    public Result<Workspace> Rotate(Workspace workspace, Guid meshId, float angleRadians, Vector3 axis) {
        var getMeshResult = workspace.GetMesh(meshId);
        if (getMeshResult.IsFailure)
            return getMeshResult.Error;

        var mesh = getMeshResult.Value;

        var quaternion = Quaternion.CreateFromAxisAngle(axis, angleRadians);

        var transformedResult = _engine.Transforms.Rotate(mesh, quaternion);
        if (transformedResult.IsFailure)
            return transformedResult.Error;

        var transformedMesh = transformedResult.Value;

        var rotationResult = mesh.Metadata.Rotation();
        if (rotationResult.HasValue) {
            quaternion = quaternion * rotationResult.Value ;
        }

        var metadata = transformedMesh.Metadata.WithCommand(new RotateCommand(quaternion));
        transformedMesh = transformedMesh.WithMetadata(metadata);

        return workspace.UpdateMesh(transformedMesh);
    }

    /// <summary>
    /// Reverts to the original geometry by discarding the current derived mesh 
    /// and restoring the parent mesh.
    /// </summary>
    public Result<Workspace> ClearRotation(Workspace workspace, Guid meshId) {
        var getMeshResult = workspace.GetMesh(meshId);
        if (getMeshResult.IsFailure)
            return getMeshResult.Error;

        var mesh = getMeshResult.Value;

        var metadata = mesh.Metadata;
        var rotationResult = metadata.Rotation();
        if (rotationResult.HasNoValue) {
            return workspace; // no rotation to remove
        }

        var quaternion = rotationResult.Value;

        // reverse rotation by multiplying by inverse
        quaternion = Quaternion.Conjugate(quaternion);

        var transformedResult = _engine.Transforms.Rotate(mesh, quaternion);
        if (transformedResult.IsFailure)
            return transformedResult.Error;

        var transformedMesh = transformedResult.Value;
        transformedMesh = transformedMesh.WithMetadata(metadata.WithoutCommand<RotateCommand>());
        return workspace.UpdateMesh(transformedMesh);
    }

}