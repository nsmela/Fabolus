using Fabolus.Core.Common;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Smoothing;

public static class SmoothKeys {
    public static readonly MetadataKey<bool> IsSmoothed = new("Smoothing.IsSmoothed");
    public static readonly MetadataKey<int> Iterations = new("Smoothing.Iterations");
    public static readonly MetadataKey<float> Intensity = new("Smoothing.Intensity");
    public static readonly MetadataKey<double> RemeshRatio = new("Smoothing.RemeshRatio");
    public static readonly MetadataKey<float> Inflation = new("Smoothing.Inflation");
    public static readonly MetadataKey<string> OriginalColor = new("Smoothing.OriginalColor");
}

public static class MeshSmoothingExtensions {
    public static Result<bool> IsSmoothed(this MeshMetadata metadata) =>
        metadata.GetProperty(SmoothKeys.IsSmoothed);

    public static Result<int> GetSmoothingIterations(this MeshMetadata metadata) =>
        metadata.GetProperty(SmoothKeys.Iterations);

    public static Result<float> GetSmoothingIntensity(this MeshMetadata metadata) =>
        metadata.GetProperty(SmoothKeys.Intensity);

    public static Result<double> GetSmoothingRemeshRatio(this MeshMetadata metadata) =>
        metadata.GetProperty(SmoothKeys.RemeshRatio);

    public static Result<float> GetSmoothingInflation(this MeshMetadata metadata) =>
        metadata.GetProperty(SmoothKeys.Inflation);

    public static MeshMetadata WithIsSmoothed(this MeshMetadata metadata, bool value) =>
    metadata.WithProperty(SmoothKeys.IsSmoothed, value);

    public static MeshMetadata WithSmoothing(this MeshMetadata metadata, int iterations, float intensity, double ratio, float inflation) =>
        metadata.WithProperties(m => {
            m.Set(SmoothKeys.IsSmoothed, true)
             .Set(SmoothKeys.Iterations, iterations)
             .Set(SmoothKeys.Intensity, intensity)
             .Set(SmoothKeys.RemeshRatio, ratio)
             .Set(SmoothKeys.Inflation, inflation)
             .Set(CoreKeys.CreatedBy, $"Smooth(i={iterations}, s={intensity:F2}, r={ratio:F1}, f={inflation:F2})");
        });

    public static MeshMetadata WithoutSmoothing(this MeshMetadata metadata) {

        var createdByResult = metadata.GetProperty(CoreKeys.CreatedBy);

        // 2. Safely strip all smoothing properties in a single allocation
        return metadata.WithProperties(m => {
            m.Set(SmoothKeys.IsSmoothed, false)
             .Remove(SmoothKeys.Iterations)
             .Remove(SmoothKeys.Intensity)
             .Remove(SmoothKeys.RemeshRatio)
             .Remove(SmoothKeys.Inflation);

            if (createdByResult.IsSuccess && createdByResult.Value.StartsWith("Smooth")) {
                m.Set(CoreKeys.CreatedBy, "Clear Smoothing");
            }
        });
    }
}
