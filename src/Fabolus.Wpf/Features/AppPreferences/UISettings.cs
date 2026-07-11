using System.Configuration;

namespace Fabolus.Wpf.Features.AppPreferences;
// ref: https://www.youtube.com/watch?v=GWixs4RN10w
public class UISettings : ConfigurationSection {

    public const string Label = "UISettings";

    public new object this[string propertyName] {
        get => base[propertyName];
        set => base[propertyName] = value;
    }

    public const string DefaultImportFolderLabel = "default_import_folder";
    public const string DefaultExportFolderLabel = "default_export_folder";
    public const string DefaultExportFormatLabel = "default_export_format";
    public const string PrintBedWidthLabel = "print_bed_width";
    public const string PrintBedDepthLabel = "print_bed_depth";
    public const string PrintBedHeightLabel = "print_bed_height";
    public const string ShowBedGridLabel = "show_bed_grid";
    public const string AutodetectChannelsLabel = "autodetect_channels";
    public const string ChannelDiameterLabel = "channel_diameter";
    public const string ViewportBackgroundLabel = "viewport_background";
    public const string UnitsLabel = "measurement_units";
    public const string SplitViewEnabledLabel = "split_view_enabled";
    public const string CutViewEnabledLabel = "cut_view_enabled";

    // ---- Folders -------------------------------------------------------

    [ConfigurationProperty(DefaultImportFolderLabel)]
    public string DefaultImportFolder {
        get => (string)this[DefaultImportFolderLabel];
        set => this[DefaultImportFolderLabel] = value;
    }

    [ConfigurationProperty(DefaultExportFolderLabel)]
    public string DefaultExportFolder {
        get => (string)this[DefaultExportFolderLabel];
        set => this[DefaultExportFolderLabel] = value;
    }

    [ConfigurationProperty(DefaultExportFormatLabel, DefaultValue = "Stl")]
    public string DefaultExportFormat {
        get => (string)this[DefaultExportFormatLabel];
        set => this[DefaultExportFormatLabel] = value;
    }

    // ---- Print bed -----------------------------------------------------

    [ConfigurationProperty(PrintBedWidthLabel, DefaultValue = 250.0f)]
    public float PrintBedWidth {
        get => (float)this[PrintBedWidthLabel];
        set => this[PrintBedWidthLabel] = value;
    }

    [ConfigurationProperty(PrintBedDepthLabel, DefaultValue = 250.0f)]
    public float PrintBedDepth {
        get => (float)this[PrintBedDepthLabel];
        set => this[PrintBedDepthLabel] = value;
    }

    [ConfigurationProperty(PrintBedHeightLabel, DefaultValue = 300.0f)]
    public float PrintBedHeight {
        get => (float)this[PrintBedHeightLabel];
        set => this[PrintBedHeightLabel] = value;
    }

    [ConfigurationProperty(ShowBedGridLabel, DefaultValue = true)]
    public bool ShowBedGrid {
        get => (bool)this[ShowBedGridLabel];
        set => this[ShowBedGridLabel] = value;
    }

    // ---- Air channels --------------------------------------------------

    [ConfigurationProperty(AutodetectChannelsLabel, DefaultValue = true)]
    public bool AutodetectChannels {
        get => (bool)this[AutodetectChannelsLabel];
        set => this[AutodetectChannelsLabel] = value;
    }

    [ConfigurationProperty(ChannelDiameterLabel, DefaultValue = 4.0f)]
    public float ChannelDiameter {
        get => (float)this[ChannelDiameterLabel];
        set => this[ChannelDiameterLabel] = value;
    }

    // ---- Appearance ----------------------------------------------------



    [ConfigurationProperty(ViewportBackgroundLabel, DefaultValue = "Graphite")]
    public string ViewportBackground {
        get => (string)this[ViewportBackgroundLabel];
        set => this[ViewportBackgroundLabel] = value;
    }

    [ConfigurationProperty(UnitsLabel, DefaultValue = "Millimeters")]
    public string Units {
        get => (string)this[UnitsLabel];
        set => this[UnitsLabel] = value;
    }

    // ---- Cut / Split --------------------------------------------------

    [ConfigurationProperty(SplitViewEnabledLabel, DefaultValue = false)]
    public bool SplitViewEnabled {
        get => (bool)this[SplitViewEnabledLabel];
        set => this[SplitViewEnabledLabel] = value;
    }

    [ConfigurationProperty(CutViewEnabledLabel, DefaultValue = false)]
    public bool CutViewEnabled {
        get => (bool)this[CutViewEnabledLabel];
        set => this[CutViewEnabledLabel] = value;
    }
}
