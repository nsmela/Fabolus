using Fabolus.Core.Features.Smoothing;

namespace Fabolus.Wpf.Features.Smoothing;

/// <summary>
/// User preferences for the mesh smoothing tool.
/// </summary>
public sealed record SmoothingPreferences(
    int Iterations,
    float Intensity,
    float Inflation,
    float RemeshRatio,
    float Resolution,
    SmoothDisplayMode DisplayMode
) : IPreferenceSettings
{
    public static readonly SmoothingPreferences Default = new(
        Iterations: 1,
        Intensity: 1.5f,
        Inflation: 0.2f,
        RemeshRatio: 1.0f,
        Resolution: 1.0f,
        DisplayMode: SmoothDisplayMode.None
    );

    public static class Ranges
    {
        public const int IterationsMin = 1;
        public const int IterationsMax = 5;
        public const float IntensityMin = 0.1f;
        public const float IntensityMax = 3.0f;
        public const float InflationMin = 0.0f;
        public const float InflationMax = 1.0f;
        public const float RemeshRatioMin = 0.2f;
        public const float RemeshRatioMax = 2.0f;
        public const float ResolutionMin = 0.25f;
        public const float ResolutionMax = 4.0f;
    }

    // Object initialiser rather than positional: SmoothSettings carries init properties with
    // defaults, so there is no positional constructor to call.
    public SmoothSettings ToSmoothSettings() => new() {
        Iterations = Iterations,
        Intensity = Intensity,
        Inflation = Inflation,
        RemeshRatio = RemeshRatio,
        Resolution = Resolution
    };

    public SmoothingPreferences Clamped() => new(
        Math.Clamp(Iterations, Ranges.IterationsMin, Ranges.IterationsMax),
        Math.Clamp(Intensity, Ranges.IntensityMin, Ranges.IntensityMax),
        Math.Clamp(Inflation, Ranges.InflationMin, Ranges.InflationMax),
        Math.Clamp(RemeshRatio, Ranges.RemeshRatioMin, Ranges.RemeshRatioMax),
        Math.Clamp(Resolution, Ranges.ResolutionMin, Ranges.ResolutionMax),
        Enum.IsDefined(DisplayMode) ? DisplayMode : Default.DisplayMode
    );
}
