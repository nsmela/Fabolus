namespace Fabolus.Wpf.Features.AppPreferences;

/// <summary>
/// Preferences for print bed dimensions, grid, and air channel detection.
/// </summary>
public sealed record PrintBedPreferences(
    float Width,
    float Depth,
    bool ShowGrid,
    bool AutodetectChannels,
    float ChannelDiameter
) : IPreferenceSettings
{
    public static readonly PrintBedPreferences Default = new(
        Width: 250.0f,
        Depth: 250.0f,
        ShowGrid: true,
        AutodetectChannels: true,
        ChannelDiameter: 4.0f
    );

    public static class Ranges
    {
        public const float PrintBedMin = 50.0f;
        public const float PrintBedMax = 1000.0f;
        public const float ChannelDiameterMin = 1.0f;
        public const float ChannelDiameterMax = 20.0f;
    }

    public PrintBedPreferences Clamped() => new(
        Math.Clamp(Width, Ranges.PrintBedMin, Ranges.PrintBedMax),
        Math.Clamp(Depth, Ranges.PrintBedMin, Ranges.PrintBedMax),
        ShowGrid,
        AutodetectChannels,
        Math.Clamp(ChannelDiameter, Ranges.ChannelDiameterMin, Ranges.ChannelDiameterMax)
    );
}
