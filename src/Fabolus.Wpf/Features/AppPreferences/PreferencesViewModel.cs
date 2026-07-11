using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Features.AppPreferences;
using Microsoft.Win32;

namespace Fabolus.Wpf.Pages.Preferences;

public partial class PreferencesViewModel : ObservableObject
{
    private readonly IMessenger _messenger;
    private readonly AppPreferencesStore _store;

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
    [ObservableProperty] private float _printbedHeight;
    [ObservableProperty] private bool _showBedGrid;

    // ---- Air channels --------------------------------------------------
    [ObservableProperty] private bool _autodetectChannels;
    [ObservableProperty] private float _channelDiameter;

    // ---- Appearance ----------------------------------------------------
    [ObservableProperty] private ViewportBackground _viewportBackground;
    [ObservableProperty] private MeasurementUnit _units;

    // ---- Cut / Split --------------------------------------------------
    [ObservableProperty] private bool _enableSplitView;
    [ObservableProperty] private bool _enableCutView;

    public PreferencesViewModel(IMessenger messenger, AppPreferencesStore store)
    {
        _messenger = messenger;
        _store = store;

        _importFilepath = (string)_messenger.Send(new AppPreferenceRequestMessage(UISettings.DefaultImportFolderLabel)).Response;
        _exportFilepath = (string)_messenger.Send(new AppPreferenceRequestMessage(UISettings.DefaultExportFolderLabel)).Response;
        _exportFormat = Enum.Parse<ExportFormat>((string)_messenger.Send(new AppPreferenceRequestMessage(UISettings.DefaultExportFormatLabel)).Response);
        _printbedWidth = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.PrintBedWidthLabel)).Response;
        _printbedDepth = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.PrintBedDepthLabel)).Response;
        _printbedHeight = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.PrintBedHeightLabel)).Response;
        _showBedGrid = (bool)_messenger.Send(new AppPreferenceRequestMessage(UISettings.ShowBedGridLabel)).Response;
        _autodetectChannels = (bool)_messenger.Send(new AppPreferenceRequestMessage(UISettings.AutodetectChannelsLabel)).Response;
        _channelDiameter = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.ChannelDiameterLabel)).Response;
        _viewportBackground = Enum.Parse<ViewportBackground>((string)_messenger.Send(new AppPreferenceRequestMessage(UISettings.ViewportBackgroundLabel)).Response);
        _units = Enum.Parse<MeasurementUnit>((string)_messenger.Send(new AppPreferenceRequestMessage(UISettings.UnitsLabel)).Response);
        _enableSplitView = (bool)_messenger.Send(new AppPreferenceRequestMessage(UISettings.SplitViewEnabledLabel)).Response;
        _enableCutView = (bool)_messenger.Send(new AppPreferenceRequestMessage(UISettings.CutViewEnabledLabel)).Response;

        Categories = new ObservableCollection<PreferenceCategory> {
            new() { Key = "general",      Name = "General",      Keywords = "folder import export format file", Icon = Geo("M8 1.4v1.9M8 12.7v1.9M14.6 8h-1.9M3.3 8H1.4M12.7 3.3l-1.3 1.3M4.6 11.4l-1.3 1.3M12.7 12.7l-1.3-1.3M4.6 4.6 3.3 3.3 M10.3 8 a2.3 2.3 0 1 1 -4.6 0 a2.3 2.3 0 0 1 4.6 0") },
            new() { Key = "bed",          Name = "Print Bed",    Keywords = "width depth height size volume grid", Icon = Geo("M8 2 14 5v6l-6 3-6-3V5z M2 5l6 3 6-3 M8 8v6") },
            new() { Key = "channels",     Name = "Air Channels", Keywords = "autodetect diameter vent",           Icon = Geo("M4.3 2.5v11 M8 2.5v11 M11.7 2.5v11") },
            new() { Key = "cutsplit",     Name = "Cut / Split",  Keywords = "cut split view toggle",              Icon = Geo("M6.2 2h3.6 M6.6 2v3.4L3.3 12a1.3 1.3 0 0 0 1.1 2h7.2a1.3 1.3 0 0 0 1.1-2L9.4 5.4V2") },
            new() { Key = "appearance",   Name = "Appearance",   Keywords = "theme viewport units",               Icon = Geo("M8 2 a6 6 0 1 0 0 12 c1 0 1.4-.7 1.4-1.4 0-.9-.8-1.2-.8-2 0-.6.5-1 1.1-1 H12 a2.5 2.5 0 0 0 2.5-2.5 C14.5 4.4 11.6 2 8 2z") },
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

    partial void OnPrintbedHeightChanged(float oldValue, float newValue)
    {
        if (oldValue == newValue) { return; }
        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.PrintBedHeightLabel, newValue));
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

    partial void OnUnitsChanged(MeasurementUnit value)
        => _messenger.Send(new AppPreferenceUpdateMessage(UISettings.UnitsLabel, value));

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

        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.DefaultImportFolderLabel, ofd.FolderName));
        var response = _messenger.Send(new AppPreferenceRequestMessage(UISettings.DefaultImportFolderLabel)).Response;
        ImportFilepath = Path.GetFullPath((string)response);
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

        _messenger.Send(new AppPreferenceUpdateMessage(UISettings.DefaultExportFolderLabel, ofd.FolderName));
        var response = _messenger.Send(new AppPreferenceRequestMessage(UISettings.DefaultExportFolderLabel)).Response;
        ExportFilepath = Path.GetFullPath((string)response);
    }

    [RelayCommand]
    private void RestoreDefaults()
    {
        ExportFormat = ExportFormat.Stl;
        PrintbedWidth = 250.0f;
        PrintbedDepth = 250.0f;
        PrintbedHeight = 300.0f;
        ShowBedGrid = true;
        AutodetectChannels = true;
        ChannelDiameter = 4.0f;
        ViewportBackground = ViewportBackground.Graphite;
        Units = MeasurementUnit.Millimeters;
        EnableSplitView = false;
        EnableCutView = false;
    }
}
