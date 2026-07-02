using Fabolus.Core.Common;
using Fabolus.Core.Geometry.Metadata;
using System.Linq;

namespace Fabolus.Core.Features.Smoothing;

public static class MeshSmoothingExtensions {
    public static Maybe<SmoothSettings> GetSmoothing(this MeshMetadata metadata) {
        var settings = metadata.Commands.OfType<SmoothSettings>().FirstOrDefault();
        return settings is null ? Maybe<SmoothSettings>.None() : Maybe<SmoothSettings>.Some(settings);
    }

    public static MeshMetadata WithSmoothing(this MeshMetadata metadata, SmoothSettings settings) =>
        metadata.WithCommand(settings);
}
