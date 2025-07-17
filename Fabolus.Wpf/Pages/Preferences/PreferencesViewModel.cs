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

    public PreferencesViewModel() {
        ImportFilepath = WeakReferenceMessenger.Default.Send(new PreferencesImportFolderRequest()).Response;
        ExportFilepath = WeakReferenceMessenger.Default.Send(new PreferencesExportFolderRequest()).Response;
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
        ImportFilepath = Path.GetFullPath(response);
    }

}
