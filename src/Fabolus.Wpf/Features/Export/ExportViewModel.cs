using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.Smoothing;
using Fabolus.Core.Features.Transforms;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Main;
using Fabolus.Wpf.Features.Viewport;

namespace Fabolus.Wpf.Features.Export;

public class OperationItem
{
    public string Name { get; set; }
    public string Value { get; set; }
    public OperationItem(string name, string value) { Name = name; Value = value; }
}

public partial class ExportViewModel : ObservableObject, IViewState
{
    private readonly IMessenger _messenger;
    private readonly IAlertDialog _alert;
    private readonly IGeometryEngine _engine;
    private readonly IDialogueSystem _dialogueSystem;
    private readonly ExportMesh _exportFeature;
    private readonly ExportSceneManager _sceneManager;

    private Workspace Workspace { get; set; } = Workspace.CreateEmpty();

    [ObservableProperty] private bool _is3mfSelected = true;

    partial void OnIs3mfSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsStlSelected));
        ExportButtonText = value ? "Export package" : "Export file";
        FileExtension = value ? ".3mf" : ".stl";
        UpdateInfoPanel();
    }

    public bool IsStlSelected { get => !Is3mfSelected; set => Is3mfSelected = !value; }

    [ObservableProperty] private string _fileName = "bolus_R1";

    partial void OnFileNameChanged(string value)
    {
        UpdateInfoPanel();
    }
    [ObservableProperty] private string _fileExtension = ".3mf";
    [ObservableProperty] private string _destinationFolder;
    [ObservableProperty] private int _fileCount;
    [ObservableProperty] private string _exportButtonText = "Export 0 files";

    [ObservableProperty] private int _bakedOperationsCount;
    [ObservableProperty] private bool _hasBakedOperations;
    [ObservableProperty] private string _bakedOperationsText;
    [ObservableProperty] private string _printableMeshName;

    public ObservableCollection<OperationItem> BakedOperations { get; } = new();

    public ISceneManager SceneManager => _sceneManager;

    public ExportViewModel(IMessenger messenger, IAlertDialog alert, IGeometryEngine engine, IDialogueSystem dialogueSystem)
    {
        _messenger = messenger;
        _alert = alert;
        _engine = engine;
        _dialogueSystem = dialogueSystem;
        _sceneManager = new ExportSceneManager(_engine);
        _exportFeature = new ExportMesh(_engine);

        DestinationFolder = _messenger.Send(new PreferencesExportFolderRequest()).Response;

    }

    public void Activate(Workspace workspace)
    {
        Workspace = workspace;
        _sceneManager.UpdateWorkspace(workspace);

        FileCount = workspace.MeshCount;
        ExportButtonText = Is3mfSelected ? "Export package" : "Export file";

        // Info-panel values come from metadata (the features keep the cached stats fresh) -
        // no need to copy geometry just to display numbers.
        var metadataResult = workspace.GetActiveMeshMetadata();
        if (metadataResult.IsFailure)
        {
            _alert.ShowError(metadataResult.Error.Description);
            _messenger.Send(new UpdateMeshInfoMessage([]));
            return;
        }

        var metadata = metadataResult.Value;
        FileName = metadata.Name;

        BakedOperations.Clear();
        var smoothing = metadata.GetSmoothing();
        if (smoothing.HasValue)
        {
            BakedOperations.Add(new OperationItem("Smoothing", $"{smoothing.Value.Intensity.ToString():F2} mm · {smoothing.Value.Iterations}x"));
        }
        var rotation = metadata.Rotation();
        if (rotation.HasValue)
        {
            BakedOperations.Add(new OperationItem("Rotation", "auto Z-up"));
        }
        var mould = metadata.MouldDefinition();
        if (mould.HasValue)
        {
            string mouldType = mould.Value switch
            {
                ConvexMouldDefinition => "Convex",
                ConcaveMouldDefinition => "Concave",
                ContouredMouldDefinition => "Contoured",
                _ => "2-part"
            };
            BakedOperations.Add(new OperationItem("Mould", mouldType));
        }

        BakedOperationsCount = BakedOperations.Count;
        HasBakedOperations = BakedOperationsCount > 0;
        BakedOperationsText = BakedOperationsCount == 3 ? "All 3 included" : $"{BakedOperationsCount} included";

        if (mould.HasValue)
        {
            PrintableMeshName = "mould";
        }
        else if (smoothing.HasValue)
        {
            PrintableMeshName = "smoothed mesh";
        }
        else
        {
            PrintableMeshName = "base mesh";
        }

        UpdateInfoPanel();
    }

    private void UpdateInfoPanel()
    {
        var activeMeshResult = Workspace.GetActiveMesh();
        if (activeMeshResult.IsFailure)
        {
            _alert.ShowError(activeMeshResult.Error.Description);

            var itemss = new List<MeshInfoItem> {
                new OutputHeaderInfoItem { Label = "OUTPUT", PillText = Is3mfSelected ? "3MF" : "STL" }
            };
            itemss.Add(new TextInfoItem { Label = "Bad metadata" });
            _messenger.Send(new UpdateMeshInfoMessage(itemss));
            return;
        }

        var mesh = activeMeshResult.Value;
        var metadata = mesh.Metadata;

        var items = new List<MeshInfoItem> {
                new OutputHeaderInfoItem { Label = "OUTPUT", PillText = Is3mfSelected ? "3MF" : "STL" }
            };

        string fileSize = "Unknown MB";
        MeshStatistics stats;
        var activeStatsResult = metadata.MeshStats();
        if (activeStatsResult.HasValue)
        {
            stats = activeStatsResult.Value;
            // Roughly estimate file size for display using the final mesh
            double sizeMb = Is3mfSelected ? (stats.TriangleCount * 15.0) / (1024 * 1024) : (stats.TriangleCount * 50.0) / (1024 * 1024);
            if (sizeMb < 0.1)
                sizeMb = 0.1;
            fileSize = $"{sizeMb:F1} MB";
        }

        string filename = $"{FileName}{FileExtension}";

        items.Add(new FileDetailsInfoItem { FileName = filename, FileSize = fileSize });
        items.Add(new SeparatorInfoItem());

        var smoothing = metadata.GetSmoothing();

        if (smoothing.HasValue)
        {
            items.Add(new SubtitleInfoItem { Label = "Measured from smoothed mesh" });
        }
        else
        {
            items.Add(new SubtitleInfoItem { Label = "Measured from base mesh" });
        }

        var transformStageResult = CommandReplay.GetMeshAtStage(_engine, mesh, CommandPriority.Transform);
        stats = transformStageResult.IsSuccess
            ? _engine.Evaluators.GetStatistics(transformStageResult.Value).Value
            : activeStatsResult.Value;

        items.Add(new TextInfoItem { Label = "Volume", Value = $"{(stats.Volume):N1} mL" });
        items.Add(new TextInfoItem { Label = "Surface area", Value = $"{(stats.SurfaceArea / 100):N1} cm²" });
        
        _messenger.Send(new UpdateMeshInfoMessage(items));

    }

    public Workspace Deactivate() => Workspace;

    [RelayCommand]
    public void BrowseDestination()
    {
        var folder = _dialogueSystem.ShowOpenFolderDialogue(DestinationFolder);
        if (folder.HasValue)
        {
            DestinationFolder = folder.Value;
            _messenger.Send(new PreferencesSetExportFolderMessage(DestinationFolder));
        }
    }

    [RelayCommand]
    public void ExportFiles()
    {
        if (Workspace.MeshCount == 0)
        {
            _alert.ShowError("No meshes to export.");
            return;
        }

        if (string.IsNullOrWhiteSpace(DestinationFolder) || !Directory.Exists(DestinationFolder))
        {
            _alert.ShowError("Invalid destination folder.");
            return;
        }

        var activeMeshResult = Workspace.GetActiveMesh();
        if (activeMeshResult.IsFailure)
        {
            _alert.ShowError($"Failed to export: {activeMeshResult.Error.Description}");
            return;
        }

        var mesh = activeMeshResult.Value;
        // Just saving ignoring Binary/ASCII toggle since it's not supported by engine currently
        var filename = $"{FileName}{FileExtension}";
        var filepath = Path.Combine(DestinationFolder, filename);

        var result = _exportFeature.Execute(mesh, filepath, true);
        if (result.IsFailure)
        {
            _alert.ShowError($"Failed to export {filename}: {result.Error.Description}");
            return;
        }

        _alert.ShowInfo($"Successfully exported {filename}.");
    }
}
