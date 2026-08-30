using CommunityToolkit.Mvvm.Messaging;

namespace Fabolus.Wpf.Features.AppPreferences;

/// <summary>
/// The one way to read and write a preference section over the messenger.
///
/// A <see cref="PreferenceSectionRequestMessage{T}"/> throws when nothing answered it, which
/// happens whenever the store is absent - the XAML designer, and any test that builds a view
/// model without one. Every caller used to wrap the send in its own try/catch and fall back to
/// the section default; that fallback lives here now so it reads the same way everywhere.
/// </summary>
public static class PreferenceMessengerExtensions {

    /// <summary>The stored section, or <paramref name="fallback"/> when no store is listening.</summary>
    public static T GetSection<T>(this IMessenger messenger, T fallback)
        where T : class, IPreferenceSettings {
        try {
            return messenger.Send(new PreferenceSectionRequestMessage<T>()).Response ?? fallback;
        }
        catch (InvalidOperationException) {
            // No store registered: nothing replied to the request.
            return fallback;
        }
    }

    /// <summary>Persists the section and tells everyone listening that it changed.</summary>
    public static void SaveSection<T>(this IMessenger messenger, T section)
        where T : class, IPreferenceSettings =>
        messenger.Send(new PreferenceSectionUpdateMessage<T>(section));
}
