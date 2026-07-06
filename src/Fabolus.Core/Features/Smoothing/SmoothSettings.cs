using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Smoothing;

public record SmoothSettings(int Iterations = 1, float Intensity = 1.0f, float Inflation = 0.1f, float RemeshRatio = 2.0f, float Resolution = 1.0f) : IMeshCommand {
    public int Priority => CommandPriority.Transform;

    /// <summary>
    /// Runs the volumetric Erosion-Dilation-Resize smoothing pipeline against <paramref name="mesh"/>.
    /// Does not take ownership of <paramref name="mesh"/>; intermediates it creates along the
    /// way are disposed here (guarding against ops that return their input unchanged).
    /// </summary>
    public Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh) {
        int baseTriangleCount = mesh.TriangleCount;

        // Erosion through offset cycle
        var offsetResult = engine.Modifiers.OffsetDouble(mesh, Intensity, Iterations, Resolution);
        if (offsetResult.IsFailure) return offsetResult.Error;

        var currentMesh = offsetResult.Value;

        if (currentMesh.TriangleCount == 0) {
            DisposeIntermediate(currentMesh, mesh);
            return new Error("Smoothing.OverEroded", "The mesh collapsed due to high intensity. Try reducing Iterations or Intensity.");
        }

        // optional inflation
        if (Math.Abs(Inflation) > 0.001) {
            var inflationResult = engine.Modifiers.Offset(currentMesh, Inflation, Resolution);
            if (inflationResult.IsFailure) {
                DisposeIntermediate(currentMesh, mesh);
                return inflationResult.Error;
            }
            if (!ReferenceEquals(inflationResult.Value, currentMesh)) {
                DisposeIntermediate(currentMesh, mesh);
            }
            currentMesh = inflationResult.Value;
        }

        // Resize (Decimation)
        int targetTriangleCount = (int)(baseTriangleCount * Math.Max(RemeshRatio, 1.0));
        var resizeResult = engine.Modifiers.Resize(currentMesh, targetTriangleCount);
        if (resizeResult.IsFailure) {
            DisposeIntermediate(currentMesh, mesh);
            return resizeResult.Error;
        }
        if (!ReferenceEquals(resizeResult.Value, currentMesh)) {
            DisposeIntermediate(currentMesh, mesh);
        }

        return resizeResult;
    }

    // Never disposes the caller's input mesh, only meshes this pipeline created.
    private static void DisposeIntermediate(IMesh intermediate, IMesh input) {
        if (!ReferenceEquals(intermediate, input)) intermediate.Dispose();
    }
}
