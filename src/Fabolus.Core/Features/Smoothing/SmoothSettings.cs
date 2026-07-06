using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Smoothing;

public record SmoothSettings(int Iterations = 1, float Intensity = 1.0f, float Inflation = 0.1f, float RemeshRatio = 2.0f, float Resolution = 1.0f) : IMeshCommand {
    public int Priority => CommandPriority.Transform;

    /// <summary>
    /// Runs the volumetric Erosion-Dilation-Resize smoothing pipeline against <paramref name="mesh"/>.
    /// Does not take ownership of <paramref name="mesh"/>; intermediates it creates along the
    /// way are disposed here. Engine modifiers never return their input instance, so every
    /// stage's output is a fresh mesh this pipeline owns until it's replaced or returned.
    /// </summary>
    public Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh) {
        int baseTriangleCount = mesh.TriangleCount;

        // Erosion through offset cycle
        var offsetResult = engine.Modifiers.OffsetDouble(mesh, Intensity, Iterations, Resolution);
        if (offsetResult.IsFailure) return offsetResult.Error;

        var currentMesh = offsetResult.Value;

        if (currentMesh.TriangleCount == 0) {
            currentMesh.Dispose();
            return new Error("Smoothing.OverEroded", "The mesh collapsed due to high intensity. Try reducing Iterations or Intensity.");
        }

        // optional inflation
        if (Math.Abs(Inflation) > 0.001) {
            var inflationResult = engine.Modifiers.Offset(currentMesh, Inflation, Resolution);
            currentMesh.Dispose();
            if (inflationResult.IsFailure) return inflationResult.Error;
            currentMesh = inflationResult.Value;
        }

        // Resize (Decimation)
        int targetTriangleCount = (int)(baseTriangleCount * Math.Max(RemeshRatio, 1.0));
        var resizeResult = engine.Modifiers.Resize(currentMesh, targetTriangleCount);
        currentMesh.Dispose();

        return resizeResult;
    }
}
