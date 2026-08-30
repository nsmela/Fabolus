using Fabolus.Wpf.Features;
using Fabolus.Wpf.Features.AppPreferences;

namespace Fabolus.Wpf.Features.Rotatation;

/// <summary>
/// User preferences for mesh rotation and overhang analysis.
/// </summary>
public sealed record RotationPreferences(
    float OverhangWarningAngle,
    float OverhangCriticalAngle
) : IPreferenceSettings<RotationPreferences>
{
    public static string SectionKey => "rotation";

    public static RotationPreferences Default { get; } = new(
        OverhangWarningAngle: 45.0f,
        OverhangCriticalAngle: 65.0f
    );

    public static class Ranges
    {
        public const float OverhangAngleMin = 10.0f;
        public const float OverhangAngleMax = 90.0f;
        public const float OverhangMinGap = 5.0f;
    }

    public static class Keys
    {
        public const string WarningAngle = "overhang_warning_angle";
        public const string CriticalAngle = "overhang_critical_angle";
    }

    public static RotationPreferences Read(IPreferenceReader reader) => new(
        reader.GetFloat(Keys.WarningAngle, "Overhang warning angle", Default.OverhangWarningAngle,
            Ranges.OverhangAngleMin, Ranges.OverhangAngleMax),
        reader.GetFloat(Keys.CriticalAngle, "Overhang critical angle", Default.OverhangCriticalAngle,
            Ranges.OverhangAngleMin, Ranges.OverhangAngleMax)
    );

    public void Write(IPreferenceWriter writer)
    {
        writer.Set(Keys.WarningAngle, OverhangWarningAngle);
        writer.Set(Keys.CriticalAngle, OverhangCriticalAngle);
    }

    /// <summary>
    /// The two angles are only meaningful as a pair, so a warning that has not stayed clear of
    /// critical resets both rather than being nudged into a gap the user never chose.
    /// </summary>
    public RotationPreferences Clamped()
    {
        float warning = Math.Clamp(OverhangWarningAngle, Ranges.OverhangAngleMin, Ranges.OverhangAngleMax);
        float critical = Math.Clamp(OverhangCriticalAngle, Ranges.OverhangAngleMin, Ranges.OverhangAngleMax);

        if (warning + Ranges.OverhangMinGap > critical)
        {
            return Default;
        }

        return new RotationPreferences(warning, critical);
    }
}
