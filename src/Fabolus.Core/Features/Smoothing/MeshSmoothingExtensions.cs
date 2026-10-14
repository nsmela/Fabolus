using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.Smoothing;

public static class MeshSmoothingExtensions {
    /// <summary>The smoothing applied to this entry, or null if it has never been smoothed.</summary>
    public static SmoothSettings? Smoothing(this MeshRecord record) =>
        record.Command<SmoothSettings>();

    public static MeshRecord WithSmoothing(this MeshRecord record, SmoothSettings settings) =>
        record.WithCommand(settings);
}
