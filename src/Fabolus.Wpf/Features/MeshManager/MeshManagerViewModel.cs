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
using static MR;

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
    private string ImportFolder { get; set; }

    public MeshManagerViewModel(IMessenger messenger, IDialogueSystem dialogue, IAlertDialog alertDialog, IGeometryEngine engine) {
        _dialogue = dialogue;
        _alertDialog = alertDialog;
        _engine = engine;
        _messenger = messenger;

        _exportFeature = new ExportMesh(_engine);
        _importFeature = new ImportMesh(_engine);
        _repairFeature = new RepairMesh(_engine);

        _sceneManager = new MeshManagerSceneManager(_engine);
        //ImportFolder = _messenger.Send(new PreferencesImportFolderRequest()).Response;
    }

    [ObservableProperty] private List<MeshItem> _meshItems = new();
    [ObservableProperty] private MeshItem? _selectedMesh;
    [ObservableProperty] private MeshSelectionState _selectionState = MeshSelectionState.None;

    [ObservableProperty] private MeshMetadata? _activeMetadata;
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

    public void Activate(Workspace workspace) => UpdateWorkspace(workspace);

    public Workspace Deactivate() {
        return Workspace;
    }

    public ISceneManager SceneManager => _sceneManager;

    private void UpdateWorkspace(Workspace workspace) {
        Workspace = workspace;

        Guid id = Workspace.ActiveMeshId;
        Guid _selectedId = SelectedMesh?.Id ?? Guid.Empty;
        _selectedMesh = null; // to prevent triggering a workspace update again


        MeshItems = Workspace.MeshMetadataList
            .Select(metadata => new MeshItem(
                metadata.Id,
                metadata.Name,
                metadata.Id == id,
                metadata.Topology().Value.IsNotValid))
            .ToList();

        SetActiveMesh();
        PublishMeshInfo();

        _sceneManager.UpdateWorkspace(workspace);
        _messenger.Send(new WorkspaceChangedMessage(Workspace));
    }

    private void SetActiveMesh() {
        // Metadata-only read - no geometry copy needed to fill the info panel.
        var metadataResult = Workspace.GetActiveMeshMetadata();

        if (metadataResult.IsSuccess) {
            ActiveMetadata = metadataResult.Value;
            SelectedMesh = MeshItems.FirstOrDefault(x => x.Id == ActiveMetadata.Id);
            ActiveStats = ActiveMetadata.MeshStats().Value;
            ActiveTopology = ActiveMetadata.Topology().Value;
        } else {
            SelectedMesh = null;
            ActiveMetadata = null;
            ActiveStats = null;
            ActiveTopology = null;
        }

    }

    private void PublishMeshInfo() {
        var items = new List<MeshInfoItem>();

        if (ActiveStats != null) {
            items.Add(new TitleInfoItem { Label = "MESH STATISTICS" });
            items.Add(new TextInfoItem { Label = "Triangles", Value = ActiveStats.TriangleCount.ToString("N0") });
            items.Add(new TextInfoItem { Label = "Surface Area", Value = $"{ActiveStats.SurfaceArea:F2} mm�" });
            items.Add(new TextInfoItem { Label = "Volume", Value = $"{ActiveStats.Volume:F2} mL" });
            
            double width = ActiveStats.MaxX - ActiveStats.MinX;
            double height = ActiveStats.MaxY - ActiveStats.MinY;
            double depth = ActiveStats.MaxZ - ActiveStats.MinZ;
            items.Add(new TextInfoItem { Label = "Dimensions", Value = $"{width:F1} x {height:F1} x {depth:F1} mm" });
        }

        if (ActiveTopology != null) {
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

            bool hasOrphanedVertices = ActiveTopology.HasOrphanedVertices;
            items.Add(new StatusInfoItem {
                Label = "Orphaned Vertices",
                Text = hasOrphanedVertices ? "Yes" : "No",
                Colour = !hasOrphanedVertices ? System.Windows.Media.Colors.MediumSeaGreen : System.Windows.Media.Colors.IndianRed
            });

            bool hasDegenerateTriangles = ActiveTopology.HasDegenerateTriangles;
            items.Add(new StatusInfoItem {
                Label = "Degenerate Triangles",
                Text = hasDegenerateTriangles ? "Yes" : "No",
                Colour = !hasDegenerateTriangles ? System.Windows.Media.Colors.MediumSeaGreen : System.Windows.Media.Colors.IndianRed
            });

            bool hasSelfInterectingTriangles = ActiveTopology.SelfIntersectionCount > 0;
            items.Add(new StatusInfoItem {
                Label = "Is Self-Intersecting",
                Text = hasSelfInterectingTriangles ? "Yes" : "No",
                Colour = !hasSelfInterectingTriangles ? System.Windows.Media.Colors.MediumSeaGreen : System.Windows.Media.Colors.IndianRed
            });
            if (hasSelfInterectingTriangles) {
                items.Add(new TextInfoItem { Label = "Self-Intersecting Triangles", Value = ActiveTopology.SelfIntersectionCount.ToString("N0") });
            }
        }

        _messenger.Send(new UpdateMeshInfoMessage(items));
    }

    [RelayCommand]
    public void ImportFile() {
        var openFileResult = _dialogue.ShowOpenFileDialog(FILTER);

        if (openFileResult.HasNoValue) return;

        var result = _importFeature.Execute(Workspace, openFileResult.Value);

        if (result.IsFailure) {
            _alertDialog.ShowError(result.Error.Description);
            return;
        }

        UpdateWorkspace(result.Value);
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

        var mesh = meshResult.Value;
        var result = _exportFeature.Execute(mesh, saveFileResult.Value, true);
        if (result.IsFailure) {
            _alertDialog.ShowError(result.Error.Description);
        }
    }
}

public sealed record MeshItem(
    Guid Id,
    string Name,
    bool IsActive,
    bool IsNotValid
);

public enum MeshSelectionState {
    None,
    Evaluating,
    Loaded
}
