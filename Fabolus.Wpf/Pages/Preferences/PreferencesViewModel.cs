using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Features.AppPreferences;
using Microsoft.Win32;
using System.IO;

namespace Fabolus.Wpf.Pages.Preferences;

public partial class PreferencesViewModel : ObservableObject {
    [ObservableProperty] private string _importFilepath;
    [ObservableProperty] private string _exportFilepath;
    [ObservableProperty] private float _printbedWidth;
    [ObservableProperty] private float _printbedDepth;

    partial void OnPrintbedWidthChanged(float oldValue, float newValue) {
        if (oldValue == newValue) { return; }
        WeakReferenceMessenger.Default.Send(new PreferencesSetPrintbedWidthMessage(newValue));
    }

    partial void OnPrintbedDepthChanged(float oldValue, float newValue) {
        if (oldValue == newValue) { return; }
        WeakReferenceMessenger.Default.Send(new PreferencesSetPrintbedDepthMessage(newValue));

    }

    public PreferencesViewModel() {
        _importFilepath = WeakReferenceMessenger.Default.Send<PreferencesImportFolderRequest>().Response;
        _exportFilepath = WeakReferenceMessenger.Default.Send<PreferencesExportFolderRequest>().Response;
        _printbedWidth = WeakReferenceMessenger.Default.Send<PreferencesPrintbedWidthRequest>().Response;
        _printbedDepth = WeakReferenceMessenger.Default.Send<PreferencesPrintbedDepthRequest>().Response;
    }

    [RelayCommand]
    private async Task SetImportFolder() {
        OpenFolderDialog ofd = new() {
            InitialDirectory = ImportFilepath,
            Title = "Select Import Folder",
            Multiselect = false
        };

        var result = ofd.ShowDialog();
        if (!result.HasValue || !result.Value) { return; }

        WeakReferenceMessenger.Default.Send(new PreferencesSetImportFolderMessage(ofd.FolderName));
        var response = WeakReferenceMessenger.Default.Send<PreferencesImportFolderRequest>().Response;
        ImportFilepath = Path.GetFullPath(response);
    }

    [RelayCommand]
    private async Task SetExportFolder() {
        OpenFolderDialog ofd = new() {
            InitialDirectory = ExportFilepath,
            Title = "Select Export Folder",
            Multiselect = false
        };

        var result = ofd.ShowDialog();
        if (!result.HasValue || !result.Value) { return; }

        WeakReferenceMessenger.Default.Send(new PreferencesSetExportFolderMessage(ofd.FolderName));
        var response = WeakReferenceMessenger.Default.Send<PreferencesExportFolderRequest>().Response;
        ExportFilepath = Path.GetFullPath(response);
    }

}
