namespace Fabolus.Wpf.Features.AppPreferences;

/// <summary>
/// One page in the preferences sidebar, contributed by the feature that owns those settings.
///
/// Deliberately separate from <see cref="IPreferenceSettings{TSelf}"/>: how
/// preferences are stored and how they are presented are not the same grouping. The print bed
/// record backs both the Print Bed and Air Channels pages; the general record backs General and
/// Appearance; mould and trough share one page. A feature contributes as many sections as it
/// needs, over as many settings records.
/// </summary>
public interface IPreferenceSection {
    /// <summary>Stable identifier, used to remember which page is open.</summary>
    string Key { get; }

    /// <summary>Sidebar entry and panel title.</summary>
    string Name { get; }

    /// <summary>Panel subtitle.</summary>
    string Description { get; }

    /// <summary>Extra terms the search box matches on, beyond the name.</summary>
    string Keywords { get; }

    /// <summary>Resource key of the sidebar icon geometry.</summary>
    string IconKey { get; }

    /// <summary>
    /// Sort position. Application-wide pages take 0-99, features 100 and up, so a feature
    /// cannot push General and Appearance down the list.
    /// </summary>
    int Order { get; }

    /// <summary>The rows to show, bound against the preferences view model's properties.</summary>
    IReadOnlyList<PreferenceRow> BuildRows(PreferencesViewModel viewModel);
}

/// <summary>Search matching, shared by every section.</summary>
public static class PreferenceSectionExtensions {
    public static bool Matches(this IPreferenceSection section, string? search) {
        if (string.IsNullOrWhiteSpace(search)) { return true; }

        var term = search.Trim();
        return section.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || section.Keywords.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}
