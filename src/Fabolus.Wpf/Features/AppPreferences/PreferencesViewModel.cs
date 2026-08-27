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
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Smoothing;
using Microsoft.Win32;

namespace Fabolus.Wpf.Pages.Preferences;

public partial class PreferencesViewModel : ObservableObject
{
    private readonly IMessenger _messenger;
    private readonly AppPreferencesStore _store;
    private readonly IAlertDialog _alert;

    // ---- Navigation ----------------------------------------------------
    [ObservableProperty] private string _selectedCategoryKey = "general";
    [ObservableProperty] private string _searchText = string.Empty;

    public ObservableCollection<PreferenceCategory> Categories { get; }
    public ICollectionView FilteredCategories { get; }

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

    public PreferencesViewModel(IMessenger messenger, AppPreferencesStore store, IAlertDialog alert)
    {
        _messenger = messenger;
        _store = store;
        _alert = alert;

        _importFilepath = (string)_messenger.Send(new AppPreferenceRequestMessage(UISettings.DefaultImportFolderLabel)).Response;
        _exportFilepath = (string)_messenger.Send(new AppPreferenceRequestMessage(UISettings.DefaultExportFolderLabel)).Response;
        _exportFormat = Enum.Parse<ExportFormat>((string)_messenger.Send(new AppPreferenceRequestMessage(UISettings.DefaultExportFormatLabel)).Response);
        _printbedWidth = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.PrintBedWidthLabel)).Response;
        _printbedDepth = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.PrintBedDepthLabel)).Response;
        _showBedGrid = (bool)_messenger.Send(new AppPreferenceRequestMessage(UISettings.ShowBedGridLabel)).Response;
        _autodetectChannels = (bool)_messenger.Send(new AppPreferenceRequestMessage(UISettings.AutodetectChannelsLabel)).Response;
        _channelDiameter = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.ChannelDiameterLabel)).Response;
        _viewportBackground = Enum.Parse<ViewportBackground>((string)_messenger.Send(new AppPreferenceRequestMessage(UISettings.ViewportBackgroundLabel)).Response);
        _enableSplitView = (bool)_messenger.Send(new AppPreferenceRequestMessage(UISettings.SplitViewEnabledLabel)).Response;
        _enableCutView = (bool)_messenger.Send(new AppPreferenceRequestMessage(UISettings.CutViewEnabledLabel)).Response;
        _enableDecals = (bool)_messenger.Send(new AppPreferenceRequestMessage(UISettings.DecalsEnabledLabel)).Response;
        _decalPlacementScope = Enum.Parse<DecalAutoPlaceScope>((string)_messenger.Send(new AppPreferenceRequestMessage(UISettings.DecalAutoPlaceScopeLabel)).Response);
        _autoPlaceFilename = (bool)_messenger.Send(new AppPreferenceRequestMessage(UISettings.DecalAutoPlaceFilenameLabel)).Response;
        _filenameAnchor = Enum.Parse<DecalAnchor>((string)_messenger.Send(new AppPreferenceRequestMessage(UISettings.DecalFilenameAnchorLabel)).Response);
        _autoPlaceVolume = (bool)_messenger.Send(new AppPreferenceRequestMessage(UISettings.DecalAutoPlaceVolumeLabel)).Response;
        _volumeAnchor = Enum.Parse<DecalAnchor>((string)_messenger.Send(new AppPreferenceRequestMessage(UISettings.DecalVolumeAnchorLabel)).Response);
        _decalDefaultFont = Enum.Parse<DecalFont>((string)_messenger.Send(new AppPreferenceRequestMessage(UISettings.DecalDefaultFontLabel)).Response);
        _decalCapHeight = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.DecalDefaultCapHeightLabel)).Response;
        _decalDepth = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.DecalDefaultDepthLabel)).Response;
        _decalOperation = Enum.Parse<EmbossOperation>((string)_messenger.Send(new AppPreferenceRequestMessage(UISettings.DecalDefaultOperationLabel)).Response);
        _smoothIterations = (int)_messenger.Send(new AppPreferenceRequestMessage(UISettings.SmoothIterationsLabel)).Response;
        _smoothIntensity = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.SmoothIntensityLabel)).Response;
        _smoothInflation = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.SmoothInflationLabel)).Response;
        _smoothRemeshRatio = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.SmoothRemeshRatioLabel)).Response;
        _smoothResolution = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.SmoothResolutionLabel)).Response;
        _smoothDisplay = Enum.Parse<SmoothDisplayMode>((string)_messenger.Send(new AppPreferenceRequestMessage(UISettings.SmoothDisplayModeLabel)).Response);
        _overhangWarningAngle = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.OverhangWarningAngleLabel)).Response;
        _overhangCriticalAngle = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.OverhangCriticalAngleLabel)).Response;
        _cutScope = Enum.Parse<CutViewScope>((string)_messenger.Send(new AppPreferenceRequestMessage(UISettings.CutViewScopeLabel)).Response);
        _mouldShape = Enum.Parse<MouldShapeType>((string)_messenger.Send(new AppPreferenceRequestMessage(UISettings.MouldShapeLabel)).Response);
        _mouldWallThickness = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.MouldWallThicknessLabel)).Response;
        _mouldBaseHeight = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.MouldBaseHeightLabel)).Response;
        _mouldTroughHeight = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.MouldTroughHeightLabel)).Response;
        _mouldTroughOffset = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.MouldTroughOffsetLabel)).Response;
        _mouldTroughShape = Enum.Parse<TroughShapeType>((string)_messenger.Send(new AppPreferenceRequestMessage(UISettings.MouldTroughShapeLabel)).Response);

        Categories = new ObservableCollection<PreferenceCategory> {
            new() { Key = "general",      Name = "General",      Keywords = "folder import export format file", Icon = Geo("M8 1.4v1.9M8 12.7v1.9M14.6 8h-1.9M3.3 8H1.4M12.7 3.3l-1.3 1.3M4.6 11.4l-1.3 1.3M12.7 12.7l-1.3-1.3M4.6 4.6 3.3 3.3 M10.3 8 a2.3 2.3 0 1 1 -4.6 0 a2.3 2.3 0 0 1 4.6 0") },
            new() { Key = "bed",          Name = "Print Bed",    Keywords = "width depth height size volume grid", Icon = Geo("M8 2 14 5v6l-6 3-6-3V5z M2 5l6 3 6-3 M8 8v6") },
            new() { Key = "rotation",     Name = "Rotation",     Keywords = "rotate overhang angle threshold warning critical support", Icon = Geo("M13.2 8 a5.2 5.2 0 1 1 -1.7 -3.9 M13.4 2.6 L13.4 5.2 L10.8 5.2") },
            new() { Key = "smoothing",    Name = "Smoothing",    Keywords = "smooth intensity inflation iterations triangle ratio resolution voxel display heatmap cross section", Icon = Geo("M2 10 C4 5.6 6 5.6 8 10 S12 14.4 14 10") },
            new() { Key = "cut",          Name = "Cut",          Keywords = "cut view toggle base mould scope", Icon = Geo("M6.2 2h3.6 M6.6 2v3.4L3.3 12a1.3 1.3 0 0 0 1.1 2h7.2a1.3 1.3 0 0 0 1.1-2L9.4 5.4V2") },
            new() { Key = "split",        Name = "Split",        Keywords = "split parting line view toggle", Icon = Geo("M8 1.8 L8 14.2 M3 5 L3 11 M13 5 L13 11") },
            new() { Key = "channels",     Name = "Air Channels", Keywords = "autodetect diameter vent",           Icon = Geo("M4.3 2.5v11 M8 2.5v11 M11.7 2.5v11") },
            new() { Key = "mould",        Name = "Mould",        Keywords = "shape convex concave contoured wall thickness base height trough depth margin", Icon = Geo("M8 2.2 L13.5 5 L13.5 11 L8 13.8 L2.5 11 L2.5 5 Z M2.5 5 L8 7.8 L13.5 5 M8 7.8 L8 13.8") },
            new() { Key = "decals",       Name = "Decals",       Keywords = "text emboss engrave font label filename volume anchor", Icon = Geo("M3.5 2.5 L12.5 2.5 a1 1 0 0 1 1 1 L13.5 9.5 L9.5 13.5 L3.5 13.5 a1 1 0 0 1 -1 -1 L2.5 3.5 a1 1 0 0 1 1 -1 Z M9.5 9.5 L13.5 9.5 M9.5 9.5 L9.5 13.5 M5.5 6 L10.5 6 M8 6 L8 10") },
            new() { Key = "appearance",   Name = "Appearance",   Keywords = "theme viewport background",          Icon = Geo("M8 2 a6 6 0 1 0 0 12 c1 0 1.4-.7 1.4-1.4 0-.9-.8-1.2-.8-2 0-.6.5-1 1.1-1 H12 a2.5 2.5 0 0 0 2.5-2.5 C14.5 4.4 11.6 2 8 2z") },
        };

        FilteredCategories = CollectionViewSource.GetDefaultView(Categories);
        FilteredCategories.Filter = o => o is PreferenceCategory c && c.Matches(SearchText);
    }

    private static System.Windows.Media.Geometry Geo(string data)
    {
        var g = System.Windows.Media.Geometry.Parse(data);
        g.Freeze();
        return g;
    }

    partial void OnSearchTextChanged(string value)
    {
        FilteredCategories.Refresh();
        // If the active category was filtered out, jump to the first visible one.
        var stillVisible = Categories.Any(c => c.Key == SelectedCategoryKey && c.Matches(value));
        if (!stillVisible)
        {
            var first = FilteredCategories.Cast<PreferenceCategory>().FirstOrDefault();
            if (first is not null) { SelectedCategoryKey = first.Key; }
        }
    }

    // ---- Change notifications → store ---------------------------------
    partial void OnImportFilepathChanged(string oldValue, string newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.DefaultImportFolderLabel, newValue));
    }

    partial void OnExportFilepathChanged(string oldValue, string newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.DefaultExportFolderLabel, newValue));
    }

    partial void OnExportFormatChanged(ExportFormat value)
        => _messenger.Send(new AppPreferenceUpdateMessage(UISettings.DefaultExportFormatLabel, value));

    partial void OnPrintbedWidthChanged(float oldValue, float newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.PrintBedWidthLabel, newValue));
    }

    partial void OnPrintbedDepthChanged(float oldValue, float newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.PrintBedDepthLabel, newValue));
    }

    partial void OnShowBedGridChanged(bool value)
        => _messenger.Send(new AppPreferenceUpdateMessage(UISettings.ShowBedGridLabel, value));

    partial void OnAutodetectChannelsChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.AutodetectChannelsLabel, newValue));
    }

    partial void OnChannelDiameterChanged(float oldValue, float newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.ChannelDiameterLabel, newValue));
    }


    partial void OnViewportBackgroundChanged(ViewportBackground value)
        => _messenger.Send(new AppPreferenceUpdateMessage(UISettings.ViewportBackgroundLabel, value));

    partial void OnEnableSplitViewChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.SplitViewEnabledLabel, newValue));
    }

    partial void OnEnableCutViewChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.CutViewEnabledLabel, newValue));
    }

    partial void OnEnableDecalsChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.DecalsEnabledLabel, newValue));
    }

    partial void OnDecalPlacementScopeChanged(DecalAutoPlaceScope value)
        => _messenger.Send(new AppPreferenceUpdateMessage(UISettings.DecalAutoPlaceScopeLabel, value));

    partial void OnAutoPlaceFilenameChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.DecalAutoPlaceFilenameLabel, newValue));
    }

    partial void OnFilenameAnchorChanged(DecalAnchor value)
        => _messenger.Send(new AppPreferenceUpdateMessage(UISettings.DecalFilenameAnchorLabel, value));

    partial void OnAutoPlaceVolumeChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.DecalAutoPlaceVolumeLabel, newValue));
    }

    partial void OnVolumeAnchorChanged(DecalAnchor value)
        => _messenger.Send(new AppPreferenceUpdateMessage(UISettings.DecalVolumeAnchorLabel, value));

    partial void OnDecalDefaultFontChanged(DecalFont value)
        => _messenger.Send(new AppPreferenceUpdateMessage(UISettings.DecalDefaultFontLabel, value));

    partial void OnDecalCapHeightChanged(float oldValue, float newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.DecalDefaultCapHeightLabel, newValue));
    }

    partial void OnDecalDepthChanged(float oldValue, float newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.DecalDefaultDepthLabel, newValue));
    }

    partial void OnDecalOperationChanged(EmbossOperation value)
        => _messenger.Send(new AppPreferenceUpdateMessage(UISettings.DecalDefaultOperationLabel, value));

    partial void OnSmoothIterationsChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.SmoothIterationsLabel, newValue));
    }

    partial void OnSmoothIntensityChanged(float oldValue, float newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.SmoothIntensityLabel, newValue));
    }

    partial void OnSmoothInflationChanged(float oldValue, float newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.SmoothInflationLabel, newValue));
    }

    partial void OnSmoothRemeshRatioChanged(float oldValue, float newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.SmoothRemeshRatioLabel, newValue));
    }

    partial void OnSmoothResolutionChanged(float oldValue, float newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.SmoothResolutionLabel, newValue));
    }

    partial void OnSmoothDisplayChanged(SmoothDisplayMode value)
        => _messenger.Send(new AppPreferenceUpdateMessage(UISettings.SmoothDisplayModeLabel, value));

    partial void OnOverhangWarningAngleChanged(float oldValue, float newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.OverhangWarningAngleLabel, newValue));
    }

    partial void OnOverhangCriticalAngleChanged(float oldValue, float newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.OverhangCriticalAngleLabel, newValue));
    }

    partial void OnCutScopeChanged(CutViewScope value)
        => _messenger.Send(new AppPreferenceUpdateMessage(UISettings.CutViewScopeLabel, value));

    partial void OnMouldShapeChanged(MouldShapeType oldValue, MouldShapeType newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.MouldShapeLabel, newValue));
    }

    partial void OnMouldWallThicknessChanged(float oldValue, float newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.MouldWallThicknessLabel, newValue));
    }

    partial void OnMouldBaseHeightChanged(float oldValue, float newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.MouldBaseHeightLabel, newValue));
    }

    partial void OnMouldTroughHeightChanged(float oldValue, float newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.MouldTroughHeightLabel, newValue));
    }

    partial void OnMouldTroughOffsetChanged(float oldValue, float newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.MouldTroughOffsetLabel, newValue));
    }

    partial void OnMouldTroughShapeChanged(TroughShapeType value)
        => _messenger.Send(new AppPreferenceUpdateMessage(UISettings.MouldTroughShapeLabel, value));

    // ---- Commands ------------------------------------------------------
    [RelayCommand]
    private void SelectCategory(string key) => SelectedCategoryKey = key;

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
    private void RestoreDefaults() => Apply(PreferenceProfile.Defaults);

    // ---- Profile import / export ---------------------------------------

    /// <summary>Everything currently set, as one value, for export.</summary>
    private PreferenceProfile Capture() => new()
    {
        ImportFolder = ImportFilepath,
        ExportFolder = ExportFilepath,
        ExportFormat = ExportFormat,
        PrintBedWidth = PrintbedWidth,
        PrintBedDepth = PrintbedDepth,
        ShowBedGrid = ShowBedGrid,
        AutodetectChannels = AutodetectChannels,
        ChannelDiameter = ChannelDiameter,
        ViewportBackground = ViewportBackground,
        SplitViewEnabled = EnableSplitView,
        CutViewEnabled = EnableCutView,
        CutScope = CutScope,
        MouldShape = MouldShape,
        MouldWallThickness = MouldWallThickness,
        MouldBaseHeight = MouldBaseHeight,
        MouldTroughHeight = MouldTroughHeight,
        MouldTroughOffset = MouldTroughOffset,
        MouldTroughShape = MouldTroughShape,
        DecalsEnabled = EnableDecals,
        DecalScope = DecalPlacementScope,
        AutoPlaceFilename = AutoPlaceFilename,
        FilenameAnchor = FilenameAnchor,
        AutoPlaceVolume = AutoPlaceVolume,
        VolumeAnchor = VolumeAnchor,
        DecalFont = DecalDefaultFont,
        DecalCapHeight = DecalCapHeight,
        DecalDepth = DecalDepth,
        DecalOperation = DecalOperation,
        SmoothIterations = SmoothIterations,
        SmoothIntensity = SmoothIntensity,
        SmoothInflation = SmoothInflation,
        SmoothRemeshRatio = SmoothRemeshRatio,
        SmoothResolution = SmoothResolution,
        SmoothDisplay = SmoothDisplay,
        OverhangWarningAngle = OverhangWarningAngle,
        OverhangCriticalAngle = OverhangCriticalAngle,
    };

    /// <summary>
    /// Writes a profile onto every property. Each assignment fires its own change handler,
    /// which persists that key and notifies live consumers - so a value that already matches
    /// costs nothing and one that differs updates the running app straight away.
    /// </summary>
    private void Apply(PreferenceProfile p)
    {
        ImportFilepath = p.ImportFolder;
        ExportFilepath = p.ExportFolder;
        ExportFormat = p.ExportFormat;
        PrintbedWidth = p.PrintBedWidth;
        PrintbedDepth = p.PrintBedDepth;
        ShowBedGrid = p.ShowBedGrid;
        AutodetectChannels = p.AutodetectChannels;
        ChannelDiameter = p.ChannelDiameter;
        ViewportBackground = p.ViewportBackground;
        EnableSplitView = p.SplitViewEnabled;
        EnableCutView = p.CutViewEnabled;
        CutScope = p.CutScope;
        MouldShape = p.MouldShape;
        MouldWallThickness = p.MouldWallThickness;
        MouldBaseHeight = p.MouldBaseHeight;
        MouldTroughHeight = p.MouldTroughHeight;
        MouldTroughOffset = p.MouldTroughOffset;
        MouldTroughShape = p.MouldTroughShape;
        EnableDecals = p.DecalsEnabled;
        DecalPlacementScope = p.DecalScope;
        AutoPlaceFilename = p.AutoPlaceFilename;
        FilenameAnchor = p.FilenameAnchor;
        AutoPlaceVolume = p.AutoPlaceVolume;
        VolumeAnchor = p.VolumeAnchor;
        DecalDefaultFont = p.DecalFont;
        DecalCapHeight = p.DecalCapHeight;
        DecalDepth = p.DecalDepth;
        DecalOperation = p.DecalOperation;
        SmoothIterations = p.SmoothIterations;
        SmoothIntensity = p.SmoothIntensity;
        SmoothInflation = p.SmoothInflation;
        SmoothRemeshRatio = p.SmoothRemeshRatio;
        SmoothResolution = p.SmoothResolution;
        SmoothDisplay = p.SmoothDisplay;
        // Critical first: the range slider will not let the lower thumb pass the upper one, so
        // raising the ceiling before the floor keeps a profile with higher thresholds than the
        // current pair from being clamped on the way in.
        OverhangCriticalAngle = p.OverhangCriticalAngle;
        OverhangWarningAngle = p.OverhangWarningAngle;
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
        Apply(result.Profile);

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
