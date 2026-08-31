namespace Fabolus.Wpf.Features.AppPreferences;

/// <summary>
/// Marker for a preference section, so the section messages can be constrained without
/// naming every section type.
/// </summary>
public interface IPreferenceSettings { }

/// <summary>
/// A preference section that knows how to store itself.
///
/// Each feature implements this on its own settings record, next to the feature it belongs to.
/// Everything else - loading, saving, restore-defaults, export, import - is written once against
/// this interface, so adding a preference means editing the record that owns it and nothing else.
/// </summary>
/// <typeparam name="TSelf">The implementing record, so Read and Default come back strongly typed.</typeparam>
public interface IPreferenceSettings<TSelf> : IPreferenceSettings
    where TSelf : class, IPreferenceSettings<TSelf> {

    /// <summary>Stable identifier for this section.</summary>
    static abstract string SectionKey { get; }

    /// <summary>The values a fresh install starts with, and the fallback for anything unreadable.</summary>
    static abstract TSelf Default { get; }

    /// <summary>Rebuilds the section from storage. Anything unusable falls back to the default.</summary>
    static abstract TSelf Read(IPreferenceReader reader);

    /// <summary>Writes every value in this section into storage.</summary>
    void Write(IPreferenceWriter writer);

    /// <summary>A copy with every value forced back into its supported range.</summary>
    TSelf Clamped();
}
