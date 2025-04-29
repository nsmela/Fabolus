using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Scene;
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
        //clear the bolus
        //send message to clear the bolus

        //open file dialog box
        OpenFileDialog openFile = new() {
            Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*",
            Multiselect = false
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
