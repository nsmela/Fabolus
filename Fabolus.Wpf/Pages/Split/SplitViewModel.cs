using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Meshes;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Features.Mould;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using static Fabolus.Core.Meshes.PartingTools.PartingTools;


namespace Fabolus.Wpf.Pages.Split;

public partial class SplitViewModel : BaseViewModel {
    public override string TitleText => "split";

    public override SceneManager GetSceneManager => new SplitSceneManager();

    [ObservableProperty] private float _seperationDistance = 0.3f;
    partial void OnSeperationDistanceChanged(float oldValue, float newValue)
    {
        if (oldValue == newValue) { return; }
        WeakReferenceMessenger.Default.Send(new SplitSeperationDistanceMessage(newValue));
    }

    // view meshes
    [ObservableProperty] private bool _showBolus = true;
    [ObservableProperty] private bool _showNegativeParting = false;
    [ObservableProperty] private bool _showPositiveParting = false;
    [ObservableProperty] private bool _showPullRegions = true;
    [ObservableProperty] private bool _showPartingLine = true;
    [ObservableProperty] private bool _showPartingMesh;
    [ObservableProperty] private bool _explodePartingMeshes = true;

    partial void OnShowBolusChanged(bool value) { UpdateViewOptions(); }
    partial void OnShowNegativePartingChanged(bool value) { UpdateViewOptions(); }
    partial void OnShowPositivePartingChanged(bool value) { UpdateViewOptions(); }
    partial void OnShowPullRegionsChanged(bool value) { UpdateViewOptions(); }
    partial void OnShowPartingLineChanged(bool value) { UpdateViewOptions(); }
    partial void OnShowPartingMeshChanged(bool value) { UpdateViewOptions(); }
    partial void OnExplodePartingMeshesChanged(bool value) { UpdateViewOptions(); }

    private void UpdateViewOptions() {
        WeakReferenceMessenger.Default.Send(new UpdateSplitViewOptionsMessage(ViewOptions));
    }

    // split settings
    [ObservableProperty] private int _outerDistance = 20;
    [ObservableProperty] private float _innerDistance = 1.0f;
    [ObservableProperty] private float _gapDistance = 0.1f;
    [ObservableProperty] private float _twistThreshold = 90.0f;

    partial void OnOuterDistanceChanged(int oldValue, int newValue) {
        if (oldValue ==  newValue) { return; }
        UpdateSettings();
    }

    partial void OnInnerDistanceChanged(float oldValue, float newValue) {
        if (oldValue == newValue) { return; }
        UpdateSettings();
    }

    partial void OnGapDistanceChanged(float oldValue, float newValue) {
        if (oldValue == newValue) { return; }
        UpdateSettings();
    }

    partial void OnTwistThresholdChanged(float oldValue, float newValue) {
        if (oldValue == newValue) { return; }
        UpdateSettings();
    }

    private void UpdateSettings() {
        var settings = CuttingSettings;
        WeakReferenceMessenger.Default.Send(new SplitSettingsMessage(settings));
    }

    private SplitViewOptions ViewOptions => new SplitViewOptions(
            ShowBolus,
            ShowNegativeParting,
            ShowPositiveParting,
            ShowPullRegions,
            ShowPartingLine,
            ShowPartingMesh,
            ExplodePartingMeshes
        );

    private CuttingMeshParams CuttingSettings => new() {
            OuterOffset = OuterDistance,
            InnerOffset = InnerDistance,
            MeshDepth = GapDistance,
            TwistThreshold = 1 - (TwistThreshold / 90.0) // 180 = 2.0, 90 = 1.0, 0 = 0.0
        };

    public SplitViewModel() {
        WeakReferenceMessenger.Default.Register<SplitViewModel, SplitRequestViewOptionsMessage>(this, (r, m) => m.Reply(ViewOptions));
        WeakReferenceMessenger.Default.Register<SplitViewModel, SplitRequestSettingsMessage>(this, (r, m) => m.Reply(CuttingSettings));
    }

    // commands

    [RelayCommand]
    private async Task Generate() {
        var settings = CuttingSettings;
        WeakReferenceMessenger.Default.Send(new SplitSettingsMessage(settings));
    }

    [RelayCommand]
    private async Task ExportSeperate() {
        var models = WeakReferenceMessenger.Default.Send(new SplitRequestModelsMessage()).Response;
        if (models is null || models.Length == 0) { return; }

        SaveFileDialog saveFile = new() {
            Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*"
        };

        //if successful, create mesh
        if (saveFile.ShowDialog() != true) { return; }

        var folder = Path.GetDirectoryName(saveFile.FileName);
        var filename = Path.GetFileNameWithoutExtension(saveFile.FileName);
        var filetype = Path.GetExtension(saveFile.FileName);

        string path = string.Empty;
        for (int i = 0; i < models.Length; i++) {
            if (MeshModel.IsNullOrEmpty(models[i])) { continue; } // skip empty models
            path = Path.Combine(folder, $"{filename}0{i}{filetype}");
            await MeshModel.ToFile(path, models[i]);
        }
    }

    [RelayCommand]
    private async Task ExportJoined() {
        var models = WeakReferenceMessenger.Default.Send(new SplitRequestModelsMessage()).Response;
        if (models is null || models.Length == 0) { return; }

        SaveFileDialog saveFile = new() {
            Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*"
        };

        //if successful, create mesh
        if (saveFile.ShowDialog() != true) { return; }

        // saving both models in a single STL file with a small gap between them
        // copying the meshes to ensure they dont modify the originals
        MeshModel negative_parting_model = MeshModel.Copy(models[0]);
        MeshModel positive_parting_model = MeshModel.Copy(models[1]);
        positive_parting_model.ApplyTranslation(0, SeperationDistance, 0); // move to create gap

        MeshModel combinedModel = MeshModel.Combine([
            negative_parting_model,
            positive_parting_model,
        ]);

        await MeshModel.ToFile(saveFile.FileName, combinedModel);
    }
}
