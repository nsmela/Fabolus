using System.IO;
using System.Text.Json;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Features.Moulds;
using Fabolus.Wpf.Features.CutSplit;
using Fabolus.Wpf.Features.Decal;
using Fabolus.Wpf.Features.Moulding;
using Fabolus.Wpf.Features.Rotatation;
using Fabolus.Wpf.Features.Smoothing;

namespace Fabolus.Wpf.Features.AppPreferences;

/// <summary>
/// Every preference, as one value. Exists so restore-defaults, export and import all move the
/// same complete set of settings around instead of each maintaining its own list that can
/// silently fall behind when a preference is added.
/// </summary>
public sealed record PreferenceProfile {
    public static PreferenceProfile Defaults {
        get {
            var general = GeneralPreferences.Default;
            var bed = PrintBedPreferences.Default;
            var cutSplit = CutSplitPreferences.Default;
            var mould = MouldPreferences.Default;
            var decal = DecalPreferences.Default;
            var smooth = SmoothingPreferences.Default;
            var rotate = RotationPreferences.Default;

            return new PreferenceProfile {
                ImportFolder = general.ImportFolder,
                ExportFolder = general.ExportFolder,
                ExportFormat = general.ExportFormat,
                PrintBedWidth = bed.Width,
                PrintBedDepth = bed.Depth,
                ShowBedGrid = bed.ShowGrid,
                AutodetectChannels = bed.AutodetectChannels,
                ChannelDiameter = bed.ChannelDiameter,
                ViewportBackground = general.ViewportBackground,
                SplitViewEnabled = cutSplit.SplitViewEnabled,
                CutViewEnabled = cutSplit.CutViewEnabled,
                CutScope = cutSplit.CutScope,
                MouldShape = mould.Shape,
                MouldWallThickness = mould.WallThickness,
                MouldBaseHeight = mould.BaseHeight,
                MouldTroughHeight = mould.TroughHeight,
                MouldTroughOffset = mould.TroughOffset,
                MouldTroughShape = mould.TroughShape,
                DecalsEnabled = decal.Enabled,
                DecalScope = decal.Scope,
                AutoPlaceFilename = decal.AutoPlaceFilename,
                FilenameAnchor = decal.FilenameAnchor,
                AutoPlaceVolume = decal.AutoPlaceVolume,
                VolumeAnchor = decal.VolumeAnchor,
                DecalFont = decal.Font,
                DecalCapHeight = decal.CapHeight,
                DecalDepth = decal.Depth,
                DecalOperation = decal.Operation,
                SmoothIterations = smooth.Iterations,
                SmoothIntensity = smooth.Intensity,
                SmoothInflation = smooth.Inflation,
                SmoothRemeshRatio = smooth.RemeshRatio,
                SmoothResolution = smooth.Resolution,
                SmoothDisplay = smooth.DisplayMode,
                OverhangWarningAngle = rotate.OverhangWarningAngle,
                OverhangCriticalAngle = rotate.OverhangCriticalAngle,
            };
        }
    }

    public string ImportFolder { get; init; } = GeneralPreferences.Default.ImportFolder;
    public string ExportFolder { get; init; } = GeneralPreferences.Default.ExportFolder;
    public ExportFormat ExportFormat { get; init; } = GeneralPreferences.Default.ExportFormat;

    public float PrintBedWidth { get; init; } = PrintBedPreferences.Default.Width;
    public float PrintBedDepth { get; init; } = PrintBedPreferences.Default.Depth;
    public bool ShowBedGrid { get; init; } = PrintBedPreferences.Default.ShowGrid;

    public bool AutodetectChannels { get; init; } = PrintBedPreferences.Default.AutodetectChannels;
    public float ChannelDiameter { get; init; } = PrintBedPreferences.Default.ChannelDiameter;

    public ViewportBackground ViewportBackground { get; init; } = GeneralPreferences.Default.ViewportBackground;

    public bool SplitViewEnabled { get; init; } = CutSplitPreferences.Default.SplitViewEnabled;
    public bool CutViewEnabled { get; init; } = CutSplitPreferences.Default.CutViewEnabled;
    public CutViewScope CutScope { get; init; } = CutSplitPreferences.Default.CutScope;

    public MouldShapeType MouldShape { get; init; } = MouldPreferences.Default.Shape;
    public float MouldWallThickness { get; init; } = MouldPreferences.Default.WallThickness;
    public float MouldBaseHeight { get; init; } = MouldPreferences.Default.BaseHeight;
    public float MouldTroughHeight { get; init; } = MouldPreferences.Default.TroughHeight;
    public float MouldTroughOffset { get; init; } = MouldPreferences.Default.TroughOffset;
    public TroughShapeType MouldTroughShape { get; init; } = MouldPreferences.Default.TroughShape;

    public bool DecalsEnabled { get; init; } = DecalPreferences.Default.Enabled;
    public DecalAutoPlaceScope DecalScope { get; init; } = DecalPreferences.Default.Scope;
    public bool AutoPlaceFilename { get; init; } = DecalPreferences.Default.AutoPlaceFilename;
    public DecalAnchor FilenameAnchor { get; init; } = DecalPreferences.Default.FilenameAnchor;
    public bool AutoPlaceVolume { get; init; } = DecalPreferences.Default.AutoPlaceVolume;
    public DecalAnchor VolumeAnchor { get; init; } = DecalPreferences.Default.VolumeAnchor;
    public DecalFont DecalFont { get; init; } = DecalPreferences.Default.Font;
    public float DecalCapHeight { get; init; } = DecalPreferences.Default.CapHeight;
    public float DecalDepth { get; init; } = DecalPreferences.Default.Depth;
    public EmbossOperation DecalOperation { get; init; } = DecalPreferences.Default.Operation;

    public int SmoothIterations { get; init; } = SmoothingPreferences.Default.Iterations;
    public float SmoothIntensity { get; init; } = SmoothingPreferences.Default.Intensity;
    public float SmoothInflation { get; init; } = SmoothingPreferences.Default.Inflation;
    public float SmoothRemeshRatio { get; init; } = SmoothingPreferences.Default.RemeshRatio;
    public float SmoothResolution { get; init; } = SmoothingPreferences.Default.Resolution;
    public SmoothDisplayMode SmoothDisplay { get; init; } = SmoothingPreferences.Default.DisplayMode;

    public float OverhangWarningAngle { get; init; } = RotationPreferences.Default.OverhangWarningAngle;
    public float OverhangCriticalAngle { get; init; } = RotationPreferences.Default.OverhangCriticalAngle;
}

/// <summary>What an import did, so the caller can tell the user rather than failing silently.</summary>
public sealed record PreferenceImportResult(
    PreferenceProfile Profile,
    IReadOnlyList<string> Adjusted);

/// <summary>
/// Reads and writes a preference profile as JSON. Keys are the UISettings storage labels so an
/// exported file lines up with the config the app actually saves, and stays readable by hand.
/// </summary>
public static class PreferenceProfileIO {
    public const string FormatId = "fabolus-preferences";
    public const int FormatVersion = 1;
    public const string FileFilter = "Fabolus preferences (*.json)|*.json|All files (*.*)|*.*";
    public const string DefaultFileName = "fabolus-preferences.json";

    private const string FormatKey = "format";
    private const string VersionKey = "version";
    private const string SettingsKey = "settings";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static void Write(string path, PreferenceProfile profile) {
        var settings = new Dictionary<string, object> {
            [UISettings.DefaultImportFolderLabel] = profile.ImportFolder,
            [UISettings.DefaultExportFolderLabel] = profile.ExportFolder,
            [UISettings.DefaultExportFormatLabel] = profile.ExportFormat.ToString(),
            [UISettings.PrintBedWidthLabel] = profile.PrintBedWidth,
            [UISettings.PrintBedDepthLabel] = profile.PrintBedDepth,
            [UISettings.ShowBedGridLabel] = profile.ShowBedGrid,
            [UISettings.AutodetectChannelsLabel] = profile.AutodetectChannels,
            [UISettings.ChannelDiameterLabel] = profile.ChannelDiameter,
            [UISettings.ViewportBackgroundLabel] = profile.ViewportBackground.ToString(),
            [UISettings.SplitViewEnabledLabel] = profile.SplitViewEnabled,
            [UISettings.CutViewEnabledLabel] = profile.CutViewEnabled,
            [UISettings.CutViewScopeLabel] = profile.CutScope.ToString(),
            [UISettings.MouldShapeLabel] = profile.MouldShape.ToString(),
            [UISettings.MouldWallThicknessLabel] = profile.MouldWallThickness,
            [UISettings.MouldBaseHeightLabel] = profile.MouldBaseHeight,
            [UISettings.MouldTroughHeightLabel] = profile.MouldTroughHeight,
            [UISettings.MouldTroughOffsetLabel] = profile.MouldTroughOffset,
            [UISettings.MouldTroughShapeLabel] = profile.MouldTroughShape.ToString(),
            [UISettings.DecalsEnabledLabel] = profile.DecalsEnabled,
            [UISettings.DecalAutoPlaceScopeLabel] = profile.DecalScope.ToString(),
            [UISettings.DecalAutoPlaceFilenameLabel] = profile.AutoPlaceFilename,
            [UISettings.DecalFilenameAnchorLabel] = profile.FilenameAnchor.ToString(),
            [UISettings.DecalAutoPlaceVolumeLabel] = profile.AutoPlaceVolume,
            [UISettings.DecalVolumeAnchorLabel] = profile.VolumeAnchor.ToString(),
            [UISettings.DecalDefaultFontLabel] = profile.DecalFont.ToString(),
            [UISettings.DecalDefaultCapHeightLabel] = profile.DecalCapHeight,
            [UISettings.DecalDefaultDepthLabel] = profile.DecalDepth,
            [UISettings.DecalDefaultOperationLabel] = profile.DecalOperation.ToString(),
            [UISettings.SmoothIterationsLabel] = profile.SmoothIterations,
            [UISettings.SmoothIntensityLabel] = profile.SmoothIntensity,
            [UISettings.SmoothInflationLabel] = profile.SmoothInflation,
            [UISettings.SmoothRemeshRatioLabel] = profile.SmoothRemeshRatio,
            [UISettings.SmoothResolutionLabel] = profile.SmoothResolution,
            [UISettings.SmoothDisplayModeLabel] = profile.SmoothDisplay.ToString(),
            [UISettings.OverhangWarningAngleLabel] = profile.OverhangWarningAngle,
            [UISettings.OverhangCriticalAngleLabel] = profile.OverhangCriticalAngle,
        };

        var document = new Dictionary<string, object> {
            [FormatKey] = FormatId,
            [VersionKey] = FormatVersion,
            ["app_version"] = typeof(PreferenceProfileIO).Assembly.GetName().Version?.ToString() ?? "unknown",
            ["exported_utc"] = DateTime.UtcNow.ToString("o"),
            [SettingsKey] = settings,
        };

        File.WriteAllText(path, JsonSerializer.Serialize(document, WriteOptions));
    }

    public static PreferenceImportResult Read(string path) {
        var text = File.ReadAllText(path);

        JsonDocument document;
        try { document = JsonDocument.Parse(text); }
        catch (JsonException e) { throw new InvalidDataException($"This file is not valid JSON. {e.Message}"); }

        using (document) {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
                throw new InvalidDataException("This file does not contain a preferences object.");
            }

            if (!root.TryGetProperty(FormatKey, out var format)
                || format.ValueKind != JsonValueKind.String
                || format.GetString() != FormatId) {
                throw new InvalidDataException("This is not a Fabolus preferences file.");
            }

            if (root.TryGetProperty(VersionKey, out var version)
                && version.TryGetInt32(out int fileVersion)
                && fileVersion > FormatVersion) {
                throw new InvalidDataException(
                    $"This file was written by a newer version of Fabolus (format {fileVersion}, this build reads {FormatVersion}).");
            }

            if (!root.TryGetProperty(SettingsKey, out var settings) || settings.ValueKind != JsonValueKind.Object) {
                throw new InvalidDataException("This preferences file has no settings in it.");
            }

            var adjusted = new List<string>();
            var defaults = PreferenceProfile.Defaults;

            var profile = new PreferenceProfile {
                ImportFolder = ReadFolder(settings, UISettings.DefaultImportFolderLabel, defaults.ImportFolder, "Import folder", adjusted),
                ExportFolder = ReadFolder(settings, UISettings.DefaultExportFolderLabel, defaults.ExportFolder, "Export folder", adjusted),
                ExportFormat = ReadEnum(settings, UISettings.DefaultExportFormatLabel, defaults.ExportFormat, "Export format", adjusted),
                PrintBedWidth = ReadFloat(settings, UISettings.PrintBedWidthLabel, defaults.PrintBedWidth, "Print bed width", adjusted, PrintBedPreferences.Ranges.PrintBedMin, PrintBedPreferences.Ranges.PrintBedMax),
                PrintBedDepth = ReadFloat(settings, UISettings.PrintBedDepthLabel, defaults.PrintBedDepth, "Print bed depth", adjusted, PrintBedPreferences.Ranges.PrintBedMin, PrintBedPreferences.Ranges.PrintBedMax),
                ShowBedGrid = ReadBool(settings, UISettings.ShowBedGridLabel, defaults.ShowBedGrid, "Show bed grid", adjusted),
                AutodetectChannels = ReadBool(settings, UISettings.AutodetectChannelsLabel, defaults.AutodetectChannels, "Autodetect channels", adjusted),
                ChannelDiameter = ReadFloat(settings, UISettings.ChannelDiameterLabel, defaults.ChannelDiameter, "Channel diameter", adjusted, PrintBedPreferences.Ranges.ChannelDiameterMin, PrintBedPreferences.Ranges.ChannelDiameterMax),
                ViewportBackground = ReadEnum(settings, UISettings.ViewportBackgroundLabel, defaults.ViewportBackground, "Viewport background", adjusted),
                SplitViewEnabled = ReadBool(settings, UISettings.SplitViewEnabledLabel, defaults.SplitViewEnabled, "Split view", adjusted),
                CutViewEnabled = ReadBool(settings, UISettings.CutViewEnabledLabel, defaults.CutViewEnabled, "Cut view", adjusted),
                CutScope = ReadEnum(settings, UISettings.CutViewScopeLabel, defaults.CutScope, "Cut view scope", adjusted),
                MouldShape = ReadEnum(settings, UISettings.MouldShapeLabel, defaults.MouldShape, "Mould shape", adjusted),
                MouldWallThickness = ReadFloat(settings, UISettings.MouldWallThicknessLabel, defaults.MouldWallThickness, "Mould wall thickness", adjusted, MouldPreferences.Ranges.WallThicknessMin, MouldPreferences.Ranges.WallThicknessMax),
                MouldBaseHeight = ReadFloat(settings, UISettings.MouldBaseHeightLabel, defaults.MouldBaseHeight, "Mould base height", adjusted, MouldPreferences.Ranges.BaseHeightMin, MouldPreferences.Ranges.BaseHeightMax),
                MouldTroughHeight = ReadFloat(settings, UISettings.MouldTroughHeightLabel, defaults.MouldTroughHeight, "Mould trough depth", adjusted, MouldPreferences.Ranges.TroughHeightMin, MouldPreferences.Ranges.TroughHeightMax),
                MouldTroughOffset = ReadFloat(settings, UISettings.MouldTroughOffsetLabel, defaults.MouldTroughOffset, "Mould trough margin", adjusted, MouldPreferences.Ranges.TroughOffsetMin, MouldPreferences.Ranges.TroughOffsetMax),
                MouldTroughShape = ReadEnum(settings, UISettings.MouldTroughShapeLabel, defaults.MouldTroughShape, "Mould trough shape", adjusted),
                DecalsEnabled = ReadBool(settings, UISettings.DecalsEnabledLabel, defaults.DecalsEnabled, "Decal tool", adjusted),
                DecalScope = ReadEnum(settings, UISettings.DecalAutoPlaceScopeLabel, defaults.DecalScope, "Decal placement scope", adjusted),
                AutoPlaceFilename = ReadBool(settings, UISettings.DecalAutoPlaceFilenameLabel, defaults.AutoPlaceFilename, "Auto-place file name", adjusted),
                FilenameAnchor = ReadEnum(settings, UISettings.DecalFilenameAnchorLabel, defaults.FilenameAnchor, "File name anchor", adjusted),
                AutoPlaceVolume = ReadBool(settings, UISettings.DecalAutoPlaceVolumeLabel, defaults.AutoPlaceVolume, "Auto-place volume", adjusted),
                VolumeAnchor = ReadEnum(settings, UISettings.DecalVolumeAnchorLabel, defaults.VolumeAnchor, "Volume anchor", adjusted),
                DecalFont = ReadEnum(settings, UISettings.DecalDefaultFontLabel, defaults.DecalFont, "Decal font", adjusted),
                DecalCapHeight = ReadFloat(settings, UISettings.DecalDefaultCapHeightLabel, defaults.DecalCapHeight, "Decal cap height", adjusted, DecalPreferences.Ranges.CapHeightMin, DecalPreferences.Ranges.CapHeightMax),
                DecalDepth = ReadFloat(settings, UISettings.DecalDefaultDepthLabel, defaults.DecalDepth, "Decal depth", adjusted, DecalPreferences.Ranges.DepthMin, DecalPreferences.Ranges.DepthMax),
                DecalOperation = ReadEnum(settings, UISettings.DecalDefaultOperationLabel, defaults.DecalOperation, "Decal operation", adjusted),
                SmoothIterations = ReadInt(settings, UISettings.SmoothIterationsLabel, defaults.SmoothIterations, "Smoothing iterations", adjusted, SmoothingPreferences.Ranges.IterationsMin, SmoothingPreferences.Ranges.IterationsMax),
                SmoothIntensity = ReadFloat(settings, UISettings.SmoothIntensityLabel, defaults.SmoothIntensity, "Smoothing intensity", adjusted, SmoothingPreferences.Ranges.IntensityMin, SmoothingPreferences.Ranges.IntensityMax),
                SmoothInflation = ReadFloat(settings, UISettings.SmoothInflationLabel, defaults.SmoothInflation, "Smoothing inflation", adjusted, SmoothingPreferences.Ranges.InflationMin, SmoothingPreferences.Ranges.InflationMax),
                SmoothRemeshRatio = ReadFloat(settings, UISettings.SmoothRemeshRatioLabel, defaults.SmoothRemeshRatio, "Smoothing triangle ratio", adjusted, SmoothingPreferences.Ranges.RemeshRatioMin, SmoothingPreferences.Ranges.RemeshRatioMax),
                SmoothResolution = ReadFloat(settings, UISettings.SmoothResolutionLabel, defaults.SmoothResolution, "Smoothing smoothness", adjusted, SmoothingPreferences.Ranges.ResolutionMin, SmoothingPreferences.Ranges.ResolutionMax),
                SmoothDisplay = ReadEnum(settings, UISettings.SmoothDisplayModeLabel, defaults.SmoothDisplay, "Smoothing display mode", adjusted),
                OverhangWarningAngle = ReadFloat(settings, UISettings.OverhangWarningAngleLabel, defaults.OverhangWarningAngle, "Overhang warning angle", adjusted, RotationPreferences.Ranges.OverhangAngleMin, RotationPreferences.Ranges.OverhangAngleMax),
                OverhangCriticalAngle = ReadFloat(settings, UISettings.OverhangCriticalAngleLabel, defaults.OverhangCriticalAngle, "Overhang critical angle", adjusted, RotationPreferences.Ranges.OverhangAngleMin, RotationPreferences.Ranges.OverhangAngleMax),
            };

            if (profile.OverhangWarningAngle + RotationPreferences.Ranges.OverhangMinGap > profile.OverhangCriticalAngle) {
                profile = profile with {
                    OverhangWarningAngle = defaults.OverhangWarningAngle,
                    OverhangCriticalAngle = defaults.OverhangCriticalAngle,
                };
                adjusted.RemoveAll(a => a.StartsWith("Overhang ", StringComparison.Ordinal));
                adjusted.Add("Overhang thresholds (warning was not at least "
                    + $"{RotationPreferences.Ranges.OverhangMinGap:0.##}\u00b0 below critical)");
            }

            return new PreferenceImportResult(profile, adjusted);
        }
    }

    private static bool ReadBool(JsonElement settings, string key, bool fallback, string label, List<string> adjusted) {
        if (!settings.TryGetProperty(key, out var value)) { adjusted.Add($"{label} (not in file)"); return fallback; }
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) { return value.GetBoolean(); }
        adjusted.Add($"{label} (not a true/false value)");
        return fallback;
    }

    private static float ReadFloat(JsonElement settings, string key, float fallback, string label,
                                   List<string> adjusted, float min, float max) {
        if (!settings.TryGetProperty(key, out var value)) { adjusted.Add($"{label} (not in file)"); return fallback; }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double parsed)
            && parsed >= min && parsed <= max) {
            return (float)parsed;
        }
        adjusted.Add($"{label} (not a number between {min:0.##} and {max:0.##})");
        return fallback;
    }

    private static int ReadInt(JsonElement settings, string key, int fallback, string label,
                               List<string> adjusted, int min, int max) {
        if (!settings.TryGetProperty(key, out var value)) { adjusted.Add($"{label} (not in file)"); return fallback; }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int parsed)
            && parsed >= min && parsed <= max) {
            return parsed;
        }
        adjusted.Add($"{label} (not a whole number between {min} and {max})");
        return fallback;
    }

    private static TEnum ReadEnum<TEnum>(JsonElement settings, string key, TEnum fallback, string label, List<string> adjusted)
        where TEnum : struct, Enum {
        if (!settings.TryGetProperty(key, out var value)) { adjusted.Add($"{label} (not in file)"); return fallback; }
        if (value.ValueKind == JsonValueKind.String
            && Enum.TryParse<TEnum>(value.GetString(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)) {
            return parsed;
        }
        adjusted.Add($"{label} (not a value this build recognises)");
        return fallback;
    }

    private static string ReadFolder(JsonElement settings, string key, string fallback, string label, List<string> adjusted) {
        if (!settings.TryGetProperty(key, out var value)) { adjusted.Add($"{label} (not in file)"); return fallback; }
        if (value.ValueKind != JsonValueKind.String) { adjusted.Add($"{label} (not a path)"); return fallback; }

        var path = value.GetString();
        if (string.IsNullOrWhiteSpace(path)) { adjusted.Add($"{label} (empty)"); return fallback; }

        try {
            if (!Directory.Exists(path)) { adjusted.Add($"{label} (no such folder on this machine)"); return fallback; }
        }
        catch (Exception) {
            adjusted.Add($"{label} (not a usable path on this machine)");
            return fallback;
        }

        return path;
    }
}
