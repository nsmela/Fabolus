using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Fabolus.Wpf.Features.AppPreferences;

/// <summary>
/// How a section reads itself back. Every call carries the four facts about a preference that
/// used to live in four different files: its storage key, the name to show a user when it has
/// to be rejected, its default, and the range it must fall in.
///
/// A value that is missing, the wrong shape, or out of range yields the fallback. It is not
/// clamped to the nearest bound: a file asking for a 1000mm channel is more likely to be wrong
/// than to mean 20mm, so the shipped default is the safer answer.
/// </summary>
public interface IPreferenceReader {
    string GetString(string key, string label, string fallback);
    string GetFolder(string key, string label, string fallback);
    bool GetBool(string key, string label, bool fallback);
    int GetInt(string key, string label, int fallback, int min, int max);
    float GetFloat(string key, string label, float fallback, float min, float max);
    TEnum GetEnum<TEnum>(string key, string label, TEnum fallback) where TEnum : struct, Enum;
}

/// <summary>How a section writes itself out.</summary>
public interface IPreferenceWriter {
    void Set(string key, string value);
    void Set(string key, bool value);
    void Set(string key, int value);
    void Set(string key, float value);
    void SetEnum<TEnum>(string key, TEnum value) where TEnum : struct, Enum;
}

/// <summary>
/// A flat bag of preference values, and the only thing that touches JSON.
///
/// Storage, export and import all move one of these around, so adding a preference means
/// teaching its own section to read and write itself - nothing here changes.
///
/// Keys it does not recognise are kept and written back out. A newer build's settings survive
/// a round trip through an older one instead of being silently dropped.
/// </summary>
public sealed class PreferenceBag : IPreferenceReader, IPreferenceWriter {
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly Dictionary<string, JsonElement> _values;

    public PreferenceBag() => _values = [];

    private PreferenceBag(Dictionary<string, JsonElement> values) => _values = values;

    public bool ContainsKey(string key) => _values.ContainsKey(key);

    /// <summary>The bag every section's defaults write themselves into.</summary>
    public static PreferenceBag FromDefaults() {
        var bag = new PreferenceBag();
        PreferenceSections.WriteDefaults(bag);
        return bag;
    }

    // ---- Writing -------------------------------------------------------

    public void Set(string key, string value) => _values[key] = JsonSerializer.SerializeToElement(value);
    public void Set(string key, bool value) => _values[key] = JsonSerializer.SerializeToElement(value);
    public void Set(string key, int value) => _values[key] = JsonSerializer.SerializeToElement(value);
    public void Set(string key, float value) => _values[key] = JsonSerializer.SerializeToElement(value);

    public void SetEnum<TEnum>(string key, TEnum value) where TEnum : struct, Enum =>
        _values[key] = JsonSerializer.SerializeToElement(value.ToString());

    /// <summary>Stores an already-parsed element, used when migrating an older config.</summary>
    public void SetRaw(string key, JsonElement value) => _values[key] = value;

    // ---- Reading -------------------------------------------------------
    // The bag itself reads silently. Import wraps it in a TrackingPreferenceReader to find
    // out which values it had to reject.

    public string GetString(string key, string label, string fallback) =>
        TryReadString(key, out var value) ? value : fallback;

    public string GetFolder(string key, string label, string fallback) =>
        TryReadFolder(key, out var value, out _) ? value : fallback;

    public bool GetBool(string key, string label, bool fallback) =>
        TryReadBool(key, out var value) ? value : fallback;

    public int GetInt(string key, string label, int fallback, int min, int max) =>
        TryReadInt(key, min, max, out var value) ? value : fallback;

    public float GetFloat(string key, string label, float fallback, float min, float max) =>
        TryReadFloat(key, min, max, out var value) ? value : fallback;

    public TEnum GetEnum<TEnum>(string key, string label, TEnum fallback) where TEnum : struct, Enum =>
        TryReadEnum<TEnum>(key, out var value) ? value : fallback;

    // ---- Parsing, shared with the tracking reader ----------------------

    internal bool TryReadString(string key, out string value) {
        value = string.Empty;
        if (!_values.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.String) { return false; }
        value = element.GetString() ?? string.Empty;
        return true;
    }

    /// <param name="reason">Why the folder was rejected, for the import report.</param>
    internal bool TryReadFolder(string key, out string value, out string reason) {
        value = string.Empty;
        reason = string.Empty;

        if (!_values.TryGetValue(key, out var element)) { reason = "not in file"; return false; }
        if (element.ValueKind != JsonValueKind.String) { reason = "not a path"; return false; }

        var path = element.GetString();
        if (string.IsNullOrWhiteSpace(path)) { reason = "empty"; return false; }

        try {
            if (!Directory.Exists(path)) { reason = "no such folder on this machine"; return false; }
        }
        catch (Exception) {
            reason = "not a usable path on this machine";
            return false;
        }

        value = path;
        return true;
    }

    internal bool TryReadBool(string key, out bool value) {
        value = false;
        if (!_values.TryGetValue(key, out var element)) { return false; }
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) { return false; }
        value = element.GetBoolean();
        return true;
    }

    internal bool TryReadInt(string key, int min, int max, out int value) {
        value = 0;
        if (!_values.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.Number) { return false; }
        if (!element.TryGetInt32(out int parsed) || parsed < min || parsed > max) { return false; }
        value = parsed;
        return true;
    }

    internal bool TryReadFloat(string key, float min, float max, out float value) {
        value = 0f;
        if (!_values.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.Number) { return false; }
        if (!element.TryGetDouble(out double parsed) || parsed < min || parsed > max) { return false; }
        value = (float)parsed;
        return true;
    }

    internal bool TryReadEnum<TEnum>(string key, out TEnum value) where TEnum : struct, Enum {
        value = default;
        if (!_values.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.String) { return false; }
        if (!Enum.TryParse(element.GetString(), ignoreCase: true, out TEnum parsed) || !Enum.IsDefined(parsed)) { return false; }
        value = parsed;
        return true;
    }

    // ---- JSON ----------------------------------------------------------

    public string ToJson() => JsonSerializer.Serialize(_values, WriteOptions);

    public IReadOnlyDictionary<string, JsonElement> Values => _values;

    public static PreferenceBag FromJsonObject(JsonElement settings) {
        var values = new Dictionary<string, JsonElement>();
        foreach (var property in settings.EnumerateObject()) {
            values[property.Name] = property.Value.Clone();
        }
        return new PreferenceBag(values);
    }

    /// <summary>Parses a loose "key = text" pairing, used when migrating the old exe config.</summary>
    public void SetFromText(string key, string text) {
        if (bool.TryParse(text, out bool flag)) { Set(key, flag); return; }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int whole)) {
            Set(key, whole);
            return;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) {
            _values[key] = JsonSerializer.SerializeToElement(number);
            return;
        }

        Set(key, text);
    }
}

/// <summary>
/// Reads through a bag and remembers everything it could not use, so an import can tell the
/// user which settings fell back to their default rather than reporting a clean run that
/// quietly changed things.
/// </summary>
public sealed class TrackingPreferenceReader : IPreferenceReader {
    private readonly PreferenceBag _bag;
    private readonly List<string> _adjusted = [];

    public TrackingPreferenceReader(PreferenceBag bag) => _bag = bag;

    public IReadOnlyList<string> Adjusted => _adjusted;

    private T Note<T>(string key, string label, string reason, T fallback) {
        _adjusted.Add(_bag.ContainsKey(key) ? $"{label} ({reason})" : $"{label} (not in file)");
        return fallback;
    }

    public string GetString(string key, string label, string fallback) =>
        _bag.TryReadString(key, out var value) ? value : Note(key, label, "not text", fallback);

    public string GetFolder(string key, string label, string fallback) {
        if (_bag.TryReadFolder(key, out var value, out var reason)) { return value; }
        _adjusted.Add($"{label} ({reason})");
        return fallback;
    }

    public bool GetBool(string key, string label, bool fallback) =>
        _bag.TryReadBool(key, out var value) ? value : Note(key, label, "not a true/false value", fallback);

    public int GetInt(string key, string label, int fallback, int min, int max) =>
        _bag.TryReadInt(key, min, max, out var value)
            ? value
            : Note(key, label, $"not a whole number between {min} and {max}", fallback);

    public float GetFloat(string key, string label, float fallback, float min, float max) =>
        _bag.TryReadFloat(key, min, max, out var value)
            ? value
            : Note(key, label, $"not a number between {min:0.##} and {max:0.##}", fallback);

    public TEnum GetEnum<TEnum>(string key, string label, TEnum fallback) where TEnum : struct, Enum =>
        _bag.TryReadEnum<TEnum>(key, out var value)
            ? value
            : Note(key, label, "not a value this build recognises", fallback);
}
