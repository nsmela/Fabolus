using Fabolus.Wpf.Features.CutSplit;
using Fabolus.Wpf.Features.Decal;
using Fabolus.Wpf.Features.Moulding;
using Fabolus.Wpf.Features.Rotatation;
using Fabolus.Wpf.Features.Smoothing;

namespace Fabolus.Wpf.Features.AppPreferences;

/// <summary>
/// The preference pages, in the order they appear.
///
/// The one place that names them; each page itself lives with the feature it configures. A new
/// page is a class in that feature's folder plus a line here - the view model and the view do
/// not change.
/// </summary>
public static class PreferenceSectionCatalog {

    /// <summary>Sorted by <see cref="IPreferenceSection.Order"/>, then name.</summary>
    public static IReadOnlyList<IPreferenceSection> Default { get; } = Sort([
        new GeneralPreferenceSection(),
        new PrintBedPreferenceSection(),
        new RotationPreferenceSection(),
        new SmoothingPreferenceSection(),
        new CutPreferenceSection(),
        new SplitPreferenceSection(),
        new AirChannelPreferenceSection(),
        new MouldPreferenceSection(),
        new DecalPreferenceSection(),
        new AppearancePreferenceSection(),
    ]);

    public static IReadOnlyList<IPreferenceSection> Sort(IEnumerable<IPreferenceSection> sections) =>
        [.. sections.OrderBy(s => s.Order).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)];
}
