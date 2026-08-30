using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Export;
using Fabolus.Wpf.Features.MeshManager;
using Fabolus.Wpf.Features.Moulding;
using Fabolus.Wpf.Features.Rotatation;
using Fabolus.Wpf.Features.Smoothing;
using Fabolus.Wpf.Features.Viewport;
using Fabolus.Wpf.Features.CutSplit;
using Fabolus.Core.Features.Decal;
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
            ?? new WpfGlyphOutlineSource();
        _messenger = messenger;
        _engine = engine;
        _dialogueSystem = dialogueSystem;
        _alertDialog = alertDialog;
        InfoViewModel = new InfoPanelViewModel(_messenger);

        _messenger.Register<WorkspaceChangedMessage>(this, (r, m) => WorkspaceUpdated(m.Workspace));
        _messenger.Register<IsLoadingMessage>(this, (r, m) => IsLoading = m.IsLoading);
        _messenger.Register<SwitchToMeshManagerMessage>(this, async (r, m) => await SwitchToMeshManagerViewAsync());

        // Take the new value off the message rather than reading it back from the store,
        // so this doesn't depend on which recipient the messenger notifies first.
        _messenger.Register<PreferenceSectionUpdateMessage<GeneralPreferences>>(this, (r, m) => UpdateViewportBackground(m.Section.ViewportBackground));
        _messenger.Register<PreferenceSectionUpdateMessage<CutSplitPreferences>>(this, (r, m) => {
            _cutViewPreferenceEnabled = m.Section.CutViewEnabled;
            _cutViewScope = m.Section.CutScope;
            UpdateCutViewAvailability();
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

        // Metadata-only read - the name and the mould command are all that is needed here.
        var result = Workspace.GetActiveMeshMetadata();

        // Without readable metadata there is no mould to detect, so the cut view falls back
        // to whatever the preference allows.
        _activeMeshIsMould = result.IsSuccess && result.Value.MouldDefinition().HasValue;
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
        PreferencesView preferences =
            Application.Current.Windows.OfType<PreferencesView>().SingleOrDefault()
            ?? new PreferencesView(PreferencesViewModel);

        preferences.Show();
        preferences.WindowState = WindowState.Normal;
        preferences.Activate();

    }

    [RelayCommand]
    public async Task SwitchToMeshManagerViewAsync()
    {
        if (CurrentView is MeshManagerViewModel) return;

        IsLoading = true;

        if (CurrentView is not null)
        {
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
    public async Task SwitchToSmoothingViewAsync()
    {
        if (CurrentView is SmoothingViewModel) return;

        IsLoading = true;

        if (CurrentView is not null)
        {
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
    public async Task SwitchToRotateViewAsync()
    {
        if (CurrentView is RotateViewModel) return;

        IsLoading = true;

        if (CurrentView is not null)
        {
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
    public async Task SwitchToEmbossViewAsync()
    {
        if (CurrentView is DecalViewModel) return;

        IsLoading = true;

        if (CurrentView is not null)
        {
            WorkspaceUpdated(await CurrentView.DeactivateAsync());
        }

        CurrentViewTitle = "decals";

        var newView = new DecalViewModel(_messenger, _alertDialog, _engine, _glyphOutlineSource);
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

    [RelayCommand]
    public async Task ShowCutSplitAsync()
    {
        if (CurrentView is CutSplitViewModel)
            return;

        IsLoading = true;

        if (CurrentView is not null)
        {
            WorkspaceUpdated(await CurrentView.DeactivateAsync());
        }

        CurrentViewTitle = "cut / split";

        var newView = new CutSplitViewModel(_messenger, _alertDialog, _engine, _dialogueSystem);
        SceneManager = newView.SceneManager;
        CurrentView = newView;
        await CurrentView.ActivateAsync(Workspace);

        IsLoading = false;
    }
}
