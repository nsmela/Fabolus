using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Features.Moulds;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.CutSplit;
using Fabolus.Wpf.Features.Decal;
using Fabolus.Wpf.Features.Moulding;
using Fabolus.Wpf.Features.Rotatation;
using Fabolus.Wpf.Features.Smoothing;
using Microsoft.Win32;

namespace Fabolus.Wpf.Features.AppPreferences;

public partial class PreferencesViewModel : ObservableObject
{
    private readonly IMessenger _messenger;
    private readonly IAlertDialog _alert;

    // ---- Navigation ----------------------------------------------------
    [ObservableProperty] private string _searchText = string.Empty;

    private readonly IReadOnlyList<IPreferenceSection> _sections;
    private readonly Dictionary<string, IReadOnlyList<PreferenceRow>> _rowsBySection = [];

    /// <summary>The sidebar, filtered by the search box.</summary>
    public ObservableCollection<IPreferenceSection> Sections { get; } = [];

    [ObservableProperty] private IPreferenceSection? _selectedSection;

    /// <summary>Rows of the open page. Built once per page and reused.</summary>
    public IReadOnlyList<PreferenceRow> Rows =>
        SelectedSection is null ? [] : _rowsBySection[SelectedSection.Key];

    // ---- Folders -------------------------------------------------------
    [ObservableProperty] private string _importFilepath = string.Empty;
    [ObservableProperty] private string _exportFilepath = string.Empty;
    [ObservableProperty] private ExportFormat _exportFormat;

    // ---- Print bed -----------------------------------------------------
    [ObservableProperty] private float _printbedWidth;
    [ObservableProperty] private float _printbedDepth;
    [ObservableProperty] private bool _showBedGrid;

    // ---- Air channels --------------------------------------------------
    [ObservableProperty] private bool _autodetectChannels;
    [ObservableProperty] private float _channelDiameter;

    // ---- Appearance ----------------------------------------------------
    [ObservableProperty] private ViewportBackground _viewportBackground;

    // ---- Cut / Split --------------------------------------------------
    [ObservableProperty] private bool _enableSplitView;
    [ObservableProperty] private bool _enableCutView;
    [ObservableProperty] private CutViewScope _cutScope;

    /// <summary>Which meshes the cut view is offered on.</summary>
    public IReadOnlyList<CutScopeOption> CutScopeOptions { get; } =
        Enum.GetValues<CutViewScope>().Select(v => new CutScopeOption(v, v.ToLabel())).ToList();

    // ---- Mould ---------------------------------------------------------
    [ObservableProperty] private MouldShapeType _mouldShape;
    [ObservableProperty] private float _mouldWallThickness;
    [ObservableProperty] private float _mouldBaseHeight;
    [ObservableProperty] private float _mouldTroughHeight;
    [ObservableProperty] private float _mouldTroughOffset;
    [ObservableProperty] private TroughShapeType _mouldTroughShape;

    /// <summary>A contoured shell follows the bolus surface, so it has no flat top to recess.</summary>
    public bool MouldSupportsTrough => MouldShape != MouldShapeType.Contoured;

    partial void OnMouldShapeChanged(MouldShapeType value) => OnPropertyChanged(nameof(MouldSupportsTrough));

    // ---- Decals --------------------------------------------------------
    [ObservableProperty] private bool _enableDecals;
    [ObservableProperty] private DecalAutoPlaceScope _decalPlacementScope;
    [ObservableProperty] private bool _autoPlaceFilename;
    [ObservableProperty] private DecalAnchor _filenameAnchor;
    [ObservableProperty] private bool _autoPlaceVolume;
    [ObservableProperty] private DecalAnchor _volumeAnchor;
    [ObservableProperty] private DecalFont _decalDefaultFont;
    [ObservableProperty] private float _decalCapHeight;
    [ObservableProperty] private float _decalDepth;
    [ObservableProperty] private EmbossOperation _decalOperation;

    // ---- Smoothing -----------------------------------------------------
    [ObservableProperty] private int _smoothIterations;
    [ObservableProperty] private float _smoothIntensity;
    [ObservableProperty] private float _smoothInflation;
    [ObservableProperty] private float _smoothRemeshRatio;
    [ObservableProperty] private float _smoothResolution;
    [ObservableProperty] private SmoothDisplayMode _smoothDisplay;

    // ---- Rotation ------------------------------------------------------
    [ObservableProperty] private float _overhangWarningAngle;
    [ObservableProperty] private float _overhangCriticalAngle;

    /// <summary>Anchor choices offered by the two auto-place pickers.</summary>
    public IReadOnlyList<AnchorOption> AnchorOptions { get; } =
        Enum.GetValues<DecalAnchor>().Select(a => new AnchorOption(a, a.ToLabel())).ToList();

    /// <summary>Scope choices offered by the auto-place scope picker.</summary>
    public IReadOnlyList<ScopeOption> ScopeOptions { get; } =
        Enum.GetValues<DecalAutoPlaceScope>().Select(s => new ScopeOption(s, s.ToLabel())).ToList();

    /// <param name="sections">
    /// The pages to show. Defaults to the shipped catalogue; overridden by tests.
    /// </param>
    public PreferencesViewModel(
        IMessenger messenger,
        IAlertDialog alert,
        IEnumerable<IPreferenceSection>? sections = null)
    {
        _messenger = messenger;
        _alert = alert;
        _sections = sections is null
            ? PreferenceSectionCatalog.Default
            : PreferenceSectionCatalog.Sort(sections);

        // Each section arrives already validated by its own Clamped(), so nothing here has to
        // re-parse strings or guard against a value the running build no longer recognises.
        var general = _messenger.GetSection(GeneralPreferences.Default);
        _importFilepath = general.ImportFolder;
        _exportFilepath = general.ExportFolder;
        _exportFormat = general.ExportFormat;
        _viewportBackground = general.ViewportBackground;

        var bed = _messenger.GetSection(PrintBedPreferences.Default);
        _printbedWidth = bed.Width;
        _printbedDepth = bed.Depth;
        _showBedGrid = bed.ShowGrid;
        _autodetectChannels = bed.AutodetectChannels;
        _channelDiameter = bed.ChannelDiameter;

        var cutSplit = _messenger.GetSection(CutSplitPreferences.Default);
        _enableCutView = cutSplit.CutViewEnabled;
        _cutScope = cutSplit.CutScope;
        _enableSplitView = cutSplit.SplitViewEnabled;

        var decal = _messenger.GetSection(DecalPreferences.Default);
        _enableDecals = decal.Enabled;
        _decalPlacementScope = decal.Scope;
        _autoPlaceFilename = decal.AutoPlaceFilename;
        _filenameAnchor = decal.FilenameAnchor;
        _autoPlaceVolume = decal.AutoPlaceVolume;
        _volumeAnchor = decal.VolumeAnchor;
        _decalDefaultFont = decal.Font;
        _decalCapHeight = decal.CapHeight;
        _decalDepth = decal.Depth;
        _decalOperation = decal.Operation;

        var smooth = _messenger.GetSection(SmoothingPreferences.Default);
        _smoothIterations = smooth.Iterations;
        _smoothIntensity = smooth.Intensity;
        _smoothInflation = smooth.Inflation;
        _smoothRemeshRatio = smooth.RemeshRatio;
        _smoothResolution = smooth.Resolution;
        _smoothDisplay = smooth.DisplayMode;

        var rotation = _messenger.GetSection(RotationPreferences.Default);
        _overhangWarningAngle = rotation.OverhangWarningAngle;
        _overhangCriticalAngle = rotation.OverhangCriticalAngle;

        var mould = _messenger.GetSection(MouldPreferences.Default);
        _mouldShape = mould.Shape;
        _mouldWallThickness = mould.WallThickness;
        _mouldBaseHeight = mould.BaseHeight;
        _mouldTroughHeight = mould.TroughHeight;
        _mouldTroughOffset = mould.TroughOffset;
        _mouldTroughShape = mould.TroughShape;

        foreach (var section in _sections)
        {
            _rowsBySection[section.Key] = section.BuildRows(this);
        }

        RefreshSectionList();
    }

    /// <summary>Re-applies the search filter, keeping a page open if it still matches.</summary>
    private void RefreshSectionList()
    {
        var previous = SelectedSection;

        Sections.Clear();
        foreach (var section in _sections.Where(s => s.Matches(SearchText)))
        {
            Sections.Add(section);
        }

        if (previous is not null && Sections.Contains(previous)) { return; }

        SelectedSection = Sections.FirstOrDefault();
    }

    partial void OnSearchTextChanged(string value) => RefreshSectionList();

    partial void OnSelectedSectionChanged(IPreferenceSection? value)
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
        if (SelectedSection is null) { return; }

        foreach (var row in _rowsBySection[SelectedSection.Key])
        {
            row.Refresh();
        }
    }

    // ---- Change notifications → store ---------------------------------

    /// <summary>
    /// Set while a whole profile is being written, so restoring defaults or importing a file
    /// saves each affected section once at the end rather than once per property it touches.
    /// </summary>
    private bool _applyingProfile;

    /// <summary>
    /// Routes a changed property to the section that owns it, and saves that section.
    ///
    /// [ObservableProperty] only raises this when the value actually changed, so an assignment
    /// that matches what is already there costs nothing - the per-property equality guards the
    /// old handlers carried were doing work SetProperty had already done.
    /// </summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // One preference can gate another - the decal defaults under the decal switch, the
        // trough rows under a non-contoured shape - so any change re-reads the visible rows.
        if (e.PropertyName is not (nameof(Rows) or nameof(SearchText) or nameof(SelectedSection)))
        {
            RefreshRows();
        }

        if (_applyingProfile) { return; }

        switch (e.PropertyName)
        {
            case nameof(ImportFilepath):
            case nameof(ExportFilepath):
            case nameof(ExportFormat):
            case nameof(ViewportBackground):
                _messenger.SaveSection(CaptureGeneral());
                break;

            case nameof(PrintbedWidth):
            case nameof(PrintbedDepth):
            case nameof(ShowBedGrid):
            case nameof(AutodetectChannels):
            case nameof(ChannelDiameter):
                _messenger.SaveSection(CapturePrintBed());
                break;

            case nameof(EnableCutView):
            case nameof(CutScope):
            case nameof(EnableSplitView):
                _messenger.SaveSection(CaptureCutSplit());
                break;

            case nameof(EnableDecals):
            case nameof(DecalPlacementScope):
            case nameof(AutoPlaceFilename):
            case nameof(FilenameAnchor):
            case nameof(AutoPlaceVolume):
            case nameof(VolumeAnchor):
            case nameof(DecalDefaultFont):
            case nameof(DecalCapHeight):
            case nameof(DecalDepth):
            case nameof(DecalOperation):
                _messenger.SaveSection(CaptureDecal());
                break;

            case nameof(SmoothIterations):
            case nameof(SmoothIntensity):
            case nameof(SmoothInflation):
            case nameof(SmoothRemeshRatio):
            case nameof(SmoothResolution):
            case nameof(SmoothDisplay):
                _messenger.SaveSection(CaptureSmoothing());
                break;

            case nameof(OverhangWarningAngle):
            case nameof(OverhangCriticalAngle):
                _messenger.SaveSection(CaptureRotation());
                break;

            case nameof(MouldShape):
            case nameof(MouldWallThickness):
            case nameof(MouldBaseHeight):
            case nameof(MouldTroughHeight):
            case nameof(MouldTroughOffset):
            case nameof(MouldTroughShape):
                _messenger.SaveSection(CaptureMould());
                break;
        }
    }

    // ---- Section captures ----------------------------------------------

    private GeneralPreferences CaptureGeneral() =>
        new(ImportFilepath, ExportFilepath, ExportFormat, ViewportBackground);

    private PrintBedPreferences CapturePrintBed() =>
        new(PrintbedWidth, PrintbedDepth, ShowBedGrid, AutodetectChannels, ChannelDiameter);

    private CutSplitPreferences CaptureCutSplit() =>
        new(EnableCutView, CutScope, EnableSplitView);

    private DecalPreferences CaptureDecal() =>
        new(EnableDecals, DecalPlacementScope, AutoPlaceFilename, FilenameAnchor, AutoPlaceVolume,
            VolumeAnchor, DecalDefaultFont, DecalCapHeight, DecalDepth, DecalOperation);

    private SmoothingPreferences CaptureSmoothing() =>
        new(SmoothIterations, SmoothIntensity, SmoothInflation, SmoothRemeshRatio, SmoothResolution, SmoothDisplay);

    private RotationPreferences CaptureRotation() =>
        new(OverhangWarningAngle, OverhangCriticalAngle);

    private MouldPreferences CaptureMould() =>
        new(MouldShape, MouldWallThickness, MouldBaseHeight, MouldTroughHeight, MouldTroughOffset, MouldTroughShape);

    /// <summary>Saves every section. Used after a profile has been written onto the properties.</summary>
    private void SaveAllSections()
    {
        _messenger.SaveSection(CaptureGeneral());
        _messenger.SaveSection(CapturePrintBed());
        _messenger.SaveSection(CaptureCutSplit());
        _messenger.SaveSection(CaptureDecal());
        _messenger.SaveSection(CaptureSmoothing());
        _messenger.SaveSection(CaptureRotation());
        _messenger.SaveSection(CaptureMould());
    }

    // ---- Commands ------------------------------------------------------
    [RelayCommand]
    private void SetImportFolder()
    {
        var ofd = new OpenFolderDialog
        {
            InitialDirectory = ImportFilepath,
            Title = "Select Import Folder",
            Multiselect = false
        };
        if (ofd.ShowDialog() != true) { return; }

        ImportFilepath = Path.GetFullPath(ofd.FolderName);
    }

    [RelayCommand]
    private void SetExportFolder()
    {
        var ofd = new OpenFolderDialog
        {
            InitialDirectory = ExportFilepath,
            Title = "Select Export Folder",
            Multiselect = false
        };
        if (ofd.ShowDialog() != true) { return; }

        ExportFilepath = Path.GetFullPath(ofd.FolderName);
    }

    [RelayCommand]
    private void RestoreDefaults() => Apply(PreferenceBag.FromDefaults());

    // ---- Profile import / export ---------------------------------------

    /// <summary>Everything currently set, for export.</summary>
    private PreferenceBag Capture()
    {
        var bag = new PreferenceBag();
        CaptureGeneral().Write(bag);
        CapturePrintBed().Write(bag);
        CaptureCutSplit().Write(bag);
        CaptureDecal().Write(bag);
        CaptureSmoothing().Write(bag);
        CaptureRotation().Write(bag);
        CaptureMould().Write(bag);
        return bag;
    }

    /// <summary>
    /// Writes a profile onto every property, then saves each section once. Saving is held off
    /// until the end so a profile touching five print-bed values persists that section once
    /// instead of five times - and so half-written sections never reach live consumers.
    /// </summary>
    private void Apply(PreferenceBag bag)
    {
        _applyingProfile = true;
        try
        {
            ApplyToProperties(bag);
        }
        finally
        {
            _applyingProfile = false;
        }

        SaveAllSections();
    }

    private void ApplyToProperties(IPreferenceReader source)
    {
        var general = GeneralPreferences.Read(source).Clamped();
        ImportFilepath = general.ImportFolder;
        ExportFilepath = general.ExportFolder;
        ExportFormat = general.ExportFormat;
        ViewportBackground = general.ViewportBackground;

        var bed = PrintBedPreferences.Read(source).Clamped();
        PrintbedWidth = bed.Width;
        PrintbedDepth = bed.Depth;
        ShowBedGrid = bed.ShowGrid;
        AutodetectChannels = bed.AutodetectChannels;
        ChannelDiameter = bed.ChannelDiameter;

        var cutSplit = CutSplitPreferences.Read(source).Clamped();
        EnableCutView = cutSplit.CutViewEnabled;
        CutScope = cutSplit.CutScope;
        EnableSplitView = cutSplit.SplitViewEnabled;

        var decal = DecalPreferences.Read(source).Clamped();
        EnableDecals = decal.Enabled;
        DecalPlacementScope = decal.Scope;
        AutoPlaceFilename = decal.AutoPlaceFilename;
        FilenameAnchor = decal.FilenameAnchor;
        AutoPlaceVolume = decal.AutoPlaceVolume;
        VolumeAnchor = decal.VolumeAnchor;
        DecalDefaultFont = decal.Font;
        DecalCapHeight = decal.CapHeight;
        DecalDepth = decal.Depth;
        DecalOperation = decal.Operation;

        var smooth = SmoothingPreferences.Read(source).Clamped();
        SmoothIterations = smooth.Iterations;
        SmoothIntensity = smooth.Intensity;
        SmoothInflation = smooth.Inflation;
        SmoothRemeshRatio = smooth.RemeshRatio;
        SmoothResolution = smooth.Resolution;
        SmoothDisplay = smooth.DisplayMode;

        var rotation = RotationPreferences.Read(source).Clamped();
        // Critical first: the range slider will not let the lower thumb pass the upper one, so
        // raising the ceiling before the floor keeps a profile with higher thresholds than the
        // current pair from being clamped on the way in.
        OverhangCriticalAngle = rotation.OverhangCriticalAngle;
        OverhangWarningAngle = rotation.OverhangWarningAngle;

        var mould = MouldPreferences.Read(source).Clamped();
        MouldShape = mould.Shape;
        MouldWallThickness = mould.WallThickness;
        MouldBaseHeight = mould.BaseHeight;
        MouldTroughHeight = mould.TroughHeight;
        MouldTroughOffset = mould.TroughOffset;
        MouldTroughShape = mould.TroughShape;
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
            InitialDirectory = Directory.Exists(ExportFilepath) ? ExportFilepath : string.Empty
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
            InitialDirectory = Directory.Exists(ExportFilepath) ? ExportFilepath : string.Empty
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
}

/// <summary>One entry in the auto-place anchor picker.</summary>
public sealed record AnchorOption(DecalAnchor Value, string Label);

/// <summary>One entry in the cut-view scope picker.</summary>
public sealed record CutScopeOption(CutViewScope Value, string Label);

/// <summary>One entry in the auto-place scope picker.</summary>
public sealed record ScopeOption(DecalAutoPlaceScope Value, string Label);
