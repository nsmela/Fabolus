using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Pages.MainWindow;
using Microsoft.Win32;
using System.IO;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Import;

public partial class ImportViewModel : BaseViewModel {
    public override string TitleText => "Import";

    public override SceneManager GetSceneManager => new SceneManager();

    private string _filepath = string.Empty;

    public ImportViewModel() {
        SetMeshText();
    }

    private void SetMeshText() {
        BolusModel[] boli = WeakReferenceMessenger.Default.Send(new AllBolusRequestMessage()).Response;
        if (boli is null || boli.Length == 0) {
            WeakReferenceMessenger.Default.Send(new MeshInfoSetMessage("No bolus loaded."));
            return;
        }
        var filepath = _filepath.Split(Path.DirectorySeparatorChar).LastOrDefault() ?? "Unknown Filepath";
        var text = $"Filepath:\r\n   {filepath}\r\nVolume:\r\n   {boli[0].VolumeToText}";
        WeakReferenceMessenger.Default.Send(new MeshInfoSetMessage(text));
    }

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

        _filepath = openFile.FileName;

        if (string.IsNullOrEmpty(_filepath) ) { return; }

        //send filepath to bolus store to generate a bolus
        WeakReferenceMessenger.Default.Send(new AddBolusFromFileMessage(_filepath));
        SetMeshText();
    }

    #endregion
}
