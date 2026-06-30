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

        ShowSplitView = _messenger.Send(new PreferencesSplitViewRequest()).Response;

        SwitchToMeshManagerView();
    }

    private void WorkspaceUpdated(Workspace workspace) {
        Workspace = workspace;

        var result = Workspace.GetActiveMesh();

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

        var mesh = result.Value;

        MeshLoaded = Workspace.ActiveMeshId != Guid.Empty;
        MeshName = MeshLoaded ? mesh.Metadata.Name : "No mesh selected";
    }

    //[RelayCommand] public void ToggleWireframe() => _messenger.Send(new WireframeToggleMessage());

    [RelayCommand] public void CaptureScreenshot() {
        //var viewport = _messenger.Send(new ViewportRequestMessage()).Response;
        //var bitmap = ViewportExtensions.RenderBitmap(viewport);

        //var info = _messenger.Send(new MeshInfoRequestMessage()).Response;
        //RenderTargetBitmap renderInfo = new((int)viewport.ActualWidth, (int)viewport.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        //renderInfo.Render(info);

        DrawingVisual visual = new();
        //using (DrawingContext context = visual.RenderOpen()) {
        //    context.DrawImage(bitmap, new Rect(0, 0, viewport.ActualWidth, viewport.ActualHeight));
        //    context.DrawImage(renderInfo, new Rect(0, 0, viewport.ActualWidth, viewport.ActualHeight));
        //}

        //RenderTargetBitmap result = new((int)viewport.ActualWidth, (int)viewport.ActualHeight, 96, 96, PixelFormats.Pbgra32);
       // result.Render(visual);

        try {
            Clipboard.Clear();
        //    Clipboard.SetImage(result);
        } catch (Exception e) {
            DebugText = $"Error copying screenshot to clipboard: {e.Message}";
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
    public void SwitchToMeshManagerView() {
        if (CurrentView is MeshManagerViewModel) return;

        if(CurrentView is not null) {
            WorkspaceUpdated(CurrentView.Deactivate());
        }

        CurrentViewTitle = "meshes";
        CurrentView = new MeshManagerViewModel(_messenger, _dialogueSystem, _alertDialog, _engine);
        SceneManager = CurrentView.SceneManager;

        CurrentView.Activate(Workspace);
    }

    [RelayCommand]
    public void SwitchToSmoothingView() {
        if (CurrentView is SmoothingViewModel) return;

        if (CurrentView is not null) {
            WorkspaceUpdated(CurrentView.Deactivate());
        }

        CurrentViewTitle = "smooth";
        CurrentView = new SmoothingViewModel(_messenger, _alertDialog, _engine);
        SceneManager = CurrentView.SceneManager;

        CurrentView.Activate(Workspace);
    }

    [RelayCommand]
    public void SwitchToRotateView() {
        if (CurrentView is RotateViewModel) return;

        if (CurrentView is not null) {
            WorkspaceUpdated(CurrentView.Deactivate());
        }

        CurrentViewTitle = "rotate";
        CurrentView = new RotateViewModel(_messenger, _alertDialog, _engine);
        SceneManager = CurrentView.SceneManager;

        CurrentView.Activate(Workspace);
    }
}
