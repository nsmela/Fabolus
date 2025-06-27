using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Meshes;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Bolus;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Features.Mould;
using Fabolus.Wpf.Pages.MainWindow;
using Microsoft.Win32;
using static Fabolus.Wpf.Bolus.BolusStore;

namespace Fabolus.Wpf.Pages.Export;

public partial class ExportViewModel : BaseViewModel {
    public override string TitleText => "Export";

    public override SceneManager GetSceneManager => new ExportSceneManager();

    [ObservableProperty] private bool _showBolus;
    [ObservableProperty] private bool _showMould;

    public ExportViewModel() {
        var bolus = WeakReferenceMessenger.Default.Send<BolusRequestMessage>().Response;
        ShowBolus = !BolusModel.IsNullOrEmpty(bolus);

        var mould = WeakReferenceMessenger.Default.Send<MouldRequestMessage>().Response;
        ShowMould = !MouldModel.IsNullOrEmpty(mould);

        WeakReferenceMessenger.Default.Send(new MeshInfoSetMessage("TODO:\r\n bolus volume\r\n mould volume"));
    }

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

        await MeshModel.ToFile(filepath, bolus.Mesh);
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

