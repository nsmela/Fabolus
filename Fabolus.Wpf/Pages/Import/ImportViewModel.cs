using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Extensions;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Pages.MainWindow;
using Microsoft.Win32;
using System.IO;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Import;

public partial class ImportViewModel : BaseViewModel {
    public override string TitleText => "Import";

    private string _filepath = string.Empty;

    protected override void RegisterMessages() { } // no messages to register 

    public ImportViewModel() : base(new SceneManager()) {
        RegisterMessages();
        SetMeshText();
    }

    private void SetMeshText() {
        BolusModel[] boli = Messenger.Send(new AllBolusRequestMessage()).Response;
        if (BolusModel.IsNullOrEmpty(boli)) {
            Messenger.Send(new MeshInfoSetMessage("No bolus loaded."));
            return;
        }

        var filepath = _filepath.Split(Path.DirectorySeparatorChar).LastOrDefault() ?? "Unknown Filepath";
        var text = $"Filepath:\r\n   {filepath}\r\nVolume:\r\n   {boli[0].Mesh.VolumeString()}";
        Messenger.Send(new MeshInfoSetMessage(text));
    }

    // Commands

    [RelayCommand]
    public void ImportFile() {
        // get app preference for import folder
        string import_folder = Messenger.Send(new PreferencesImportFolderRequest()).Response;

        //open file dialog box
        OpenFileDialog openFile = new() {
            Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*",
            Multiselect = false,
            InitialDirectory = import_folder,
        };

        //if successful, create mesh
        if (openFile.ShowDialog() == false) { return; }

        _filepath = openFile.FileName;

        if (string.IsNullOrEmpty(_filepath) ) { return; }

        //send filepath to bolus store to generate a bolus
        Messenger.Send(new AddBolusFromFileMessage(_filepath));
        SetMeshText();
    }

}
