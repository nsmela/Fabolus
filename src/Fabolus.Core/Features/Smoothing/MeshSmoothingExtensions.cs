using Fabolus.Core.Common;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Smoothing;

public static class SmoothKeys {
    public static readonly MetadataKey<SmoothSettings> SmoothSettings = new("Smoothing.Settings");
}

public static class MeshSmoothingExtensions {
    public static Maybe<SmoothSettings> GetSmoothing(this MeshMetadata metadata) =>
        metadata.GetProperty(SmoothKeys.SmoothSettings);

    public static MeshMetadata WithSmoothing(this MeshMetadata metadata, SmoothSettings settings) =>
        metadata.WithProperty(SmoothKeys.SmoothSettings, settings);

}
