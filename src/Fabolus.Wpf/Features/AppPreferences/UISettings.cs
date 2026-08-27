using System.Configuration;

namespace Fabolus.Wpf.Features.AppPreferences;
// ref: https://www.youtube.com/watch?v=GWixs4RN10w
public class UISettings : ConfigurationSection {

    public const string Label = "UISettings";

    public new object this[string propertyName] {
        get => base[propertyName];
        set => base[propertyName] = value;
    }

    // Preferences saved by an older build can contain attributes that no longer exist.
    // Drop them instead of throwing, so a removed setting can't brick startup.
    protected override bool OnDeserializeUnrecognizedAttribute(string name, string value) => true;

    public const string DefaultImportFolderLabel = "default_import_folder";
    public const string DefaultExportFolderLabel = "default_export_folder";
    public const string DefaultExportFormatLabel = "default_export_format";
    public const string PrintBedWidthLabel = "print_bed_width";
    public const string PrintBedDepthLabel = "print_bed_depth";
    public const string ShowBedGridLabel = "show_bed_grid";
    public const string AutodetectChannelsLabel = "autodetect_channels";
    public const string ChannelDiameterLabel = "channel_diameter";
    public const string ViewportBackgroundLabel = "viewport_background";
    public const string AppThemeLabel = "app_theme";
    public const string SplitViewEnabledLabel = "split_view_enabled";
    public const string CutViewEnabledLabel = "cut_view_enabled";
    public const string DecalsEnabledLabel = "decals_enabled";
    public const string DecalAutoPlaceScopeLabel = "decal_autoplace_scope";
    public const string DecalAutoPlaceFilenameLabel = "decal_autoplace_filename";
    public const string DecalFilenameAnchorLabel = "decal_filename_anchor";
    public const string DecalAutoPlaceVolumeLabel = "decal_autoplace_volume";
    public const string DecalVolumeAnchorLabel = "decal_volume_anchor";
    public const string DecalDefaultFontLabel = "decal_default_font";
    public const string DecalDefaultCapHeightLabel = "decal_default_cap_height";
    public const string DecalDefaultDepthLabel = "decal_default_depth";
    public const string DecalDefaultOperationLabel = "decal_default_operation";
    public const string SmoothIterationsLabel = "smooth_iterations";
    public const string SmoothIntensityLabel = "smooth_intensity";
    public const string SmoothInflationLabel = "smooth_inflation";
    public const string SmoothRemeshRatioLabel = "smooth_remesh_ratio";
    public const string SmoothResolutionLabel = "smooth_resolution";
    public const string SmoothDisplayModeLabel = "smooth_display_mode";
    public const string OverhangWarningAngleLabel = "overhang_warning_angle";
    public const string OverhangCriticalAngleLabel = "overhang_critical_angle";
    public const string CutViewScopeLabel = "cut_view_scope";
    public const string MouldShapeLabel = "mould_shape";
    public const string MouldWallThicknessLabel = "mould_wall_thickness";
    public const string MouldBaseHeightLabel = "mould_base_height";
    public const string MouldTroughHeightLabel = "mould_trough_height";
    public const string MouldTroughOffsetLabel = "mould_trough_offset";
    public const string MouldTroughShapeLabel = "mould_trough_shape";

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

    [ConfigurationProperty(AppThemeLabel, DefaultValue = "Dark")]
    public string AppTheme {
        get => (string)this[AppThemeLabel];
        set => this[AppThemeLabel] = value;
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

    // ---- Decals --------------------------------------------------------

    [ConfigurationProperty(DecalsEnabledLabel, DefaultValue = true)]
    public bool DecalsEnabled {
        get => (bool)this[DecalsEnabledLabel];
        set => this[DecalsEnabledLabel] = value;
    }

    [ConfigurationProperty(DecalAutoPlaceScopeLabel, DefaultValue = "Mould")]
    public string DecalAutoPlaceScope {
        get => (string)this[DecalAutoPlaceScopeLabel];
        set => this[DecalAutoPlaceScopeLabel] = value;
    }

    [ConfigurationProperty(DecalAutoPlaceFilenameLabel, DefaultValue = true)]
    public bool DecalAutoPlaceFilename {
        get => (bool)this[DecalAutoPlaceFilenameLabel];
        set => this[DecalAutoPlaceFilenameLabel] = value;
    }

    [ConfigurationProperty(DecalFilenameAnchorLabel, DefaultValue = "Front")]
    public string DecalFilenameAnchor {
        get => (string)this[DecalFilenameAnchorLabel];
        set => this[DecalFilenameAnchorLabel] = value;
    }

    [ConfigurationProperty(DecalAutoPlaceVolumeLabel, DefaultValue = true)]
    public bool DecalAutoPlaceVolume {
        get => (bool)this[DecalAutoPlaceVolumeLabel];
        set => this[DecalAutoPlaceVolumeLabel] = value;
    }

    [ConfigurationProperty(DecalVolumeAnchorLabel, DefaultValue = "Back")]
    public string DecalVolumeAnchor {
        get => (string)this[DecalVolumeAnchorLabel];
        set => this[DecalVolumeAnchorLabel] = value;
    }

    [ConfigurationProperty(DecalDefaultFontLabel, DefaultValue = "Sans")]
    public string DecalDefaultFont {
        get => (string)this[DecalDefaultFontLabel];
        set => this[DecalDefaultFontLabel] = value;
    }

    [ConfigurationProperty(DecalDefaultCapHeightLabel, DefaultValue = 6.0f)]
    public float DecalDefaultCapHeight {
        get => (float)this[DecalDefaultCapHeightLabel];
        set => this[DecalDefaultCapHeightLabel] = value;
    }

    [ConfigurationProperty(DecalDefaultDepthLabel, DefaultValue = 0.8f)]
    public float DecalDefaultDepth {
        get => (float)this[DecalDefaultDepthLabel];
        set => this[DecalDefaultDepthLabel] = value;
    }

    [ConfigurationProperty(DecalDefaultOperationLabel, DefaultValue = "Engrave")]
    public string DecalDefaultOperation {
        get => (string)this[DecalDefaultOperationLabel];
        set => this[DecalDefaultOperationLabel] = value;
    }

    // ---- Smoothing -----------------------------------------------------

    [ConfigurationProperty(SmoothIterationsLabel, DefaultValue = 1)]
    public int SmoothIterations {
        get => (int)this[SmoothIterationsLabel];
        set => this[SmoothIterationsLabel] = value;
    }

    [ConfigurationProperty(SmoothIntensityLabel, DefaultValue = 1.5f)]
    public float SmoothIntensity {
        get => (float)this[SmoothIntensityLabel];
        set => this[SmoothIntensityLabel] = value;
    }

    [ConfigurationProperty(SmoothInflationLabel, DefaultValue = 0.2f)]
    public float SmoothInflation {
        get => (float)this[SmoothInflationLabel];
        set => this[SmoothInflationLabel] = value;
    }

    [ConfigurationProperty(SmoothRemeshRatioLabel, DefaultValue = 1.0f)]
    public float SmoothRemeshRatio {
        get => (float)this[SmoothRemeshRatioLabel];
        set => this[SmoothRemeshRatioLabel] = value;
    }

    [ConfigurationProperty(SmoothResolutionLabel, DefaultValue = 1.0f)]
    public float SmoothResolution {
        get => (float)this[SmoothResolutionLabel];
        set => this[SmoothResolutionLabel] = value;
    }

    [ConfigurationProperty(SmoothDisplayModeLabel, DefaultValue = "None")]
    public string SmoothDisplayMode {
        get => (string)this[SmoothDisplayModeLabel];
        set => this[SmoothDisplayModeLabel] = value;
    }

    // ---- Rotation ------------------------------------------------------

    [ConfigurationProperty(OverhangWarningAngleLabel, DefaultValue = 45.0f)]
    public float OverhangWarningAngle {
        get => (float)this[OverhangWarningAngleLabel];
        set => this[OverhangWarningAngleLabel] = value;
    }

    [ConfigurationProperty(OverhangCriticalAngleLabel, DefaultValue = 65.0f)]
    public float OverhangCriticalAngle {
        get => (float)this[OverhangCriticalAngleLabel];
        set => this[OverhangCriticalAngleLabel] = value;
    }

    // ---- Cut ----------------------------------------------------------

    [ConfigurationProperty(CutViewScopeLabel, DefaultValue = "Base")]
    public string CutViewScope {
        get => (string)this[CutViewScopeLabel];
        set => this[CutViewScopeLabel] = value;
    }

    // ---- Mould ---------------------------------------------------------

    [ConfigurationProperty(MouldShapeLabel, DefaultValue = "Concave")]
    public string MouldShape {
        get => (string)this[MouldShapeLabel];
        set => this[MouldShapeLabel] = value;
    }

    [ConfigurationProperty(MouldWallThicknessLabel, DefaultValue = 2.0f)]
    public float MouldWallThickness {
        get => (float)this[MouldWallThicknessLabel];
        set => this[MouldWallThicknessLabel] = value;
    }

    [ConfigurationProperty(MouldBaseHeightLabel, DefaultValue = 5.0f)]
    public float MouldBaseHeight {
        get => (float)this[MouldBaseHeightLabel];
        set => this[MouldBaseHeightLabel] = value;
    }

    [ConfigurationProperty(MouldTroughHeightLabel, DefaultValue = 0.0f)]
    public float MouldTroughHeight {
        get => (float)this[MouldTroughHeightLabel];
        set => this[MouldTroughHeightLabel] = value;
    }

    [ConfigurationProperty(MouldTroughOffsetLabel, DefaultValue = 2.5f)]
    public float MouldTroughOffset {
        get => (float)this[MouldTroughOffsetLabel];
        set => this[MouldTroughOffsetLabel] = value;
    }

    [ConfigurationProperty(MouldTroughShapeLabel, DefaultValue = "Footprint")]
    public string MouldTroughShape {
        get => (string)this[MouldTroughShapeLabel];
        set => this[MouldTroughShapeLabel] = value;
    }
}
