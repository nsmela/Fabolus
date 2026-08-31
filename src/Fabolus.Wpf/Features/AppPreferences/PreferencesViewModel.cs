using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.Rotatation;
using Microsoft.Win32;

namespace Fabolus.Wpf.Features.AppPreferences;

public partial class PreferencesViewModel : ObservableObject
{
    private readonly IMessenger _messenger;
    private readonly IAlertDialog _alert;

    // ---- Navigation ----------------------------------------------------
    [ObservableProperty] private string _searchText = string.Empty;

    private readonly IReadOnlyList<IPreferencePage> _pages;
    private readonly Dictionary<string, IReadOnlyList<PreferenceRow>> _rowsByPage = [];

    /// <summary>The sidebar, filtered by the search box.</summary>
    public ObservableCollection<IPreferencePage> Pages { get; } = [];

    [ObservableProperty] private IPreferencePage? _selectedPage;

    /// <summary>Rows of the open page. Built once per page and reused.</summary>
    public IReadOnlyList<PreferenceRow> Rows =>
        SelectedPage is null ? [] : _rowsByPage[SelectedPage.Key];

    // ---- The settings themselves ---------------------------------------

    /// <summary>
    /// One entry per settings record, the validated value as it stands now.
    ///
    /// The view model used to mirror all thirty-nine preferences as loose properties: the
    /// constructor shredded seven records into them, seven Capture methods reassembled the
    /// records on the way out, and a switch over property names mapped each one back to its
    /// owner. That map was hand-maintained - forget a case and the value edited fine, updated
    /// live consumers, and never persisted. A page addresses its own record now, so which
    /// record owns a preference is a compile-time fact again.
    /// </summary>
    private readonly Dictionary<Type, IPreferenceSettings> _settings = [];

    /// <summary>The section as it stands, for a page building or refreshing its rows.</summary>
    public T Get<T>() where T : class, IPreferenceSettings<T> => (T)_settings[typeof(T)];

    /// <summary>
    /// Applies a change to one section and persists it.
    ///
    /// The section is clamped here as well as in the store, so the rows can never go on showing
    /// a value the store refused - the overhang pair resets when its two angles come too close,
    /// and this is what makes the window show that rather than the pair the user dragged to.
    /// </summary>
    public void Update<T>(Func<T, T> change) where T : class, IPreferenceSettings<T>
    {
        var current = Get<T>();
        var updated = change(current).Clamped();

        // Records compare by value, so an edit that lands on what is already there does nothing.
        if (updated.Equals(current)) { return; }

        _settings[typeof(T)] = updated;
        _messenger.SaveSection(updated);
        RefreshRows();
    }

    /// <summary>
    /// The overhang page draws its own range slider (see RotationPreferencePage), so unlike a
    /// NumberRow it has no descriptor to read through. These two are the only values the view
    /// still exposes directly, and they read and write the rotation section rather than holding
    /// a copy of it.
    /// </summary>
    public float OverhangWarningAngle
    {
        get => Get<RotationPreferences>().OverhangWarningAngle;
        set => SetOverhang(r => r with { OverhangWarningAngle = value });
    }

    public float OverhangCriticalAngle
    {
        get => Get<RotationPreferences>().OverhangCriticalAngle;
        set => SetOverhang(r => r with { OverhangCriticalAngle = value });
    }

    // Clamped() rejects a pair that has come too close together by resetting both angles, so a
    // change to either thumb has to re-announce both.
    private void SetOverhang(Func<RotationPreferences, RotationPreferences> change)
    {
        Update(change);
        OnPropertyChanged(nameof(OverhangWarningAngle));
        OnPropertyChanged(nameof(OverhangCriticalAngle));
    }

    public double OverhangAngleMinimum => RotationPreferences.Ranges.OverhangAngleMin;
    public double OverhangAngleMaximum => RotationPreferences.Ranges.OverhangAngleMax;
    public double OverhangMinimumGap => RotationPreferences.Ranges.OverhangMinGap;

    /// <summary>Which meshes the cut view is offered on.</summary>
    public IReadOnlyList<CutScopeOption> CutScopeOptions { get; } =
        Enum.GetValues<CutViewScope>().Select(v => new CutScopeOption(v, v.ToLabel())).ToList();

    /// <summary>Anchor choices offered by the two auto-place pickers.</summary>
    public IReadOnlyList<AnchorOption> AnchorOptions { get; } =
        Enum.GetValues<DecalAnchor>().Select(a => new AnchorOption(a, a.ToLabel())).ToList();

    /// <summary>Scope choices offered by the auto-place scope picker.</summary>
    public IReadOnlyList<ScopeOption> ScopeOptions { get; } =
        Enum.GetValues<DecalAutoPlaceScope>().Select(s => new ScopeOption(s, s.ToLabel())).ToList();

    /// <param name="pages">
    /// The pages to show. Defaults to the shipped catalogue; overridden by tests.
    /// </param>
    public PreferencesViewModel(
        IMessenger messenger,
        IAlertDialog alert,
        IEnumerable<IPreferencePage>? pages = null)
    {
        _messenger = messenger;
        _alert = alert;
        _pages = pages is null
            ? PreferencePageCatalog.Default
            : PreferencePageCatalog.Sort(pages);

        // Every section, loaded through the same roster the store and the profile use, so a
        // new one is picked up here without this constructor changing.
        PreferenceSections.ForEach(new Loader(this));

        foreach (var page in _pages)
        {
            _rowsByPage[page.Key] = page.BuildRows(this);
        }

        RefreshPageList();
    }

    /// <summary>Re-applies the search filter, keeping a page open if it still matches.</summary>
    private void RefreshPageList()
    {
        var previous = SelectedPage;

        Pages.Clear();
        foreach (var page in _pages.Where(s => s.Matches(SearchText)))
        {
            Pages.Add(page);
        }

        if (previous is not null && Pages.Contains(previous)) { return; }

        SelectedPage = Pages.FirstOrDefault();
    }

    partial void OnSearchTextChanged(string value) => RefreshPageList();

    partial void OnSelectedPageChanged(IPreferencePage? value)
    {
        OnPropertyChanged(nameof(Rows));
        RefreshRows();
    }

    /// <summary>
    /// Rows read their values straight off this view model, so anything that changes a property
    /// from underneath them - restore defaults, an import, a dependent row being switched off -
    /// has to tell them to look again.
    /// </summary>
    private void RefreshRows()
    {
        if (SelectedPage is null) { return; }

        foreach (var row in _rowsByPage[SelectedPage.Key])
        {
            row.Refresh();
        }
    }

    // ---- Commands ------------------------------------------------------
    [RelayCommand]
    private void SetImportFolder()
    {
        var ofd = new OpenFolderDialog
        {
            InitialDirectory = Get<GeneralPreferences>().ImportFolder,
            Title = "Select Import Folder",
            Multiselect = false
        };
        if (ofd.ShowDialog() != true) { return; }

        Update<GeneralPreferences>(g => g with { ImportFolder = Path.GetFullPath(ofd.FolderName) });
    }

    [RelayCommand]
    private void SetExportFolder()
    {
        var ofd = new OpenFolderDialog
        {
            InitialDirectory = Get<GeneralPreferences>().ExportFolder,
            Title = "Select Export Folder",
            Multiselect = false
        };
        if (ofd.ShowDialog() != true) { return; }

        Update<GeneralPreferences>(g => g with { ExportFolder = Path.GetFullPath(ofd.FolderName) });
    }

    [RelayCommand]
    private void RestoreDefaults() => Apply(PreferenceBag.FromDefaults());

    // ---- Profile import / export ---------------------------------------

    /// <summary>Everything currently set, for export.</summary>
    private PreferenceBag Capture()
    {
        var bag = new PreferenceBag();
        PreferenceSections.ForEach(new Writer(this, bag));
        return bag;
    }

    /// <summary>
    /// Replaces every section from a profile, then saves them all.
    ///
    /// The sections are swapped in first and persisted afterwards, so a half-written set never
    /// reaches a live consumer - the flag that used to hold saving off while thirty-nine
    /// properties were assigned one at a time is not needed once a section moves as one value.
    /// </summary>
    private void Apply(IPreferenceReader source)
    {
        PreferenceSections.ForEach(new Reader(this, source));
        PreferenceSections.ForEach(new Saver(this));

        RefreshRows();
        OnPropertyChanged(nameof(OverhangWarningAngle));
        OnPropertyChanged(nameof(OverhangCriticalAngle));
    }

    [RelayCommand]
    private void ExportPreferences()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Preferences",
            Filter = PreferenceProfileIO.FileFilter,
            DefaultExt = ".json",
            FileName = PreferenceProfileIO.DefaultFileName,
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = Directory.Exists(Get<GeneralPreferences>().ExportFolder)
                ? Get<GeneralPreferences>().ExportFolder
                : string.Empty
        };

        if (dialog.ShowDialog() != true) { return; }

        try
        {
            PreferenceProfileIO.Write(dialog.FileName, Capture());
            _alert.ShowInfo($"Preferences exported to {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _alert.ShowError($"Could not write the preferences file.{Environment.NewLine}{e.Message}");
        }
    }

    [RelayCommand]
    private void ImportPreferences()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Preferences",
            Filter = PreferenceProfileIO.FileFilter,
            DefaultExt = ".json",
            Multiselect = false,
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(Get<GeneralPreferences>().ExportFolder)
                ? Get<GeneralPreferences>().ExportFolder
                : string.Empty
        };

        if (dialog.ShowDialog() != true) { return; }

        PreferenceImportResult result;
        try
        {
            result = PreferenceProfileIO.Read(dialog.FileName);
        }
        catch (InvalidDataException e)
        {
            _alert.ShowError($"{Path.GetFileName(dialog.FileName)} could not be imported.{Environment.NewLine}{e.Message}");
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _alert.ShowError($"Could not read the preferences file.{Environment.NewLine}{e.Message}");
            return;
        }

        // Nothing is written until the file has parsed, so a rejected import leaves the
        // current preferences exactly as they were.
        Apply(result.Bag);

        if (result.Adjusted.Count == 0)
        {
            _alert.ShowInfo("Preferences imported.");
            return;
        }

        // Import replaces the whole set, so anything the file did not carry has just moved to
        // its default. Say which, rather than reporting a clean import that silently changed things.
        var detail = string.Join(Environment.NewLine, result.Adjusted.Select(a => "  \u2022 " + a));
        _alert.ShowInfo(
            $"Preferences imported.{Environment.NewLine}{Environment.NewLine}" +
            $"{result.Adjusted.Count} setting(s) were reset to their default because the file did not " +
            $"carry a usable value:{Environment.NewLine}{detail}");
    }

    // ---- Roster walks ---------------------------------------------------
    // Each section needs its own type as a generic argument, which a plain delegate cannot
    // carry, so every "do this to all of them" is a visitor over the one roster.

    private sealed class Loader(PreferencesViewModel vm) : IPreferenceSectionVisitor {
        public void Visit<T>() where T : class, IPreferenceSettings<T> =>
            vm._settings[typeof(T)] = vm._messenger.GetSection(T.Default);
    }

    private sealed class Saver(PreferencesViewModel vm) : IPreferenceSectionVisitor {
        public void Visit<T>() where T : class, IPreferenceSettings<T> =>
            vm._messenger.SaveSection(vm.Get<T>());
    }

    private sealed class Reader(PreferencesViewModel vm, IPreferenceReader source) : IPreferenceSectionVisitor {
        public void Visit<T>() where T : class, IPreferenceSettings<T> =>
            vm._settings[typeof(T)] = T.Read(source).Clamped();
    }

    private sealed class Writer(PreferencesViewModel vm, IPreferenceWriter target) : IPreferenceSectionVisitor {
        public void Visit<T>() where T : class, IPreferenceSettings<T> =>
            vm.Get<T>().Write(target);
    }
}

/// <summary>One entry in the auto-place anchor picker.</summary>
public sealed record AnchorOption(DecalAnchor Value, string Label);

/// <summary>One entry in the cut-view scope picker.</summary>
public sealed record CutScopeOption(CutViewScope Value, string Label);

/// <summary>One entry in the auto-place scope picker.</summary>
public sealed record ScopeOption(DecalAutoPlaceScope Value, string Label);
