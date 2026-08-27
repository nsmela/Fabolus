using CommunityToolkit.Mvvm.Messaging;

namespace Fabolus.Wpf.Features.AppPreferences;

/// <summary>
/// Reads single preferences through the messenger on behalf of a feature view model.
///
/// Every read falls back rather than throwing. Two things can go wrong and neither should take
/// a view down with it: the store may not be listening at all (design-time view models construct
/// without one), and the saved config is a plain file a user can hand-edit into any shape.
/// </summary>
public static class AppPreferenceReader {
    public static object? Raw(IMessenger messenger, string key) {
        try { return messenger.Send(new AppPreferenceRequestMessage(key)).Response; }
        catch { return null; }
    }

    public static bool Bool(IMessenger messenger, string key, bool fallback)
        => Raw(messenger, key) is bool value ? value : fallback;

    /// <summary>
    /// A number within <paramref name="min"/>..<paramref name="max"/> inclusive. The bounds are
    /// the ones the matching control offers, so a config edited past what the UI can express
    /// cannot push a value the app would not otherwise accept.
    /// </summary>
    public static float Float(IMessenger messenger, string key, float fallback, float min, float max) {
        var raw = Raw(messenger, key);
        float? value = raw switch {
            float f => f,
            double d => (float)d,
            int i => i,
            _ => null
        };
        return value is float v && float.IsFinite(v) && v >= min && v <= max ? v : fallback;
    }

    public static int Int(IMessenger messenger, string key, int fallback, int min, int max) {
        var raw = Raw(messenger, key);
        int? value = raw switch {
            int i => i,
            long l when l >= int.MinValue && l <= int.MaxValue => (int)l,
            _ => null
        };
        return value is int v && v >= min && v <= max ? v : fallback;
    }

    /// <summary>
    /// Enums are stored by name. Enum.TryParse also accepts a numeric string and will happily
    /// produce an undefined value from it, so the result is checked against the defined members.
    /// </summary>
    public static TEnum Enum<TEnum>(IMessenger messenger, string key, TEnum fallback) where TEnum : struct, System.Enum {
        var raw = Raw(messenger, key);
        if (raw is TEnum typed) { return typed; }
        return raw is string text
            && System.Enum.TryParse<TEnum>(text, ignoreCase: true, out var parsed)
            && System.Enum.IsDefined(parsed)
            ? parsed
            : fallback;
    }
}
