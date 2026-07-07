using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Viewport;
using System;
using System.IO;
using System.Collections.ObjectModel;
using Fabolus.Core.Features.Transforms;
using Fabolus.Core.Features.Smoothing;
using Fabolus.Core.Features.Moulds;

namespace Fabolus.Wpf.Features.Export;

public class OperationItem {
    public string Name { get; set; }
    public string Value { get; set; }
    public OperationItem(string name, string value) { Name = name; Value = value; }
}

public partial class ExportViewModel : ObservableObject, IViewState {
    private readonly IMessenger _messenger;
    private readonly IAlertDialog _alert;
    private readonly IGeometryEngine _engine;
    private readonly IDialogueSystem _dialogueSystem;
    private readonly ExportMesh _exportFeature;
    private readonly ExportSceneManager _sceneManager;

    private Workspace Workspace { get; set; } = Workspace.CreateEmpty();

    [ObservableProperty] private bool _is3mfSelected = true;

    partial void OnIs3mfSelectedChanged(bool value) {
        OnPropertyChanged(nameof(IsStlSelected));
        ExportButtonText = value ? "Export package" : $"Export {FileCount} {(FileCount == 1 ? "file" : "files")}";
        FileExtension = value ? ".3mf" : ".stl";
    }

    public bool IsStlSelected {
        get => !Is3mfSelected;
        set {
            if (value) Is3mfSelected = false;
        }
    }

    [ObservableProperty] private string _fileName = "bolus_R1";
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

    public ExportViewModel(IMessenger messenger, IAlertDialog alert, IGeometryEngine engine, IDialogueSystem dialogueSystem) {
        _messenger = messenger;
        _alert = alert;
        _engine = engine;
        _dialogueSystem = dialogueSystem;
        _sceneManager = new ExportSceneManager(_engine);
        _exportFeature = new ExportMesh(_engine);

        DestinationFolder = _messenger.Send(new PreferencesExportFolderRequest()).Response;
        if (string.IsNullOrWhiteSpace(DestinationFolder)) {
            DestinationFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }

    public void Activate(Workspace workspace) {
        Workspace = workspace;
        _sceneManager.UpdateWorkspace(workspace);
        
        FileCount = workspace.MeshCount;
        ExportButtonText = Is3mfSelected ? "Export package" : $"Export {FileCount} {(FileCount == 1 ? "file" : "files")}";

        // Info-panel values come from metadata (the features keep the cached stats fresh) -
        // no need to copy geometry just to display numbers.
        var metadataResult = workspace.GetActiveMeshMetadata();
        if (metadataResult.IsSuccess) {
            var metadata = metadataResult.Value;
            FileName = metadata.Name;
            
            BakedOperations.Clear();
            var smoothing = metadata.GetSmoothing();
            if (smoothing.HasValue) {
                BakedOperations.Add(new OperationItem("Smoothing", $"Poisson · {smoothing.Value.Iterations}"));
            }
            var rotation = metadata.Rotation();
            if (rotation.HasValue) {
                BakedOperations.Add(new OperationItem("Rotation", "auto Z-up"));
            }
            var mould = metadata.MouldDefinition();
            if (mould.HasValue) {
                string mouldType = mould.Value switch {
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
            
            if (mould.HasValue) {
                PrintableMeshName = "mould";
            } else if (smoothing.HasValue) {
                PrintableMeshName = "smoothed mesh";
            } else {
                PrintableMeshName = "base mesh";
            }
            
            var items = new System.Collections.Generic.List<Fabolus.Wpf.Features.Main.MeshInfoItem> {
                new Fabolus.Wpf.Features.Main.TitleInfoItem { Label = metadata.Name }
            };

            var statsResult = metadata.MeshStats();
            if (statsResult.HasValue) {
                var stats = statsResult.Value;
                items.Add(new Fabolus.Wpf.Features.Main.TextInfoItem { Label = "Volume", Value = $"{stats.Volume:N2} mm³" });
                items.Add(new Fabolus.Wpf.Features.Main.TextInfoItem { Label = "Surface Area", Value = $"{stats.SurfaceArea:N2} mm²" });
            }

            _messenger.Send(new Fabolus.Wpf.Features.Main.UpdateMeshInfoMessage(items));
        } else {
            _messenger.Send(new Fabolus.Wpf.Features.Main.UpdateMeshInfoMessage([]));
        }
    }

    public Workspace Deactivate() => Workspace;

    [RelayCommand]
    public void BrowseDestination() {
        var folder = _dialogueSystem.ShowOpenFolderDialogue(DestinationFolder);
        if (folder.HasValue) {
            DestinationFolder = folder.Value;
            _messenger.Send(new PreferencesSetExportFolderMessage(DestinationFolder));
        }
    }

    [RelayCommand]
    public void ExportFiles() {
        if (Workspace.MeshCount == 0) {
            _alert.ShowError("No meshes to export.");
            return;
        }

        if (string.IsNullOrWhiteSpace(DestinationFolder) || !Directory.Exists(DestinationFolder)) {
            _alert.ShowError("Invalid destination folder.");
            return;
        }

        foreach (var metadata in Workspace.MeshMetadataList) {
            var meshResult = Workspace.GetMesh(metadata.Id);
            if (meshResult.IsFailure) {
                _alert.ShowError($"Failed to export {metadata.Name}: {meshResult.Error.Description}");
                return;
            }

            var mesh = meshResult.Value;
            // Just saving ignoring Binary/ASCII toggle since it's not supported by engine currently
            var filename = Workspace.MeshCount > 1 && !Is3mfSelected ? $"{FileName}_{metadata.Name}{FileExtension}" : $"{FileName}{FileExtension}";
            var filepath = Path.Combine(DestinationFolder, filename);

            var result = _exportFeature.Execute(mesh, filepath, true);
            if (result.IsFailure) {
                _alert.ShowError($"Failed to export {filename}: {result.Error.Description}");
                return;
            }
        }

        _alert.ShowInfo($"Successfully exported {Workspace.MeshCount} files.");
    }
}
