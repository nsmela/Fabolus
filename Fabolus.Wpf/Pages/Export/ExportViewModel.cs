using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Meshes;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Extensions;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Mould;
using Fabolus.Wpf.Pages.MainWindow;
using Microsoft.Win32;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Export;

public partial class ExportViewModel : BaseViewModel {
    public override string TitleText => "Export";

    public override SceneManager GetSceneManager => new ExportSceneManager();

    [ObservableProperty] private bool _showBolus = false;
    [ObservableProperty] private bool _showMould = false;

    public ExportViewModel() {
        string mesh_info = string.Empty;

        var bolus = WeakReferenceMessenger.Default.Send<BolusRequestMessage>().Response;
        if (!BolusModel.IsNullOrEmpty(bolus)) {
            ShowBolus = true;
            mesh_info += $"Bolus Volume:\r\n {bolus.Mesh.VolumeString()}";
        }

        var mould = WeakReferenceMessenger.Default.Send<MouldRequestMessage>().Response;
        if (!MouldModel.IsNullOrEmpty(mould)) {
            ShowMould = true;
            mesh_info += $"\r\nMould Volume:\r\n {mould.VolumeString()}";
        }

        WeakReferenceMessenger.Default.Send(new MeshInfoSetMessage(mesh_info));
    }

    //commands
    #region Commands
    [RelayCommand]
    public async Task ExportBolus() {
        var bolus = WeakReferenceMessenger.Default.Send<BolusRequestMessage>().Response;
        if (BolusModel.IsNullOrEmpty(bolus)) { return; }
        
        // get app preference for import folder
        string export_folder = WeakReferenceMessenger.Default.Send(new PreferencesExportFolderRequest()).Response;

        SaveFileDialog saveFile = new() {
            Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*",
            InitialDirectory = export_folder,
        };

        //if successful, create mesh
        if (saveFile.ShowDialog() != true) { return; }

        var filepath = saveFile.FileName;

        await MeshModel.ToFile(filepath, bolus.TransformedMesh());
    }

    [RelayCommand]
    public async Task ExportMould() {
        var mould = WeakReferenceMessenger.Default.Send<MouldRequestMessage>().Response;
        if (MouldModel.IsNullOrEmpty(mould)) { return; }

        // get app preference for import folder
        string export_folder = WeakReferenceMessenger.Default.Send(new PreferencesExportFolderRequest()).Response;

        SaveFileDialog saveFile = new() {
            Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*",
            InitialDirectory = export_folder,
        };

        //if successful, create mesh
        if (saveFile.ShowDialog() != true) { return; }

        var filepath = saveFile.FileName;

        await MeshModel.ToFile(filepath, mould);
    }

    #endregion
}

