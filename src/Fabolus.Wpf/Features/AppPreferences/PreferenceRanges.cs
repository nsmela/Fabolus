namespace Fabolus.Wpf.Features.AppPreferences;

/// <summary>
/// Accepted range for every numeric preference, in one place.
///
/// Both the live reader and the profile importer validate against these, so a config or a
/// profile file edited by hand cannot introduce a value the preferences window itself could
/// not produce. The smoothing bounds deliberately mirror the sliders in the smooth view -
/// a default the sliders cannot represent would snap the moment the user touched one.
/// </summary>
public static class PreferenceRanges {
    // Print bed - generous, since build volumes vary far more than the other settings.
    public const float PrintBedMin = 1.0f;
    public const float PrintBedMax = 10_000.0f;

    public const float ChannelDiameterMin = 0.1f;
    public const float ChannelDiameterMax = 100.0f;

    public const float DecalCapHeightMin = 0.5f;
    public const float DecalCapHeightMax = 500.0f;
    public const float DecalDepthMin = 0.1f;
    public const float DecalDepthMax = 100.0f;

    // Smoothing - these are the smooth view's own slider bounds.
    public const int SmoothIterationsMin = 1;
    public const int SmoothIterationsMax = 12;
    // Zero is meaningful for both of these: it means "no erosion" / "no inflation",
    // which is why they are lower-bounded at 0 rather than at some small positive value.
    public const float SmoothIntensityMin = 0.0f;
    public const float SmoothIntensityMax = 20.0f;
    public const float SmoothInflationMin = 0.0f;
    public const float SmoothInflationMax = 2.0f;
    public const float SmoothRemeshRatioMin = 0.5f;
    public const float SmoothRemeshRatioMax = 10.0f;
    public const float SmoothResolutionMin = 0.5f;
    public const float SmoothResolutionMax = 10.0f;

    // Overhang thresholds - the rotate view's own range slider bounds. MinGap mirrors that
    // slider's MinRange: the warning threshold always sits at least a degree below critical,
    // or the gradient between them has nowhere to go.
    public const float OverhangAngleMin = 40.0f;
    public const float OverhangAngleMax = 90.0f;
    public const float OverhangMinGap = 1.0f;

    // Mould - the mould control's own slider bounds. Trough depth starts at 0, which means
    // "leave the top of the mould solid" rather than "no trough of any size".
    public const float MouldWallThicknessMin = 1.0f;
    public const float MouldWallThicknessMax = 10.0f;
    public const float MouldBaseHeightMin = 0.0f;
    public const float MouldBaseHeightMax = 30.0f;
    public const float MouldTroughHeightMin = 0.0f;
    public const float MouldTroughHeightMax = 20.0f;
    public const float MouldTroughOffsetMin = 0.5f;
    public const float MouldTroughOffsetMax = 10.0f;
}
