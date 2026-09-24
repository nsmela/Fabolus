using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.Viewport;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Wpf.Features.Main;
using SharpDX.DirectWrite;
using Fabolus.Core.Features.MeshIO;

namespace Fabolus.Wpf.Features.MeshManager;

public partial class MeshManagerViewModel : ObservableObject, IViewState {
    private const string FILTER = "3D Models (*.stl;*.3mf;*.obj;*.off;*.ply)|*.stl;*.3mf;*.obj;*.off;*.ply|STL Files (*.stl)|*.stl|3MF Files (*.3mf)|*.3mf|All Files (*.*)|*.*";

    private readonly IDialogueSystem _dialogue;
    private readonly IAlertDialog _alertDialog;
    private readonly IGeometryEngine _engine;
    private readonly IMessenger _messenger;

    private readonly ExportMesh _exportFeature;
    private readonly ImportMesh _importFeature;
    private readonly RepairMesh _repairFeature;
    
    private readonly MeshManagerSceneManager _sceneManager;

    private Workspace Workspace { get; set; }

    public MeshManagerViewModel(IMessenger messenger, IDialogueSystem dialogue, IAlertDialog alertDialog, IGeometryEngine engine) {
        _dialogue = dialogue;
        _alertDialog = alertDialog;
        _engine = engine;
        _messenger = messenger;

        _exportFeature = new ExportMesh(_engine);
        _importFeature = new ImportMesh(_engine);
        _repairFeature = new RepairMesh(_engine);

        _sceneManager = new MeshManagerSceneManager(_engine, _messenger);
    }

    [ObservableProperty] private List<MeshItem> _meshItems = new();
    [ObservableProperty] private MeshItem? _selectedMesh;
    [ObservableProperty] private MeshSelectionState _selectionState = MeshSelectionState.None;

    [ObservableProperty] private MeshRecord? _activeRecord;
    [ObservableProperty] private MeshStatistics? _activeStats;
    [ObservableProperty] private TopologyValidation? _activeTopology;

    partial void OnSelectedMeshChanged(MeshItem? value) {
        Guid newId = value?.Id ?? Guid.Empty;
        Guid oldId = Workspace.ActiveMeshId;
        if (newId == oldId) return; // same mesh

        var result = Workspace.SetActiveMesh(newId);
        if (result.IsFailure) {
            _alertDialog.ShowError(result.Error.Description);
        }

        UpdateWorkspace(result.Value);
    }

    public async Task ActivateAsync(Workspace workspace) {
        await Task.Yield();
        UpdateWorkspace(workspace);
    }

    public Task<Workspace> DeactivateAsync() {
        return Task.FromResult(Workspace);
    }

    public ISceneManager SceneManager => _sceneManager;

    private void UpdateWorkspace(Workspace workspace) {
        Workspace = workspace;

        Guid id = Workspace.ActiveMeshId;

        // Deliberately the field, not the property: the setter publishes a selection change,
        // which would send us straight back round this method. SetActiveMesh below assigns
        // SelectedMesh properly once MeshItems has been rebuilt.
#pragma warning disable MVVMTK0034
        _selectedMesh = null;
#pragma warning restore MVVMTK0034

        // Identity and name come from the record; the topology audit is cached on the geometry,
        // which the workspace hands back without copying.
        MeshItems = Workspace.Records
            .Select(record => new MeshItem(
                record.Id,
                record.Name,
                record.Id == id,
                Workspace.GetMesh(record.Id) is { IsSuccess: true } entry
                    && entry.Value.Topology()?.HasCorruptTopology == true))
            .ToList();

        SetActiveMesh();
        PublishMeshInfo();

        _sceneManager.UpdateWorkspace(workspace);
        _messenger.Send(new WorkspaceChangedMessage(Workspace));
    }

    private void SetActiveMesh() {
        var recordResult = Workspace.GetActiveRecord();

        if (recordResult.IsSuccess) {
            ActiveRecord = recordResult.Value;
            SelectedMesh = MeshItems.FirstOrDefault(x => x.Id == ActiveRecord.Id);

            // The measurements are cached on the geometry by whichever feature last changed it,
            // so filling the info panel still costs no measuring.
            var mesh = Workspace.GetActiveMesh();
            ActiveStats = mesh.IsSuccess ? mesh.Value.Stats() : null;
            ActiveTopology = mesh.IsSuccess ? mesh.Value.Topology() : null;
        } else {
            SelectedMesh = null;
            ActiveRecord = null;
            ActiveStats = null;
            ActiveTopology = null;
        }
    }

    private void PublishMeshInfo() {
        var items = new List<MeshInfoItem>();

        if (ActiveStats is not null) {
            items.Add(new TitleInfoItem { Label = "MESH STATISTICS" });
            items.Add(new TextInfoItem { Label = "Triangles", Value = ActiveStats.TriangleCount.ToString("N0") });
            items.Add(new TextInfoItem { Label = "Surface Area", Value = $"{ActiveStats.SurfaceArea:F2} mm\u00B2" });
            items.Add(new TextInfoItem { Label = "Volume", Value = $"{ActiveStats.Volume:F2} mL" });
            
            double width = ActiveStats.BoundsMax.X - ActiveStats.BoundsMin.X;
            double height = ActiveStats.BoundsMax.Y - ActiveStats.BoundsMin.Y;
            double depth = ActiveStats.BoundsMax.Z - ActiveStats.BoundsMin.Z;
            items.Add(new TextInfoItem { Label = "Dimensions", Value = $"{width:F1} x {height:F1} x {depth:F1} mm" });
        }

        if (ActiveTopology is not null) {
            items.Add(new TitleInfoItem { Label = "MESH TOPOLOGY" });

            bool isManifold = ActiveTopology.NonManifoldEdgeCount == 0;
            items.Add(new StatusInfoItem {
                Label = "Manifold",
                Text = isManifold ? "Yes" : "No",
                Colour = isManifold ? System.Windows.Media.Colors.MediumSeaGreen : System.Windows.Media.Colors.IndianRed
            });
            if (!isManifold) {
                items.Add(new TextInfoItem { Label = "Non-Manifold", Value = ActiveTopology.NonManifoldEdgeCount.ToString("N0") });
            }

            bool isWaterTight = ActiveTopology.IsWatertight;
            items.Add(new StatusInfoItem {
                Label = "WaterTight",
                Text = isWaterTight ? "Yes" : "No",
                Colour = isWaterTight ? System.Windows.Media.Colors.MediumSeaGreen : System.Windows.Media.Colors.IndianRed
            });

            bool hasOrphanedVertices = false;
            items.Add(new StatusInfoItem {
                Label = "Orphaned Vertices",
                Text = hasOrphanedVertices ? "Yes" : "No",
                Colour = !hasOrphanedVertices ? System.Windows.Media.Colors.MediumSeaGreen : System.Windows.Media.Colors.IndianRed
            });

            bool hasDegenerateTriangles = ActiveTopology.DegenerateTriangleCount > 0;
            items.Add(new StatusInfoItem {
                Label = "Degenerate Triangles",
                Text = hasDegenerateTriangles ? "Yes" : "No",
                Colour = !hasDegenerateTriangles ? System.Windows.Media.Colors.MediumSeaGreen : System.Windows.Media.Colors.IndianRed
            });

            bool hasSelfInterectingTriangles = false;
            items.Add(new StatusInfoItem {
                Label = "Is Self-Intersecting",
                Text = hasSelfInterectingTriangles ? "Yes" : "No",
                Colour = !hasSelfInterectingTriangles ? System.Windows.Media.Colors.MediumSeaGreen : System.Windows.Media.Colors.IndianRed
            });
            if (hasSelfInterectingTriangles) {
                
            }
        }

        _messenger.Send(new UpdateMeshInfoMessage(items));
    }

    [RelayCommand]
    public async Task ImportFileAsync() {
        var openFileResult = _dialogue.ShowOpenFileDialog(FILTER);

        if (openFileResult.HasNoValue) return;

        _messenger.Send(new IsLoadingMessage(true));

        var result = await Task.Run(() => _importFeature.Execute(Workspace, openFileResult.Value));

        if (result.IsFailure) {
            _alertDialog.ShowError(result.Error.Description);
            _messenger.Send(new IsLoadingMessage(false));
            return;
        }

        UpdateWorkspace(result.Value);

        _messenger.Send(new IsLoadingMessage(false));
    }

    [RelayCommand]
    public void RepairMesh(Guid id) {

        var result = _repairFeature.Execute(Workspace, id);
        if (result.IsFailure) {
            _alertDialog.ShowError(result.Error.Description);
            return;
        }

        UpdateWorkspace(result.Value);
    }

    [RelayCommand]
    public void DeleteMesh(Guid id) {
        var result = Workspace.RemoveMesh(id);

        if (result.IsFailure) {
            _alertDialog.ShowError(result.Error.Description);
            return;
        }

        UpdateWorkspace(result.Value);
    }

    [RelayCommand]
    public void ExportMesh(Guid id) {
        var saveFileResult = _dialogue.ShowSaveFileDialog(FILTER, ".stl");
        if (saveFileResult.HasNoValue) return;

        var meshResult = Workspace.GetMesh(id);
        if (meshResult.IsFailure) {
            _alertDialog.ShowError(meshResult.Error.Description);
            return;
        }

        var recordResult = Workspace.GetRecord(id);
        if (recordResult.IsFailure) {
            _alertDialog.ShowError(recordResult.Error.Description);
            return;
        }

        var mesh = meshResult.Value;
        var result = _exportFeature.Execute(mesh, recordResult.Value, saveFileResult.Value, true);
        if (result.IsFailure) {
            _alertDialog.ShowError(result.Error.Description);
        }
    }
}

public sealed record MeshItem(
    Guid Id,
    string Name,
    bool IsActive,
    bool HasCorruptTopology
);

public enum MeshSelectionState {
    None,
    Evaluating,
    Loaded
}
