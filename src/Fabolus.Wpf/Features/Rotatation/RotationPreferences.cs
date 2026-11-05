using Fabolus.Wpf.Features.AppPreferences;

namespace Fabolus.Wpf.Features.Rotatation;

/// <summary>
/// User preferences for mesh rotation and overhang analysis.
/// </summary>
public sealed record RotationPreferences(
    float OverhangWarningAngle,
    float OverhangCriticalAngle,
    float MeshOpacity
) : IPreferenceSettings<RotationPreferences>
{
    public static RotationPreferences Default { get; } = new(
        OverhangWarningAngle: 45.0f,
        OverhangCriticalAngle: 65.0f,
        MeshOpacity: 100.0f
    );

    public static class Ranges
    {
        public const float OverhangAngleMin = 30.0f;
        public const float OverhangAngleMax = 90.0f;
        public const float OverhangMinGap = 5.0f;

        // In percent. The floor is well above zero because a mesh faint enough to lose has stopped
        // being something to align, and its overhang colours have gone with it.
        public const float MeshOpacityMin = 20.0f;
        public const float MeshOpacityMax = 100.0f;
    }

    public static class Keys
    {
        public const string WarningAngle = "overhang_warning_angle";
        public const string CriticalAngle = "overhang_critical_angle";
        public const string MeshOpacity = "mesh_opacity";
    }

    public static RotationPreferences Read(IPreferenceReader reader) => new(
        reader.GetFloat(Keys.WarningAngle, "Overhang warning angle", Default.OverhangWarningAngle,
            Ranges.OverhangAngleMin, Ranges.OverhangAngleMax),
        reader.GetFloat(Keys.CriticalAngle, "Overhang critical angle", Default.OverhangCriticalAngle,
            Ranges.OverhangAngleMin, Ranges.OverhangAngleMax),
        reader.GetFloat(Keys.MeshOpacity, "Rotation mesh opacity", Default.MeshOpacity,
            Ranges.MeshOpacityMin, Ranges.MeshOpacityMax)
    );

    public void Write(IPreferenceWriter writer)
    {
        writer.Set(Keys.WarningAngle, OverhangWarningAngle);
        writer.Set(Keys.CriticalAngle, OverhangCriticalAngle);
        writer.Set(Keys.MeshOpacity, MeshOpacity);
    }

    /// <summary>
    /// The two angles are only meaningful as a pair, so a warning that has not stayed clear of
    /// critical resets both rather than being nudged into a gap the user never chose. The opacity
    /// has nothing to do with that pair, so it is clamped on its own and survives the reset.
    /// </summary>
    public RotationPreferences Clamped()
    {
        float warning = Math.Clamp(OverhangWarningAngle, Ranges.OverhangAngleMin, Ranges.OverhangAngleMax);
        float critical = Math.Clamp(OverhangCriticalAngle, Ranges.OverhangAngleMin, Ranges.OverhangAngleMax);

        float opacity = Math.Clamp(MeshOpacity, Ranges.MeshOpacityMin, Ranges.MeshOpacityMax);

        if (warning + Ranges.OverhangMinGap > critical)
        {
            return Default with { MeshOpacity = opacity };
        }

        return new RotationPreferences(warning, critical, opacity);
    }
}
