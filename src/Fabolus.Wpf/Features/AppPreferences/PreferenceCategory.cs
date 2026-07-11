using System.Windows.Media;

namespace Fabolus.Wpf.Pages.Preferences;

/// <summary>A row in the preferences sidebar.</summary>
public sealed class PreferenceCategory {
    /// <summary>Stable key used to switch the content pane (e.g. "general").</summary>
    public required string Key { get; init; }

    /// <summary>Display name shown in the sidebar and matched against the search box.</summary>
    public required string Name { get; init; }

    /// <summary>Extra keywords (not shown) that the search box also matches against.</summary>
    public string Keywords { get; init; } = string.Empty;

    /// <summary>16x16 stroke geometry for the row icon.</summary>
    public required System.Windows.Media.Geometry Icon { get; init; }

    /// <summary>True when the row should be visible for the given search text.</summary>
    public bool Matches(string? search) {
        if (string.IsNullOrWhiteSpace(search)) { return true; }
        var s = search.Trim();
        return Name.Contains(s, System.StringComparison.OrdinalIgnoreCase)
            || Keywords.Contains(s, System.StringComparison.OrdinalIgnoreCase);
    }
}
