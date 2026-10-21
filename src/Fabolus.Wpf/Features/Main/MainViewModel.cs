using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.About;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Export;
using Fabolus.Wpf.Features.MeshManager;
using Fabolus.Wpf.Features.Moulding;
using Fabolus.Wpf.Features.PartingSplit;
using Fabolus.Wpf.Features.Rotatation;
using Fabolus.Wpf.Features.Smoothing;
using Fabolus.Wpf.Features.Viewport;
using Fabolus.Wpf.Features.CutSplit;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Wpf.Features.Decal;

namespace Fabolus.Wpf.Features.Main;

public partial class MainViewModel : ObservableObject
{
    private readonly IMessenger _messenger;
    private readonly IGeometryEngine _engine;
    private readonly IDialogueSystem _dialogueSystem;
    private readonly IAlertDialog _alertDialog;
    private readonly IGlyphOutlineSource _glyphOutlineSource;
    private const string NoFileText = "No file loaded";

    [ObservableProperty] private IViewState _currentView;
    [ObservableProperty] private string _currentViewTitle = "No View Selected";
    [ObservableProperty] private bool _meshLoaded;
    [ObservableProperty] private string _meshName;
    [ObservableProperty] private ISceneManager _sceneManager;

    //debug info
    [ObservableProperty] private string _debugText = NoFileText;

    // wireframe display, cycled by the viewport overlay button
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WireframeToolTip))]
    private WireframeMode _wireframeMode = WireframeMode.None;

    // describes what the next click does, not the current state
    public string WireframeToolTip => WireframeMode switch
    {
        WireframeMode.None => "Show wireframe over the mesh",
        WireframeMode.Overlay => "Show wireframe only",
        _ => "Hide wireframe"
    };

    // display views
    [ObservableProperty] private bool _showSplitView;

    // Whether the cut / split tab button is offered at all. Both halves come from preferences
    // now: an on/off switch, and which meshes it applies to. Withholding it on a generated
    // mould used to be hardcoded here; it is the default of that scope preference instead.
    [ObservableProperty] private bool _showCutView;
    private bool _cutViewPreferenceEnabled;
    private CutViewScope _cutViewScope = CutViewScope.Base;
    private bool _activeMeshIsMould;

    // Whether the decals tab button is offered at all. Purely a preference - unlike the cut
    // view there is no mesh state that makes decals inapplicable.
    [ObservableProperty] private bool _showDecalView = true;
    [ObservableProperty] private Brush _viewportBackgroundBrush;
    [ObservableProperty] private InfoPanelViewModel _infoViewModel;

    // loading display
    [ObservableProperty] private bool _isLoading = false;

    // App Preferences Window
    private PreferencesViewModel PreferencesViewModel { get; }

    // Stores: passively monitor messages to store or retreive data for multiple views
    private Workspace Workspace { get; set; } = Workspace.CreateEmpty();

    private readonly UnsavedWork _unsavedWork = new();

    public MainViewModel(
        IMessenger messenger,
        IGeometryEngine engine,
        IDialogueSystem dialogueSystem,
        IAlertDialog alertDialog,
        IGlyphOutlineSource? glyphOutlineSource = null)
    {
        // Optional so tests can construct without one; in the app it comes from DI as a
        // singleton, which matters once the source starts caching glyph outlines.
        _glyphOutlineSource = glyphOutlineSource
            ?? GlyphOutlineSourceProvider.Default
            ?? new WpfGlyphOutlineSource(engine);
        _messenger = messenger;
        _engine = engine;
        _dialogueSystem = dialogueSystem;
        _alertDialog = alertDialog;
        InfoViewModel = new InfoPanelViewModel(_messenger);

        _messenger.Register<WorkspaceChangedMessage>(this, (r, m) => WorkspaceUpdated(m.Workspace));
        _messenger.Register<IsLoadingMessage>(this, (r, m) => IsLoading = m.IsLoading);
        _messenger.Register<SwitchToMeshManagerMessage>(this, async (r, m) => await SwitchToMeshManagerViewAsync());
        _messenger.Register<MeshesSavedMessage>(this, (r, m) => _unsavedWork.MarkSaved(m.Workspace, m.Ids));

        // Take the new value off the message rather than reading it back from the store,
        // so this doesn't depend on which recipient the messenger notifies first.
        _messenger.Register<PreferenceSectionUpdateMessage<GeneralPreferences>>(this, (r, m) => UpdateViewportBackground(m.Section.ViewportBackground));
        _messenger.Register<PreferenceSectionUpdateMessage<CutSplitPreferences>>(this, (r, m) => {
            _cutViewPreferenceEnabled = m.Section.CutViewEnabled;
            _cutViewScope = m.Section.CutScope;
            UpdateCutViewAvailability();

            ShowSplitView = m.Section.SplitViewEnabled;
            if (!ShowSplitView && CurrentView is PartingSplitViewModel)
            {
                _ = SwitchToMeshManagerViewAsync();
            }
        });
        _messenger.Register<PreferenceSectionUpdateMessage<DecalPreferences>>(this, (r, m) => {
            ShowDecalView = m.Section.Enabled;
            if (!ShowDecalView && CurrentView is DecalViewModel)
            {
                _ = SwitchToMeshManagerViewAsync();
            }
        });

        PreferencesViewModel = new PreferencesViewModel(_messenger, _alertDialog);

        UpdateViewportBackground(_messenger.GetSection(GeneralPreferences.Default).ViewportBackground);

        var cutSplit = _messenger.GetSection(CutSplitPreferences.Default);
        _cutViewPreferenceEnabled = cutSplit.CutViewEnabled;
        _cutViewScope = cutSplit.CutScope;
        UpdateCutViewAvailability();
        ShowSplitView = cutSplit.SplitViewEnabled;

        ShowDecalView = _messenger.GetSection(DecalPreferences.Default).Enabled;

        _ = SwitchToMeshManagerViewAsync();

    }

    private void UpdateViewportBackground(ViewportBackground bg)
    {
        if (bg == ViewportBackground.Graphite)
        {
            var brush = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#FF1A1A1D"),
                (Color)ColorConverter.ConvertFromString("#FF2D2D35"),
                new Point(0, 0),
                new Point(0, 1));
            brush.Freeze();
            ViewportBackgroundBrush = brush;
        }
        else if (bg == ViewportBackground.LightSteel)
        {
            var brush = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#FFD8DEE4"),
                (Color)ColorConverter.ConvertFromString("#FFB0B9C3"),
                new Point(0, 0),
                new Point(0, 1));
            brush.Freeze();
            ViewportBackgroundBrush = brush;
        }
    }

    private void UpdateCutViewAvailability()
    {
        bool scopeAllows = _cutViewScope switch
        {
            CutViewScope.Mould => _activeMeshIsMould,
            CutViewScope.Both => true,
            _ => !_activeMeshIsMould
        };

        ShowCutView = _cutViewPreferenceEnabled && scopeAllows;

        // Narrowing the scope while the cut view is open would otherwise leave the user on a
        // view whose tab has just disappeared.
        if (!ShowCutView && CurrentView is CutSplitViewModel)
        {
            _ = SwitchToMeshManagerViewAsync();
        }
    }

    private void WorkspaceUpdated(Workspace workspace)
    {
        Workspace = workspace;

        // Record-only read - the name and the mould command are all that is needed here.
        var result = Workspace.GetActiveRecord();

        // Without a readable record there is no mould to detect, so the cut view falls back
        // to whatever the preference allows.
        _activeMeshIsMould = result.IsSuccess && result.Value.MouldDefinition() is not null;
        UpdateCutViewAvailability();

        if (result.IsFailure && result.Error == WorkspaceErrors.NoActiveMesh)
        {
            MeshLoaded = false;
            MeshName = "No mesh selected";
            return;
        }

        if (result.IsFailure && result.Error != WorkspaceErrors.NoActiveMesh)
        {
            _alertDialog.ShowError(result.Error.Description);
            MeshLoaded = false;
            MeshName = "N/A";
            return;
        }

        MeshLoaded = Workspace.ActiveMeshId != Guid.Empty;
        MeshName = MeshLoaded ? result.Value.Name : "No mesh selected";
    }

    /// <summary>
    /// Whether the app may close: true when nothing would be lost, or when the user, told what
    /// would be, says to close anyway.
    /// </summary>
    /// <remarks>
    /// Asks the current view for its workspace rather than using <see cref="Workspace"/>: most
    /// views only hand theirs back on deactivation, so a smoothing applied a moment ago is not
    /// here yet. And it asks without deactivating, which would tear the view down even when the
    /// user then decides to stay.
    /// </remarks>
    public bool ConfirmClose()
    {
        var workspace = CurrentView?.CurrentWorkspace ?? Workspace;
        var unsaved = _unsavedWork.Unsaved(workspace);
        if (unsaved.Count == 0) return true;

        var names = string.Join(Environment.NewLine, unsaved.Select(record => $"  \u2022 {record.Name}"));
        var message = unsaved.Count == 1
            ? $"This mesh has changes that have not been exported:{Environment.NewLine}{Environment.NewLine}{names}{Environment.NewLine}{Environment.NewLine}Close anyway and lose them?"
            : $"These meshes have changes that have not been exported:{Environment.NewLine}{Environment.NewLine}{names}{Environment.NewLine}{Environment.NewLine}Close anyway and lose them?";

        return _dialogueSystem.ShowConfirmation("Unsaved changes", message);
    }

    // Cycles solid -> solid with edges -> edges only -> solid.
    [RelayCommand]
    public void ToggleWireframe() => WireframeMode = WireframeMode switch
    {
        WireframeMode.None => WireframeMode.Overlay,
        WireframeMode.Overlay => WireframeMode.Only,
        _ => WireframeMode.None
    };

    [RelayCommand]
    public void CaptureScreenshot()
    {
        try
        {
            _messenger.Send(new CaptureScreenshotMessage());
        }
        catch (Exception e)
        {
            DebugText = $"Error triggering screenshot: {e.Message}";
        }
    }

    [RelayCommand]
    public void OpenPreferences()
    {
        // Modal and owned by the main window, so it stays above it and can never be left behind
        // once the main window closes.
        var preferences = new PreferencesView(PreferencesViewModel) { Owner = Application.Current.MainWindow };
        preferences.ShowDialog();
    }

    [RelayCommand]
    public void OpenAbout()
    {
        var about = new AboutView(new AboutViewModel()) { Owner = Application.Current.MainWindow };
        about.ShowDialog();
    }

    /// <summary>
    /// Empties the info panel as each view is swapped in.
    /// </summary>
    /// <remarks>
    /// The panel is shared, but its contents belong to whichever view published them, so without
    /// this the next view inherits the last one's numbers - and the views that publish nothing
    /// at all (decals, moulding, cut/split) showed the previous view's readings for as long as
    /// the user stayed in them. Clearing here rather than in each view means a view has to do
    /// nothing to get an empty panel, and the ones that do publish are unaffected: every switch
    /// assigns this property immediately before awaiting ActivateAsync, so their own publishing
    /// happens after this and stands.
    /// </remarks>
    partial void OnCurrentViewChanged(IViewState value) =>
        _messenger.Send(new UpdateMeshInfoMessage([]));

    // One view switch at a time - see SwitchViewAsync.
    private readonly SemaphoreSlim _switchGate = new(1, 1);
    private int _latestSwitch;

    /// <summary>
    /// Deactivates the current view and opens a new one in its place, unless it is already open.
    /// </summary>
    /// <remarks>
    /// Switches wait for each other. A view's ActivateAsync can spend seconds off the UI thread
    /// (the parting view works out the body on opening), and the tabs stay clickable meanwhile; a
    /// second switch that deactivated it then tore down the state its activation was about to
    /// resume into, which threw (#89). Waiting here means no view ever has to cope with being
    /// deactivated half-open. Clicks made while waiting are not replayed in turn: only the most
    /// recent one is carried out, since that is where the user wants to end up.
    /// </remarks>
    private async Task SwitchViewAsync<TView>(string title, Func<TView> create) where TView : IViewState
    {
        var request = ++_latestSwitch;
        await _switchGate.WaitAsync();
        try
        {
            if (request != _latestSwitch) return;
            if (CurrentView is TView) return;

            IsLoading = true;
            try
            {
                if (CurrentView is not null)
                {
                    WorkspaceUpdated(await CurrentView.DeactivateAsync());
                }

                CurrentViewTitle = title;

                var newView = create();
                SceneManager = newView.SceneManager;
                CurrentView = newView;
                await CurrentView.ActivateAsync(Workspace);
            }
            finally
            {
                IsLoading = false;
            }
        }
        finally
        {
            _switchGate.Release();
        }
    }

    [RelayCommand]
    public Task SwitchToMeshManagerViewAsync() =>
        SwitchViewAsync("meshes", () => new MeshManagerViewModel(_messenger, _dialogueSystem, _alertDialog, _engine));

    [RelayCommand]
    public Task SwitchToSmoothingViewAsync() =>
        SwitchViewAsync("smooth", () => new SmoothingViewModel(_messenger, _alertDialog, _engine));

    [RelayCommand]
    public Task SwitchToRotateViewAsync() =>
        SwitchViewAsync("rotate", () => new RotateViewModel(_messenger, _alertDialog, _engine));

    [RelayCommand]
    public Task SwitchToDecalViewAsync() =>
        SwitchViewAsync("decals", () => new DecalViewModel(_messenger, _alertDialog, _engine, _glyphOutlineSource));

    [RelayCommand]
    public Task SwitchToMouldViewAsync() =>
        SwitchViewAsync("mould", () => new MouldViewModel(_messenger, _alertDialog, _engine));

    [RelayCommand]
    public Task SwitchToExportViewAsync() =>
        SwitchViewAsync("export", () => new ExportViewModel(_messenger, _alertDialog, _engine, _dialogueSystem));

    [RelayCommand]
    public Task ShowCutSplitAsync() =>
        SwitchViewAsync("cut / split", () => new CutSplitViewModel(_messenger, _alertDialog, _engine, _dialogueSystem));

    // Switching CurrentView always deactivates whatever was active first (see above), so this
    // and ShowCutSplitAsync are naturally mutually exclusive - only one can ever be CurrentView.
    [RelayCommand]
    public async Task ShowPartingSplitAsync()
    {
        if (!ShowSplitView) return;
        await SwitchViewAsync("parting split", () => new PartingSplitViewModel(_messenger, _alertDialog, _engine));
    }
}
