using Fabolus.Core.Common;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Smoothing;

/// <summary>
/// Smooths a mesh using a volumetric Erosion-Dilation-Resize pipeline.
/// This approach produces high-quality, manifold, and feature-preserved surfaces.
/// </summary>
public sealed class SmoothMesh(IGeometryEngine Engine) {
    /// <summary>
    /// Smooths the specified mesh.
    /// </summary>
    /// <param name="workspace">The current workspace.</param>
    /// <param name="meshId">The ID of the mesh to smooth.</param>
    /// <param name="iterations">Effective smoothing passes (maps to offset distance).</param>
    /// <param name="intensity">The strength of each pass (maps to offset distance).</param>
    /// <param name="ratio">The target complexity relative to the original mesh (e.g. 2.0 = double the triangles).</param>
    public Result<Workspace> Execute(
        Workspace workspace, 
        SmoothSettings settings) 
    {
        var getMeshResult = workspace.GetActiveMesh();
        if (getMeshResult.IsFailure) return getMeshResult.Error;

        var activeMesh = getMeshResult.Value;

        // use derived Mesh to prevent smoothing stacking / degrading
        IMesh originalMesh;
        Guid workingMeshId;
        bool isForked = false;

        var derivedResult = activeMesh.Metadata.DerivedFrom;
        if (derivedResult.HasValue) {
            // use parent
            var parentResult = workspace.GetMesh(derivedResult.Value);
            if (parentResult.IsFailure) return parentResult.Error;

            originalMesh = parentResult.Value;
            workingMeshId = activeMesh.Metadata.Id;
        } else {
            // need to derive
            originalMesh = activeMesh;
            workingMeshId = Guid.NewGuid();
            isForked = true;
        }

        int baseTriangleCount = originalMesh.TriangleCount;

        // Erosion through offset cycle
        var offsetResult = Engine.Modifiers.OffsetDouble(originalMesh, settings.Intensity, settings.Iterations, settings.Resolution);
        if (offsetResult.IsFailure) return offsetResult.Error;

        if (offsetResult.Value.TriangleCount == 0)
            return new Error("Smoothing.OverEroded", "The mesh collapsed due to high intensity. Try reducing Iterations or Intensity.");

        var currentMesh = offsetResult.Value;

        // optional inflation 
        if (Math.Abs(settings.Inflation) > 0.001) {
            var inflationResult = Engine.Modifiers.Offset(currentMesh, settings.Inflation, settings.Resolution);
            if (inflationResult.IsFailure) return inflationResult.Error;
            currentMesh = inflationResult.Value;
        }

        // Resize (Decimation)
        int targetTriangleCount = (int)(baseTriangleCount * Math.Max(settings.RemeshRatio, 1.0));
        var resizeResult = Engine.Modifiers.Resize(currentMesh, targetTriangleCount);
        if (resizeResult.IsFailure) return resizeResult.Error;

        // Finalize metadata and update workspace
        var finalMesh = resizeResult.Value;

        var topology = Engine.Evaluators.ValidateTopology(finalMesh).Value;
        var stats = Engine.Evaluators.GetStatistics(finalMesh).Value;
        var metadata = activeMesh.Metadata.WithProperties(m => {
            if (isForked) {
                m.Set(CoreKeys.Id, workingMeshId)
                 .Set(CoreKeys.Name, $"{originalMesh.Metadata.Name} (Smoothed)")
                 .Set(CoreKeys.DerivedFrom, originalMesh.Metadata.Id);
            }

            m.Set(MeshIOKeys.Stats, stats)
             .Set(MeshIOKeys.Topology, topology)
             .Set(SmoothKeys.SmoothSettings, settings);
        });

        finalMesh = finalMesh.WithMetadata(metadata);

        // Update workspace
        if (isForked) {
            return workspace.AddMesh(finalMesh);
        } else {
            return workspace.UpdateMesh(finalMesh);
        }

    }
}
