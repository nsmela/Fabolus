using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Smoothing;

public record SmoothSettings : IMeshCommand {
    public int Iterations { get; init; } = 1;
    public float Intensity { get; init; } = 1.0f;
    public float Inflation { get; init; } = 0.1f;
    public float RemeshRatio { get; init; } = 2.0f;
    public float Resolution { get; init; } = 1.0f;

    public int Priority => CommandPriority.Transform;

    /// <summary>
    /// Runs the volumetric Erosion-Dilation-Resize smoothing pipeline against <paramref name="mesh"/>.
    /// Does not take ownership of <paramref name="mesh"/>; intermediates it creates along the
    /// way are disposed here. Engine modifiers never return their input instance, so every
    /// stage's output is a fresh mesh this pipeline owns until it's replaced or returned.
    /// </summary>
    public string Describe() => $"Smoothing ({Intensity:F2} mm \u00b7 {Iterations}x)";

    public Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh) {
        int baseTriangleCount = mesh.TriangleCount;

        // Erosion through offset cycle
        var offsetResult = engine.Modifiers.OffsetDouble(mesh, Intensity, Iterations, Resolution);
        if (offsetResult.IsFailure) return offsetResult.Error;

        var currentMesh = offsetResult.Value;

        if (currentMesh.TriangleCount == 0) {
            return new Error("Smoothing.OverEroded", "The mesh collapsed due to high intensity. Try reducing Iterations or Intensity.");
        }

        // optional inflation
        if (Math.Abs(Inflation) > 0.001) {
            var inflationResult = engine.Modifiers.Offset(currentMesh, Inflation, Resolution);
            if (inflationResult.IsFailure) return inflationResult.Error;
            currentMesh = inflationResult.Value;
        }

        // Resize (Decimation)
        int targetTriangleCount = (int)(baseTriangleCount * Math.Max(RemeshRatio, 1.0));
        var resizeResult = engine.Modifiers.Resize(currentMesh, targetTriangleCount);

        return resizeResult;
    }
}
