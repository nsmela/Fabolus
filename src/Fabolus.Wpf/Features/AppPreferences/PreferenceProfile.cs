using System.IO;
using System.Text.Json;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Features.Moulds;
using Fabolus.Wpf.Features.Smoothing;

namespace Fabolus.Wpf.Features.AppPreferences;

/// <summary>
/// Every preference, as one value. Exists so restore-defaults, export and import all move the
/// same complete set of settings around instead of each maintaining its own list that can
/// silently fall behind when a preference is added.
/// </summary>
public sealed record PreferenceProfile {
    // Folders default to the same places AppPreferencesStore seeds a fresh section with.
    public static PreferenceProfile Defaults => new() {
        ImportFolder = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
        ExportFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    };

    public string ImportFolder { get; init; } = string.Empty;
    public string ExportFolder { get; init; } = string.Empty;
    public ExportFormat ExportFormat { get; init; } = ExportFormat.Stl;

    public float PrintBedWidth { get; init; } = 250.0f;
    public float PrintBedDepth { get; init; } = 250.0f;
    public bool ShowBedGrid { get; init; } = true;

    public bool AutodetectChannels { get; init; } = true;
    public float ChannelDiameter { get; init; } = 4.0f;

    public ViewportBackground ViewportBackground { get; init; } = ViewportBackground.Graphite;

    public bool SplitViewEnabled { get; init; } = false;
    public bool CutViewEnabled { get; init; } = false;
    /// <summary>Which meshes the cut view appears on. Base matches the rule this replaced.</summary>
    public CutViewScope CutScope { get; init; } = CutViewScope.Base;

    public MouldShapeType MouldShape { get; init; } = MouldShapeType.Concave;
    public float MouldWallThickness { get; init; } = 2.0f;
    public float MouldBaseHeight { get; init; } = 5.0f;
    /// <summary>0 leaves the top of the mould solid.</summary>
    public float MouldTroughHeight { get; init; } = 0.0f;
    public float MouldTroughOffset { get; init; } = 2.5f;
    public TroughShapeType MouldTroughShape { get; init; } = TroughShapeType.Footprint;

    public bool DecalsEnabled { get; init; } = true;
    public DecalAutoPlaceScope DecalScope { get; init; } = DecalAutoPlaceScope.Mould;
    public bool AutoPlaceFilename { get; init; } = true;
    public DecalAnchor FilenameAnchor { get; init; } = DecalAnchor.Front;
    public bool AutoPlaceVolume { get; init; } = true;
    public DecalAnchor VolumeAnchor { get; init; } = DecalAnchor.Back;
    public DecalFont DecalFont { get; init; } = DecalFont.Sans;
    public float DecalCapHeight { get; init; } = 6.0f;
    public float DecalDepth { get; init; } = 0.8f;
    public EmbossOperation DecalOperation { get; init; } = EmbossOperation.Engrave;

    // Smoothing. These are the values SmoothingViewModel was already initialising its sliders
    // with; until now ActivateAsync overwrote them with the SmoothSettings record's defaults
    // before they could ever be seen.
    public int SmoothIterations { get; init; } = 1;
    public float SmoothIntensity { get; init; } = 1.5f;
    public float SmoothInflation { get; init; } = 0.2f;
    public float SmoothRemeshRatio { get; init; } = 1.0f;
    public float SmoothResolution { get; init; } = 1.0f;
    public SmoothDisplayMode SmoothDisplay { get; init; } = SmoothDisplayMode.None;

    // Rotation: the overhang gradient's two thresholds, in degrees from the build plate.
    public float OverhangWarningAngle { get; init; } = 45.0f;
    public float OverhangCriticalAngle { get; init; } = 65.0f;
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

    /// <summary>
    /// Parses a profile from disk. Import replaces the whole set, so anything the file does not
    /// carry - or carries in a form that no longer reads - lands on the shipped default rather
    /// than on whatever happened to be set before. Every such case is named in Adjusted so the
    /// caller can say what did not survive instead of quietly changing settings.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a Fabolus preferences file.</exception>
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
                PrintBedWidth = ReadFloat(settings, UISettings.PrintBedWidthLabel, defaults.PrintBedWidth, "Print bed width", adjusted, PreferenceRanges.PrintBedMin, PreferenceRanges.PrintBedMax),
                PrintBedDepth = ReadFloat(settings, UISettings.PrintBedDepthLabel, defaults.PrintBedDepth, "Print bed depth", adjusted, PreferenceRanges.PrintBedMin, PreferenceRanges.PrintBedMax),
                ShowBedGrid = ReadBool(settings, UISettings.ShowBedGridLabel, defaults.ShowBedGrid, "Show bed grid", adjusted),
                AutodetectChannels = ReadBool(settings, UISettings.AutodetectChannelsLabel, defaults.AutodetectChannels, "Autodetect channels", adjusted),
                ChannelDiameter = ReadFloat(settings, UISettings.ChannelDiameterLabel, defaults.ChannelDiameter, "Channel diameter", adjusted, PreferenceRanges.ChannelDiameterMin, PreferenceRanges.ChannelDiameterMax),
                ViewportBackground = ReadEnum(settings, UISettings.ViewportBackgroundLabel, defaults.ViewportBackground, "Viewport background", adjusted),
                SplitViewEnabled = ReadBool(settings, UISettings.SplitViewEnabledLabel, defaults.SplitViewEnabled, "Split view", adjusted),
                CutViewEnabled = ReadBool(settings, UISettings.CutViewEnabledLabel, defaults.CutViewEnabled, "Cut view", adjusted),
                CutScope = ReadEnum(settings, UISettings.CutViewScopeLabel, defaults.CutScope, "Cut view scope", adjusted),
                MouldShape = ReadEnum(settings, UISettings.MouldShapeLabel, defaults.MouldShape, "Mould shape", adjusted),
                MouldWallThickness = ReadFloat(settings, UISettings.MouldWallThicknessLabel, defaults.MouldWallThickness, "Mould wall thickness", adjusted, PreferenceRanges.MouldWallThicknessMin, PreferenceRanges.MouldWallThicknessMax),
                MouldBaseHeight = ReadFloat(settings, UISettings.MouldBaseHeightLabel, defaults.MouldBaseHeight, "Mould base height", adjusted, PreferenceRanges.MouldBaseHeightMin, PreferenceRanges.MouldBaseHeightMax),
                MouldTroughHeight = ReadFloat(settings, UISettings.MouldTroughHeightLabel, defaults.MouldTroughHeight, "Mould trough depth", adjusted, PreferenceRanges.MouldTroughHeightMin, PreferenceRanges.MouldTroughHeightMax),
                MouldTroughOffset = ReadFloat(settings, UISettings.MouldTroughOffsetLabel, defaults.MouldTroughOffset, "Mould trough margin", adjusted, PreferenceRanges.MouldTroughOffsetMin, PreferenceRanges.MouldTroughOffsetMax),
                MouldTroughShape = ReadEnum(settings, UISettings.MouldTroughShapeLabel, defaults.MouldTroughShape, "Mould trough shape", adjusted),
                DecalsEnabled = ReadBool(settings, UISettings.DecalsEnabledLabel, defaults.DecalsEnabled, "Decal tool", adjusted),
                DecalScope = ReadEnum(settings, UISettings.DecalAutoPlaceScopeLabel, defaults.DecalScope, "Decal placement scope", adjusted),
                AutoPlaceFilename = ReadBool(settings, UISettings.DecalAutoPlaceFilenameLabel, defaults.AutoPlaceFilename, "Auto-place file name", adjusted),
                FilenameAnchor = ReadEnum(settings, UISettings.DecalFilenameAnchorLabel, defaults.FilenameAnchor, "File name anchor", adjusted),
                AutoPlaceVolume = ReadBool(settings, UISettings.DecalAutoPlaceVolumeLabel, defaults.AutoPlaceVolume, "Auto-place volume", adjusted),
                VolumeAnchor = ReadEnum(settings, UISettings.DecalVolumeAnchorLabel, defaults.VolumeAnchor, "Volume anchor", adjusted),
                DecalFont = ReadEnum(settings, UISettings.DecalDefaultFontLabel, defaults.DecalFont, "Decal font", adjusted),
                DecalCapHeight = ReadFloat(settings, UISettings.DecalDefaultCapHeightLabel, defaults.DecalCapHeight, "Decal cap height", adjusted, PreferenceRanges.DecalCapHeightMin, PreferenceRanges.DecalCapHeightMax),
                DecalDepth = ReadFloat(settings, UISettings.DecalDefaultDepthLabel, defaults.DecalDepth, "Decal depth", adjusted, PreferenceRanges.DecalDepthMin, PreferenceRanges.DecalDepthMax),
                DecalOperation = ReadEnum(settings, UISettings.DecalDefaultOperationLabel, defaults.DecalOperation, "Decal operation", adjusted),
                SmoothIterations = ReadInt(settings, UISettings.SmoothIterationsLabel, defaults.SmoothIterations, "Smoothing iterations", adjusted, PreferenceRanges.SmoothIterationsMin, PreferenceRanges.SmoothIterationsMax),
                SmoothIntensity = ReadFloat(settings, UISettings.SmoothIntensityLabel, defaults.SmoothIntensity, "Smoothing intensity", adjusted, PreferenceRanges.SmoothIntensityMin, PreferenceRanges.SmoothIntensityMax),
                SmoothInflation = ReadFloat(settings, UISettings.SmoothInflationLabel, defaults.SmoothInflation, "Smoothing inflation", adjusted, PreferenceRanges.SmoothInflationMin, PreferenceRanges.SmoothInflationMax),
                SmoothRemeshRatio = ReadFloat(settings, UISettings.SmoothRemeshRatioLabel, defaults.SmoothRemeshRatio, "Smoothing triangle ratio", adjusted, PreferenceRanges.SmoothRemeshRatioMin, PreferenceRanges.SmoothRemeshRatioMax),
                SmoothResolution = ReadFloat(settings, UISettings.SmoothResolutionLabel, defaults.SmoothResolution, "Smoothing smoothness", adjusted, PreferenceRanges.SmoothResolutionMin, PreferenceRanges.SmoothResolutionMax),
                SmoothDisplay = ReadEnum(settings, UISettings.SmoothDisplayModeLabel, defaults.SmoothDisplay, "Smoothing display mode", adjusted),
                OverhangWarningAngle = ReadFloat(settings, UISettings.OverhangWarningAngleLabel, defaults.OverhangWarningAngle, "Overhang warning angle", adjusted, PreferenceRanges.OverhangAngleMin, PreferenceRanges.OverhangAngleMax),
                OverhangCriticalAngle = ReadFloat(settings, UISettings.OverhangCriticalAngleLabel, defaults.OverhangCriticalAngle, "Overhang critical angle", adjusted, PreferenceRanges.OverhangAngleMin, PreferenceRanges.OverhangAngleMax),
            };

            // The two overhang thresholds are only meaningful as an ordered pair, and each was
            // read on its own above. A file that puts warning at or above critical describes a
            // gradient with no band between them - which the range slider cannot produce - so
            // both go back to their defaults rather than one being silently bent to fit.
            if (profile.OverhangWarningAngle + PreferenceRanges.OverhangMinGap > profile.OverhangCriticalAngle) {
                profile = profile with {
                    OverhangWarningAngle = defaults.OverhangWarningAngle,
                    OverhangCriticalAngle = defaults.OverhangCriticalAngle,
                };
                adjusted.RemoveAll(a => a.StartsWith("Overhang ", StringComparison.Ordinal));
                adjusted.Add("Overhang thresholds (warning was not at least "
                    + $"{PreferenceRanges.OverhangMinGap:0.##}\u00b0 below critical)");
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

    // Bounded by what the matching control can express (see PreferenceRanges), so a profile
    // cannot carry in a value the preferences window itself would refuse. NaN and infinity fail
    // the range test on their own.
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

    // A folder from another machine may simply not exist here. Falling back to the default
    // keeps the app pointing somewhere real instead of at a path that fails on first use.
    private static string ReadFolder(JsonElement settings, string key, string fallback, string label, List<string> adjusted) {
        if (!settings.TryGetProperty(key, out var value)) { adjusted.Add($"{label} (not in file)"); return fallback; }
        if (value.ValueKind != JsonValueKind.String) { adjusted.Add($"{label} (not a path)"); return fallback; }

        var path = value.GetString();
        if (string.IsNullOrWhiteSpace(path)) { adjusted.Add($"{label} (empty)"); return fallback; }

        try {
            if (!Directory.Exists(path)) { adjusted.Add($"{label} (no such folder on this machine)"); return fallback; }
        }
        catch (Exception) {
            // Malformed paths throw rather than returning false.
            adjusted.Add($"{label} (not a usable path on this machine)");
            return fallback;
        }

        return path;
    }
}
