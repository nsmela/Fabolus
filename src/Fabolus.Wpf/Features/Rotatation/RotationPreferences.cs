namespace Fabolus.Wpf.Features.Rotatation;

/// <summary>
/// User preferences for mesh rotation and overhang analysis.
/// </summary>
public sealed record RotationPreferences(
    float OverhangWarningAngle,
    float OverhangCriticalAngle
) : IPreferenceSettings
{
    public static readonly RotationPreferences Default = new(
        OverhangWarningAngle: 45.0f,
        OverhangCriticalAngle: 65.0f
    );

    public static class Ranges
    {
        public const float OverhangAngleMin = 10.0f;
        public const float OverhangAngleMax = 90.0f;
        public const float OverhangMinGap = 5.0f;
    }

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
