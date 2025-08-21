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

// messages
public sealed record ExportShowBolusMessage(bool ShowBolus);
public sealed record ExportShowMouldMessage(bool ShowMould);

public partial class ExportViewModel : BaseViewModel {
    public override string TitleText => "Export";

    [ObservableProperty] private bool _hasBolus = false;
    [ObservableProperty] private bool _hasMould = false;

    protected override void RegisterMessages() { }

    public ExportViewModel() : base(new ExportSceneManager()) {
        string mesh_info = string.Empty;

        var bolus = _messenger.Send<BolusRequestMessage>().Response;
        if (!BolusModel.IsNullOrEmpty(bolus)) {
            HasBolus = true;
            mesh_info += $"Bolus Volume:\r\n {bolus.Mesh.VolumeString()}";
        }

        var mould = _messenger.Send<MouldRequestMessage>().Response;
        if (!MouldModel.IsNullOrEmpty(mould)) {
            HasMould = true;
            mesh_info += $"\r\nMould Volume:\r\n {mould.VolumeString()}";
        }

        _messenger.Send(new MeshInfoSetMessage(mesh_info));
    }

    // commands
    [RelayCommand]
    public async Task ExportBolus() {
        var bolus = _messenger.Send<BolusRequestMessage>().Response;
        if (BolusModel.IsNullOrEmpty(bolus)) { return; }
        
        // get app preference for import folder
        string export_folder = _messenger.Send(new PreferencesExportFolderRequest()).Response;

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
        var mould = _messenger.Send<MouldRequestMessage>().Response;
        if (MouldModel.IsNullOrEmpty(mould)) { return; }

        // get app preference for import folder
        string export_folder = _messenger.Send(new PreferencesExportFolderRequest()).Response;

        SaveFileDialog saveFile = new() {
            Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*",
            InitialDirectory = export_folder,
        };

        //if successful, create mesh
        if (saveFile.ShowDialog() != true) { return; }

        var filepath = saveFile.FileName;

        await MeshModel.ToFile(filepath, mould);
    }

    [RelayCommand] public void ShowBolus() => _messenger.Send(new ExportShowBolusMessage(true));
    [RelayCommand] public void HideBolus() => _messenger.Send(new ExportShowBolusMessage(false));
    [RelayCommand] public void ShowMould() => _messenger.Send(new ExportShowMouldMessage(true));
    [RelayCommand] public void HideMould() => _messenger.Send(new ExportShowMouldMessage(false));

}

