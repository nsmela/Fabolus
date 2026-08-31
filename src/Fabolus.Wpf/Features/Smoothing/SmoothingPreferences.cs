using Fabolus.Core.Features.Smoothing;
using Fabolus.Wpf.Features;
using Fabolus.Wpf.Features.AppPreferences;

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
) : IPreferenceSettings<SmoothingPreferences>
{
    public static string SectionKey => "smoothing";

    public static SmoothingPreferences Default { get; } = new(
        Iterations: 1,
        Intensity: 2.0f,
        Inflation: 0.1f,
        RemeshRatio: 2.0f,
        Resolution: 1.0f,
        DisplayMode: SmoothDisplayMode.None
    );

    public static class Ranges
    {
        public const int IterationsMin = 0;
        public const int IterationsMax = 10;
        public const float IntensityMin = 0.0f;
        public const float IntensityMax = 20.0f;
        public const float InflationMin = 0.0f;
        public const float InflationMax = 1.0f;
        public const float RemeshRatioMin = 1.0f;
        public const float RemeshRatioMax = 10.0f;
        public const float ResolutionMin = 0.5f;
        public const float ResolutionMax = 4.0f;
    }

    public static class Keys
    {
        public const string Iterations = "smooth_iterations";
        public const string Intensity = "smooth_intensity";
        public const string Inflation = "smooth_inflation";
        public const string RemeshRatio = "smooth_remesh_ratio";
        public const string Resolution = "smooth_resolution";
        public const string DisplayMode = "smooth_display_mode";
    }

    public static SmoothingPreferences Read(IPreferenceReader reader) => new(
        reader.GetInt(Keys.Iterations, "Smoothing iterations", Default.Iterations,
            Ranges.IterationsMin, Ranges.IterationsMax),
        reader.GetFloat(Keys.Intensity, "Smoothing intensity", Default.Intensity,
            Ranges.IntensityMin, Ranges.IntensityMax),
        reader.GetFloat(Keys.Inflation, "Smoothing inflation", Default.Inflation,
            Ranges.InflationMin, Ranges.InflationMax),
        reader.GetFloat(Keys.RemeshRatio, "Smoothing triangle ratio", Default.RemeshRatio,
            Ranges.RemeshRatioMin, Ranges.RemeshRatioMax),
        reader.GetFloat(Keys.Resolution, "Smoothing smoothness", Default.Resolution,
            Ranges.ResolutionMin, Ranges.ResolutionMax),
        reader.GetEnum(Keys.DisplayMode, "Smoothing display mode", Default.DisplayMode)
    );

    public void Write(IPreferenceWriter writer)
    {
        writer.Set(Keys.Iterations, Iterations);
        writer.Set(Keys.Intensity, Intensity);
        writer.Set(Keys.Inflation, Inflation);
        writer.Set(Keys.RemeshRatio, RemeshRatio);
        writer.Set(Keys.Resolution, Resolution);
        writer.SetEnum(Keys.DisplayMode, DisplayMode);
    }

    public SmoothSettings ToSmoothSettings() => new(
        Iterations, Intensity, Inflation, RemeshRatio, Resolution);

    public SmoothingPreferences Clamped() => new(
        Math.Clamp(Iterations, Ranges.IterationsMin, Ranges.IterationsMax),
        Math.Clamp(Intensity, Ranges.IntensityMin, Ranges.IntensityMax),
        Math.Clamp(Inflation, Ranges.InflationMin, Ranges.InflationMax),
        Math.Clamp(RemeshRatio, Ranges.RemeshRatioMin, Ranges.RemeshRatioMax),
        Math.Clamp(Resolution, Ranges.ResolutionMin, Ranges.ResolutionMax),
        Enum.IsDefined(DisplayMode) ? DisplayMode : Default.DisplayMode
    );
}
