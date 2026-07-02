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

        var applyResult = settings.Apply(Engine, originalMesh);
        if (applyResult.IsFailure) return applyResult.Error;

        // Finalize metadata and update workspace
        var finalMesh = applyResult.Value;

        var topology = Engine.Evaluators.ValidateTopology(finalMesh).Value;
        var stats = Engine.Evaluators.GetStatistics(finalMesh).Value;
        var metadata = activeMesh.Metadata.WithProperties(m => {
            if (isForked) {
                m.Set(CoreKeys.Id, workingMeshId)
                 .Set(CoreKeys.Name, $"{originalMesh.Metadata.Name} (Smoothed)")
                 .Set(CoreKeys.DerivedFrom, originalMesh.Metadata.Id);
            }

            m.Set(MeshIOKeys.Stats, stats)
             .Set(MeshIOKeys.Topology, topology);
        });
        metadata = metadata.WithCommand(settings);
        if (isForked) {
            metadata = metadata.WithBaseMesh(originalMesh);
        }

        finalMesh = finalMesh.WithMetadata(metadata);

        // Update workspace
        if (isForked) {
            return workspace.AddMesh(finalMesh);
        } else {
            return workspace.UpdateMesh(finalMesh);
        }

    }
}
