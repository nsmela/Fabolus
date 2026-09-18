using BasicResults;
using Fabolus.Core.Geometry.Metadata;
using System.Linq;

namespace Fabolus.Core.Features.Smoothing;

public static class MeshSmoothingExtensions {
    public static Maybe<SmoothSettings> GetSmoothing(this GeometryEngine.Core.Geometry.MeshMetadata metadataBase) {
        var settings = metadataBase.AsFabolus().Commands.OfType<SmoothSettings>().FirstOrDefault();
        return settings is null ? Maybe<SmoothSettings>.None() : Maybe<SmoothSettings>.Some(settings);
    }

    public static MeshMetadata WithSmoothing(this GeometryEngine.Core.Geometry.MeshMetadata metadataBase, SmoothSettings settings) =>
        metadataBase.AsFabolus().WithCommand(settings);
}
