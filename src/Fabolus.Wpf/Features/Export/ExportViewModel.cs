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

namespace Fabolus.Wpf.Features.Export;

public partial class ExportViewModel : ObservableObject, IViewState {
    private readonly IMessenger _messenger;
    private readonly IAlertDialog _alert;
    private readonly IGeometryEngine _engine;
    private readonly IDialogueSystem _dialogueSystem;
    private readonly ExportMesh _exportFeature;
    private readonly ExportSceneManager _sceneManager;

    private Workspace Workspace { get; set; } = Workspace.CreateEmpty();

    [ObservableProperty] private bool _isBinary = true;
    [ObservableProperty] private string _fileFormat = "STL · triangle mesh";
    public string[] AvailableFormats { get; } = [
        "STL · triangle mesh",
        "3MF · 3D Manufacturing Format"
    ];
    [ObservableProperty] private string _destinationFolder;
    [ObservableProperty] private int _fileCount;
    [ObservableProperty] private string _exportButtonText = "Export 0 files";

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
        ExportButtonText = $"Export {FileCount} {(FileCount == 1 ? "file" : "files")}";

        // Info-panel values come from metadata (the features keep the cached stats fresh) -
        // no need to copy geometry just to display numbers.
        var metadataResult = workspace.GetActiveMeshMetadata();
        if (metadataResult.IsSuccess) {
            var metadata = metadataResult.Value;
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

        string extension = FileFormat.StartsWith("3MF", StringComparison.OrdinalIgnoreCase) ? ".3mf" : ".stl";

        foreach (var metadata in Workspace.MeshMetadataList) {
            var meshResult = Workspace.GetMesh(metadata.Id);
            if (meshResult.IsFailure) {
                _alert.ShowError($"Failed to export {metadata.Name}: {meshResult.Error.Description}");
                return;
            }

            using var mesh = meshResult.Value;
            // Just saving ignoring Binary/ASCII toggle since it's not supported by engine currently
            var filename = $"{metadata.Name}{extension}";
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
