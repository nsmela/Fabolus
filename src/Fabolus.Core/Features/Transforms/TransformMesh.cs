using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Transforms;

/// <summary>
/// Feature workflow for transforming meshes in the workspace.
/// </summary>
public sealed class TransformMesh {
    private readonly IGeometryEngine _engine;

    public TransformMesh(IGeometryEngine engine) {
        _engine = engine;
    }

    public Result<Workspace> Translate(Workspace workspace, Guid meshId, double deltaX, double deltaY, double deltaZ) {
        var getMeshResult = workspace.GetMesh(meshId);
        if (getMeshResult.IsFailure)
            return getMeshResult.Error;

        var forkResult = EnsureDerivedWorkingMesh(workspace, getMeshResult.Value);
        if (forkResult.IsFailure) return forkResult.Error;

        var (currentWorkspace, workingMesh) = forkResult.Value;

        var transformResult = _engine.Transforms.Translate(workingMesh, deltaX, deltaY, deltaZ);
        if (transformResult.IsFailure)
            return transformResult.Error;

        var transformedMesh = transformResult.Value;

        var updatedMetadata = transformedMesh.Metadata.WithTransformRecord($"Translated by (X: {deltaX:F2}, Y: {deltaY:F2}, Z: {deltaZ:F2})");

        return currentWorkspace.UpdateMesh(transformedMesh.WithMetadata(updatedMetadata));
    }

    public Result<Workspace> Rotate(Workspace workspace, Guid meshId, double angleRadians, RotationAxis axis) {
        var getMeshResult = workspace.GetMesh(meshId);
        if (getMeshResult.IsFailure)
            return getMeshResult.Error;

        var axisVector = axis switch {
            RotationAxis.X => (1.0, 0.0, 0.0),
            RotationAxis.Y => (0.0, 1.0, 0.0),
            RotationAxis.Z => (0.0, 0.0, 1.0),
            _ => (double.NaN, double.NaN, double.NaN)
        };

        if (double.IsNaN(axisVector.Item1))
            return MeshErrors.UnknownRotationAxis;

        var forkResult = EnsureDerivedWorkingMesh(workspace, getMeshResult.Value);
        if (forkResult.IsFailure) return forkResult.Error;

        var (currentWorkspace, workingMesh) = forkResult.Value;

        var (x, y, z) = axisVector;
        var transformResult = _engine.Transforms.Rotate(workingMesh, angleRadians, x, y, z);
        if (transformResult.IsFailure)
            return transformResult.Error;

        var transformedMesh = transformResult.Value;

        double angleDegrees = angleRadians * (180.0 / Math.PI);
        var updatedMetadata = transformedMesh.Metadata.WithTransformRecord(
            $"Rotated {angleDegrees:F1} degrees on {axis} axis");

        return currentWorkspace.UpdateMesh(transformedMesh.WithMetadata(updatedMetadata));
    }

    public Result<Workspace> Scale(Workspace workspace, Guid meshId, double scaleFactor) {
        var getMeshResult = workspace.GetMesh(meshId);
        if (getMeshResult.IsFailure)
            return getMeshResult.Error;

        var forkResult = EnsureDerivedWorkingMesh(workspace, getMeshResult.Value);
        if (forkResult.IsFailure) return forkResult.Error;

        var (currentWorkspace, workingMesh) = forkResult.Value;

        var transformResult = _engine.Transforms.Scale(workingMesh, scaleFactor);
        if (transformResult.IsFailure)
            return transformResult.Error;

        var transformedMesh = transformResult.Value;
        var updatedMetadata = transformedMesh.Metadata.WithTransformRecord(
                    $"Scaled by {scaleFactor:F2}x");

        return currentWorkspace.UpdateMesh(transformedMesh.WithMetadata(updatedMetadata));
    }

    /// <summary>
    /// Reverts to the original geometry by discarding the current derived mesh 
    /// and restoring the parent mesh.
    /// </summary>
    public Result<Workspace> ClearTransforms(Workspace workspace, Guid activeMeshId) {
        var getMeshResult = workspace.GetMesh(activeMeshId);
        if (getMeshResult.IsFailure)
            return getMeshResult.Error;

        var activeMesh = getMeshResult.Value;

        var derivedFromResult = activeMesh.Metadata.DerivedFrom;
        if (derivedFromResult.HasNoValue)
            return new Error("Transform.NotDerived", "This mesh has no base geometry to revert to.");

        Guid parentId = derivedFromResult.Value;

        var getParentResult = workspace.GetMesh(parentId);
        if (getParentResult.IsFailure)
            return new Error("Transform.ParentMissing", "The original base mesh is missing from the workspace.");

        var parentMesh = getParentResult.Value;

        var updateResult = workspace.RemoveMesh(activeMeshId);
        if (updateResult.IsFailure)
            return updateResult.Error;

        updateResult = updateResult.Value.SetActiveMesh(parentId);
        if (updateResult.IsFailure)
            return updateResult.Error;

        return Result.Success(updateResult.Value);
    }

    /// <summary>
    /// Ensures that the mesh being operated on is a derived working copy.
    /// If it is a base mesh, it creates a derived clone and hides the original.
    /// </summary>
    private Result<(Workspace Workspace, IMesh WorkingMesh)> EnsureDerivedWorkingMesh(Workspace workspace, IMesh targetMesh) {
        // If it's already a derived mesh, just return it as-is
        if (targetMesh.Metadata.DerivedFrom.HasValue)
            return (workspace, targetMesh);

        Guid newCloneId = Guid.NewGuid();

        var clonedResult = _engine.CloneMesh(targetMesh);
        if (clonedResult.IsFailure)
            return clonedResult.Error;

        var clonedGeometry = clonedResult.Value;

        // Set up the derived metadata
        var cloneMetadata = targetMesh.Metadata.WithProperties(m => {
            m.Set(CoreKeys.Id, newCloneId);
            m.Set(CoreKeys.Name, $"{targetMesh.Metadata.Name} (Transformed)");
            m.Set(CoreKeys.DerivedFrom, targetMesh.Metadata.Id);
            m.Set(CoreKeys.CreatedBy, "Transformation Fork");
        });

        var workingMesh = clonedGeometry.WithMetadata(cloneMetadata);

        // Add the new derived clone to the workspace
        var updateResult = workspace.AddMesh(workingMesh);
        if (updateResult.IsFailure)
            return updateResult.Error;

        // set active mesh as the new cloned mesh
        updateResult = updateResult.Value.SetActiveMesh(workingMesh.Metadata.Id);
        if (updateResult.IsFailure)
            return updateResult.Error;

        var updatedWorkspace = updateResult.Value;
        return Result.Success((updatedWorkspace, workingMesh));
    }
}
