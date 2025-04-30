using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Meshes;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Features.Mould;
using Microsoft.Win32;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Export;

public partial class ExportViewModel : BaseViewModel {
    public override string TitleText => "Export";

    public override SceneManager GetSceneManager => new SceneManager();

    //commands
    #region Commands
    [RelayCommand]
    public async Task ExportBolus() {
        var bolus = WeakReferenceMessenger.Default.Send<BolusRequestMessage>().Response;
        if (BolusModel.IsNullOrEmpty(bolus)) { return; }

        SaveFileDialog saveFile = new() {
            Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*"
        };

        //if successful, create mesh
        if (saveFile.ShowDialog() != true) { return; }

        var filepath = saveFile.FileName;

        //var mesh = bolus.Geometry.ToDMesh();
        //StandardMeshWriter.WriteMesh(filepath, mesh, WriteOptions.Defaults);
    }

    [RelayCommand]
    public async Task ExportMould() {
        var mould = WeakReferenceMessenger.Default.Send<MouldRequestMessage>().Response;
        if (MouldModel.IsNullOrEmpty(mould)) { return; }

        SaveFileDialog saveFile = new() {
            Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*"
        };

        //if successful, create mesh
        if (saveFile.ShowDialog() != true) { return; }

        var filepath = saveFile.FileName;

        await MeshModel.ToFile(filepath, mould);
    }

    #endregion
}

