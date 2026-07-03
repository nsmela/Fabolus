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
    [ObservableProperty] private string _fileFormat = "STL - triangle mesh";
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

        foreach (var kvp in Workspace.Meshes) {
            var mesh = kvp.Value;
            // Just saving as STL for now, ignoring Binary/ASCII toggle since it's not supported by engine currently
            var filename = $"{mesh.Metadata.Name}.stl";
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
