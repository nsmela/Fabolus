using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Features.AppPreferences;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
}
