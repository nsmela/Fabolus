using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Windows;
using System.Windows.Media;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Features.MeshManager;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.Viewport;
using Fabolus.Wpf.Features.Smoothing;
using Fabolus.Wpf.Features.Rotatation;
using Fabolus.Wpf.Features.Moulding;
using Fabolus.Wpf.Features.Export;

namespace Fabolus.Wpf.Features.Main;
public partial class MainViewModel : ObservableObject {
    private readonly IMessenger _messenger;
    private readonly IGeometryEngine _engine;
    private readonly IDialogueSystem _dialogueSystem;
    private readonly IAlertDialog _alertDialog;
    private const string NoFileText = "No file loaded";

    [ObservableProperty] private IViewState _currentView;
    [ObservableProperty] private string _currentViewTitle = "No View Selected";
    [ObservableProperty] private bool _meshLoaded;
    [ObservableProperty] private string _meshName;
    [ObservableProperty] private ISceneManager _sceneManager;

    //debug info
    [ObservableProperty] private string _debugText = NoFileText;

    // display views
    [ObservableProperty] private bool _showSplitView;
    [ObservableProperty] private InfoPanelViewModel _infoViewModel;

    // loading display
    [ObservableProperty] private bool _isLoading = false;

    // Stores: passively monitor messages to store or retreive data for multiple views
    private AppPreferencesStore AppPreferences { get; } = new();
    private Workspace Workspace { get; set; } = Workspace.CreateEmpty();

    public MainViewModel(IMessenger messenger, IGeometryEngine engine, IDialogueSystem dialogueSystem, IAlertDialog alertDialog) {
        _messenger = messenger;
        _engine = engine;
        _dialogueSystem = dialogueSystem;
        _alertDialog = alertDialog;
        InfoViewModel = new InfoPanelViewModel(_messenger);

        _messenger.Register<PreferencesSetSplitViewMessage>(this, (r, m) => ShowSplitView = m.SplitViewEnabled);
        _messenger.Register<WorkspaceChangedMessage>(this, (r, m) => WorkspaceUpdated(m.Workspace));
        _messenger.Register<IsLoadingMessage>(this, (r,m) =>  IsLoading = m.IsLoading);

        ShowSplitView = _messenger.Send(new PreferencesSplitViewRequest()).Response;

        _ = SwitchToMeshManagerViewAsync();

    }

    private void WorkspaceUpdated(Workspace workspace) {
        Workspace = workspace;

        // Metadata-only read - only the name is needed here.
        var result = Workspace.GetActiveMeshMetadata();

        if (result.IsFailure && result.Error == WorkspaceErrors.NoActiveMesh) {
            MeshLoaded = false;
            MeshName = "No mesh selected";
            return;
        }

        if (result.IsFailure && result.Error != WorkspaceErrors.NoActiveMesh) {
            _alertDialog.ShowError(result.Error.Description);
            MeshLoaded = false;
            MeshName = "N/A";
            return;
        }

        MeshLoaded = Workspace.ActiveMeshId != Guid.Empty;
        MeshName = MeshLoaded ? result.Value.Name : "No mesh selected";
    }

    //[RelayCommand] public void ToggleWireframe() => _messenger.Send(new WireframeToggleMessage());

    [RelayCommand] public void CaptureScreenshot() {
        try {
            _messenger.Send(new CaptureScreenshotMessage());
        } catch (Exception e) {
            DebugText = $"Error triggering screenshot: {e.Message}";
        }
    }

    [RelayCommand]
    public void OpenPreferences() {
    //    PreferencesView preferences = 
    //        Application.Current.Windows.OfType<PreferencesView>().SingleOrDefault() 
    //        ?? new PreferencesView();

    //    preferences.Show();
    //    preferences.WindowState = WindowState.Normal;
    //    preferences.Activate();

    }

    [RelayCommand]
    public async Task SwitchToMeshManagerViewAsync() {
        if (CurrentView is MeshManagerViewModel) return;

        IsLoading = true;

        if(CurrentView is not null) {
            WorkspaceUpdated(await CurrentView.DeactivateAsync());
        }

        CurrentViewTitle = "meshes";

        var newView = new MeshManagerViewModel(_messenger, _dialogueSystem, _alertDialog, _engine);
        SceneManager = newView.SceneManager;
        CurrentView = newView;
        await CurrentView.ActivateAsync(Workspace);

        IsLoading = false;
    }

    [RelayCommand]
    public async Task SwitchToSmoothingViewAsync() {
        if (CurrentView is SmoothingViewModel) return;

        IsLoading = true;

        if (CurrentView is not null) {
            WorkspaceUpdated(await CurrentView.DeactivateAsync());
        }

        CurrentViewTitle = "smooth";

        var newView = new SmoothingViewModel(_messenger, _alertDialog, _engine);
        SceneManager = newView.SceneManager;
        CurrentView = newView;
        await CurrentView.ActivateAsync(Workspace);

        IsLoading = false;
    }

    [RelayCommand]
    public async Task SwitchToRotateViewAsync() {
        if (CurrentView is RotateViewModel) return;

        IsLoading = true;

        if (CurrentView is not null) {
            WorkspaceUpdated(await CurrentView.DeactivateAsync());
        }

        CurrentViewTitle = "rotate";

        var newView = new RotateViewModel(_messenger, _alertDialog, _engine);
        SceneManager = newView.SceneManager;
        CurrentView = newView;
        await CurrentView.ActivateAsync(Workspace);

        IsLoading = false;
    }

    [RelayCommand]
    public async Task SwitchToMouldViewAsync()
    {
        if (CurrentView is MouldViewModel) return;

        IsLoading = true;

        if (CurrentView is not null)
        {
            WorkspaceUpdated(await CurrentView.DeactivateAsync());
        }

        CurrentViewTitle = "mould";

        var newView = new MouldViewModel(_messenger, _alertDialog, _engine);
        SceneManager = newView.SceneManager;
        CurrentView = newView;
        await CurrentView.ActivateAsync(Workspace);

        IsLoading = false;
    }

    [RelayCommand]
    public async Task SwitchToExportViewAsync()
    {
        if (CurrentView is ExportViewModel) return;

        IsLoading = true;

        if (CurrentView is not null)
        {
            WorkspaceUpdated(await CurrentView.DeactivateAsync());
        }

        CurrentViewTitle = "export";

        var newView = new ExportViewModel(_messenger, _alertDialog, _engine, _dialogueSystem);
        SceneManager = newView.SceneManager;
        CurrentView = newView;
        await CurrentView.ActivateAsync(Workspace);

        IsLoading = false;
    }

}
