using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Features.AppPreferences;
using Microsoft.Win32;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Import;

public partial class ImportViewModel : BaseViewModel {
    public override string TitleText => "Import";

    public override SceneManager GetSceneManager => new SceneManager();

    //commands
    #region Commands
    [RelayCommand]
    public async Task ImportFile() {
        // get app preference for import folder
        string import_folder = WeakReferenceMessenger.Default.Send(new PreferencesImportFolderRequest()).Response;

        //open file dialog box
        OpenFileDialog openFile = new() {
            Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*",
            Multiselect = false,
            InitialDirectory = import_folder,
        };

        //if successful, create mesh
        if (openFile.ShowDialog() == false) { return; }

        var filepath = openFile.FileName;

        if (string.IsNullOrEmpty(filepath) ) { return; }

        //send filepath to bolus store to generate a bolus
        WeakReferenceMessenger.Default.Send(new AddBolusFromFileMessage(filepath));
    }

    #endregion
}
