using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Meshes;
using Fabolus.Core.Meshes.MeshTools;
using Fabolus.Core.Meshes.PartingTools;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Features.Mould;
using Microsoft.Win32;
using System.IO;
using System.Numerics;
using System.Windows;
using static Fabolus.Wpf.Bolus.BolusStore;


namespace Fabolus.Wpf.Pages.Split;

public partial class SplitViewModel : BaseViewModel {
    public override string TitleText => "split";

    // view meshes
    [ObservableProperty] private bool _showBolus = true;
    [ObservableProperty] private bool _showCurves = false;
    [ObservableProperty] private bool _showNegativeParting = false;
    [ObservableProperty] private bool _showPositiveParting = false;
    [ObservableProperty] private bool _showPullRegions = true;
    [ObservableProperty] private bool _showPartingLine = true;
    [ObservableProperty] private bool _showPartingMesh;
    [ObservableProperty] private bool _explodePartingMeshes = true;

    partial void OnShowBolusChanged(bool value) {
        if (value) { _showCurves = false; }
        UpdateViewOptions(); 
    }

    partial void OnShowCurvesChanged(bool value) {
        if (value) { _showBolus = false; }
        UpdateViewOptions();
    }

    partial void OnShowNegativePartingChanged(bool value) { UpdateViewOptions(); }
    partial void OnShowPositivePartingChanged(bool value) { UpdateViewOptions(); }
    partial void OnShowPullRegionsChanged(bool value) { UpdateViewOptions(); }
    partial void OnShowPartingLineChanged(bool value) { UpdateViewOptions(); }
    partial void OnShowPartingMeshChanged(bool value) { UpdateViewOptions(); }
    partial void OnExplodePartingMeshesChanged(bool value) { UpdateViewOptions(); }

    private void UpdateViewOptions() => _messenger.Send(new UpdateSplitViewOptionsMessage(ViewOptions));

    // split settings
    [ObservableProperty] private float _draftTolerance = 10.0f;
    [ObservableProperty] private int _outerDistance = 20;
    [ObservableProperty] private float _innerDistance = 1.0f;
    [ObservableProperty] private float _gapDistance = 0.1f;

    partial void OnDraftToleranceChanged(float oldValue, float newValue) {
        if (oldValue == newValue) { return; }
        UpdateSettings();
    }

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

    private void UpdateSettings() {
        var settings = CuttingSettings;
        _messenger.Send(new SplitSettingsMessage(settings));

        _results = GeneratePreview();
        _messenger.Send(new SplitResultsMessage(_results));
    }

    private SplitViewOptions ViewOptions => new SplitViewOptions(
            ShowBolus,
            ShowCurves,
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
        };

    private MeshModel _bolus;
    private MeshModel _mould;
    private CuttingMeshResults _results;
    private List<int[]> _path_indices = [];
    private PartingTool _partingTool;

    protected override void RegisterMessages() {
        _messenger.Register<SplitViewModel, SplitRequestViewOptionsMessage>(this, (r, m) => m.Reply(ViewOptions));
        _messenger.Register<SplitViewModel, SplitRequestSettingsMessage>(this, (r, m) => m.Reply(CuttingSettings));
        _messenger.Register<SplitViewModel, SplitRequestResultsMessage>(this, (r, m) => m.Reply(GeneratePreview()));
        _messenger.Register<SplitViewModel, SplitPartingToolUpdatedMessage>(this, (r, m) => PartingToolUpdated(m.tool));
    }

    public SplitViewModel() : base(new SplitSceneManager()) {
        RegisterMessages();

        var bolus = _messenger.Send(new BolusRequestMessage()).Response;
        _bolus = bolus.TransformedMesh();
        _partingTool = new PartingTool(_bolus, [1]);

        var mould = _messenger.Send(new MouldRequestMessage()).Response;

        if (MouldModel.IsNullOrEmpty(mould)) {
            _mould = new();
            ShowErrorMessage("Splitting View Init Error", "A valid mould is required to split the bolus.");
            return;
        }

        _mould = mould;

        // send data to the SceneManager
        UpdateViewOptions();
        UpdateSettings(); 
    }

    private CuttingMeshResults GeneratePreview() {
        var draft_regions = DraftRegions.GenerateDraftMeshes(_bolus, Vector3.UnitY, DraftTolerance);
        _path_indices = PartingTools.PartingPathIndices(draft_regions[DraftRegions.DraftRegionClassification.Positive]);
        _path_indices[0] = _partingTool.PathIndexes;

        _results = PartingTools.GeneratePartingMesh(
            _bolus,
            _path_indices,
            InnerDistance,
            OuterDistance,
            GapDistance);

        _results.Mould = _mould;
        _results.DraftRegions = draft_regions;

        var intersection_response = MeshTools.Intersections(_mould, _results.CuttingMesh);

        if (intersection_response.IsSuccess) {
            _results.Intersections = intersection_response.Data;
        }

        return _results;
    }

    private void PartingToolUpdated(PartingTool tool) {
        _partingTool = tool;
        UpdateSettings();
    }

    // commands

    [RelayCommand]
    private void Generate() {
        // trying to keep all boolean operations within MR.Dotnet framework
        var response = PartingTools.PartModel(_results);

        if (response.IsFailure) {
            var errors = response.Errors.Select(e => e.ErrorMessage).ToArray();
            ShowErrorMessage("Generate failed", string.Join(Environment.NewLine, errors));
            _messenger.Send(new SplitResultsMessage(_results));
            return;
        }

        _results = response.Data;

        // change view
        ShowBolus = false;
        ShowNegativeParting = true;
        ShowPositiveParting = true;
        ShowPartingLine = false;
        ShowPartingMesh = false;
        ShowPullRegions = false;
        ExplodePartingMeshes = true;

        _messenger.Send(new SplitResultsMessage(_results));
    }

    [RelayCommand]
    private async Task ExportCuttingMesh() {
        // trying to keep all boolean operations within MR.Dotnet framework
        MeshModel mesh = _results.CuttingMesh;

        if (mesh.IsEmpty()) {
            ShowErrorMessage("Export Cutting Mesh Error", "Cutting mesh is empty!");
            return;
        }

        SaveFileDialog saveFile = new() {
            Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*"
        };

        //if successful, create mesh
        if (saveFile.ShowDialog() != true) { return; }

        await MeshModel.ToFile(saveFile.FileName, mesh);
    }

    [RelayCommand]
    private async Task ExportSeperate() {

        SaveFileDialog saveFile = new() {
            Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*"
        };

        //if successful, create mesh
        if (saveFile.ShowDialog() != true) { return; }

        var folder = Path.GetDirectoryName(saveFile.FileName);
        var filename = Path.GetFileNameWithoutExtension(saveFile.FileName);
        var filetype = Path.GetExtension(saveFile.FileName);

        string path = string.Empty;

        MeshModel[] models = [_results.NegativePullMesh, _results.PositivePullMesh];

        for (int i = 0; i < models.Length; i++) {
            if (MeshModel.IsNullOrEmpty(models[i])) { continue; } // skip empty models
            path = Path.Combine(folder, $"{filename}0{i}{filetype}");
            await MeshModel.ToFile(path, models[i]);
        }
    }

    [RelayCommand]
    private async Task ExportJoined() {
        SaveFileDialog saveFile = new() {
            Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*"
        };

        //if successful, create mesh
        if (saveFile.ShowDialog() != true) { return; }

        // saving both models in a single STL file with a small gap between them
        // copying the meshes to ensure they dont modify the originals
        MeshModel negative_parting_model = MeshModel.Copy(_results.NegativePullMesh);
        MeshModel positive_parting_model = MeshModel.Copy(_results.PositivePullMesh);
        positive_parting_model.ApplyTranslation(0, GapDistance, 0); // move to create gap

        MeshModel combinedModel = MeshModel.Combine([
            negative_parting_model,
            positive_parting_model,
        ]);

        await MeshModel.ToFile(saveFile.FileName, combinedModel);
    }


}
