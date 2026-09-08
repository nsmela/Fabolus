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
public static class PreferencePageCatalog {

    /// <summary>Sorted by <see cref="IPreferencePage.Order"/>, then name.</summary>
    public static IReadOnlyList<IPreferencePage> Default { get; } = Sort([
        new GeneralPreferencePage(),
        new PrintBedPreferencePage(),
        new RotationPreferencePage(),
        new SmoothingPreferencePage(),
        new CutPreferencePage(),
        new SplitPreferencePage(),
        new AirChannelPreferencePage(),
        new MouldPreferencePage(),
        new DecalPreferencePage(),
    ]);

    public static IReadOnlyList<IPreferencePage> Sort(IEnumerable<IPreferencePage> sections) =>
        [.. sections.OrderBy(s => s.Order).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)];
}
