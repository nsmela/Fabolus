
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
) : IPreferenceSettings<PrintBedPreferences>
{
    public static string SectionKey => "printbed";

    public static PrintBedPreferences Default { get; } = new(
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

    public static class Keys
    {
        public const string Width = "print_bed_width";
        public const string Depth = "print_bed_depth";
        public const string ShowGrid = "show_bed_grid";
        public const string AutodetectChannels = "autodetect_channels";
        public const string ChannelDiameter = "channel_diameter";
    }

    public static PrintBedPreferences Read(IPreferenceReader reader) => new(
        reader.GetFloat(Keys.Width, "Print bed width", Default.Width, Ranges.PrintBedMin, Ranges.PrintBedMax),
        reader.GetFloat(Keys.Depth, "Print bed depth", Default.Depth, Ranges.PrintBedMin, Ranges.PrintBedMax),
        reader.GetBool(Keys.ShowGrid, "Show bed grid", Default.ShowGrid),
        reader.GetBool(Keys.AutodetectChannels, "Autodetect channels", Default.AutodetectChannels),
        reader.GetFloat(Keys.ChannelDiameter, "Channel diameter", Default.ChannelDiameter,
            Ranges.ChannelDiameterMin, Ranges.ChannelDiameterMax)
    );

    public void Write(IPreferenceWriter writer)
    {
        writer.Set(Keys.Width, Width);
        writer.Set(Keys.Depth, Depth);
        writer.Set(Keys.ShowGrid, ShowGrid);
        writer.Set(Keys.AutodetectChannels, AutodetectChannels);
        writer.Set(Keys.ChannelDiameter, ChannelDiameter);
    }

    public PrintBedPreferences Clamped() => new(
        Math.Clamp(Width, Ranges.PrintBedMin, Ranges.PrintBedMax),
        Math.Clamp(Depth, Ranges.PrintBedMin, Ranges.PrintBedMax),
        ShowGrid,
        AutodetectChannels,
        Math.Clamp(ChannelDiameter, Ranges.ChannelDiameterMin, Ranges.ChannelDiameterMax)
    );
}
