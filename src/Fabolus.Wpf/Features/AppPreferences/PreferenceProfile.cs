using System.IO;
using System.Text.Json;

namespace Fabolus.Wpf.Features.AppPreferences;

/// <summary>What an import did, so the caller can tell the user rather than failing silently.</summary>
public sealed record PreferenceImportResult(
    PreferenceBag Bag,
    IReadOnlyList<string> Adjusted);

/// <summary>
/// Reads and writes a whole set of preferences as JSON.
///
/// The file carries the same keys the app stores, so an export stays readable by hand and lines
/// up with the running configuration. Nothing here lists the individual settings: the sections
/// write themselves in, and read themselves back out through a reader that reports whatever it
/// could not use.
/// </summary>
public static class PreferenceProfileIO {
    public const string FormatId = "fabolus-preferences";
    public const int FormatVersion = 1;
    public const string FileFilter = "Fabolus preferences (*.json)|*.json|All files (*.*)|*.*";
    public const string DefaultFileName = "fabolus-preferences.json";

    private const string FormatKey = "format";
    private const string VersionKey = "version";
    private const string SettingsKey = "settings";
    private const string AppVersionKey = "app_version";
    private const string ExportedKey = "exported_utc";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static void Write(string path, PreferenceBag settings) {
        var document = new Dictionary<string, object> {
            [FormatKey] = FormatId,
            [VersionKey] = FormatVersion,
            [AppVersionKey] = typeof(PreferenceProfileIO).Assembly.GetName().Version?.ToString() ?? "unknown",
            [ExportedKey] = DateTime.UtcNow.ToString("o"),
            [SettingsKey] = settings.Values,
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

            // Read every section out of the file, noting what it could not use, and write the
            // validated result into a fresh bag. Anything the file did not carry lands on its
            // default, which is what the caller reports.
            var fromFile = PreferenceBag.FromJsonObject(settings);
            var reader = new TrackingPreferenceReader(fromFile);

            var validated = PreferenceBag.FromDefaults();
            PreferenceSections.CopyValidated(reader, validated);

            return new PreferenceImportResult(validated, reader.Adjusted);
        }
    }
}
