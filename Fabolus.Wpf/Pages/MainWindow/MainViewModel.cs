using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Bolus;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Pages.MainWindow.MeshDisplay;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Pages.Channels;
using Fabolus.Wpf.Pages.Import;
using Fabolus.Wpf.Pages.Rotate;
using Fabolus.Wpf.Pages.Smooth;
using static Fabolus.Wpf.Bolus.BolusStore;
using Fabolus.Wpf.Features.Channels;
using Fabolus.Wpf.Features.Mould;
using Fabolus.Wpf.Pages.Mould;

namespace Fabolus.Wpf.Pages.MainWindow;
public partial class MainViewModel : ObservableObject {
    private const string NoFileText = "No file loaded";

    [ObservableProperty] private BaseViewModel? _currentViewModel;
    [ObservableProperty] private string _currentViewTitle = "No View Selected";
    [ObservableProperty] private MeshViewModel _currentMeshView = new MeshViewModel();
    [ObservableProperty] private SceneManager _currentSceneModel;

    //mesh info
    [ObservableProperty] private bool _infoVisible = false;
    [ObservableProperty] private bool _meshLoaded = false;
    [ObservableProperty] private string _filePath = NoFileText;
    [ObservableProperty] private string _fileSize = NoFileText;
    [ObservableProperty] private string _triangleCount = NoFileText;
    [ObservableProperty] private string _volumeText = NoFileText;

    //debug info
    [ObservableProperty] private string _debugText = NoFileText;

    private SceneManager _sceneModel;

    #region Stores
    private BolusStore BolusStore { get; set; } = new();
    private AirChannelsStore AirChannelsStore { get; set; } = new();
    private MouldStore MoldStore { get; set; } = new();

    #endregion

    private void NavigateTo(BaseViewModel viewModel) {

        if (CurrentViewModel is not null) {
            CurrentViewModel.Dispose(); //to ensure multiple view models dont listen in at the same time
            _sceneModel?.Dispose();
        }

        CurrentViewModel = viewModel;
        CurrentViewTitle = viewModel.TitleText;

        //based on the view
        _sceneModel = viewModel.GetSceneManager;
        DebugText = CurrentMeshView.Camera.Position.ToString();

    }

    public MainViewModel()
    {
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => BolusUpdated());

        NavigateTo(new ImportViewModel());
    }

    private void BolusUpdated() {
        var boli = WeakReferenceMessenger.Default.Send(new AllBolusRequestMessage()).Response;

        MeshLoaded = boli.Length > 0;
    }

    #region Commands
    [RelayCommand] public async Task SwitchToImportView() => NavigateTo(new ImportViewModel());
    [RelayCommand] public async Task SwitchToRotationView() => NavigateTo(new RotateViewModel());
    [RelayCommand] public async Task SwitchToSmoothingView() => NavigateTo(new SmoothingViewModel());
    [RelayCommand] public async Task SwitchToAirChannelView() => NavigateTo(new ChannelsViewModel());
    [RelayCommand] public async Task SwitchToMoldView() => NavigateTo(new MouldViewModel());
    /*
    [RelayCommand] public async Task SwitchToExportView() => NavigateTo(new ExportViewModel());
    */
    #endregion
}
