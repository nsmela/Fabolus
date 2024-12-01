using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Bolus;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Common.Scene;
using Fabolus.Wpf.Pages.Import;
using Fabolus.Wpf.Pages.Rotate;
using Fabolus.Wpf.Stores;
using HelixToolkit.Wpf.SharpDX;
using static Fabolus.Wpf.Stores.BolusStore;

namespace Fabolus.Wpf.Pages.MainWindow;
public partial class MainViewModel : ObservableObject {
    private const string NoFileText = "No file loaded";

    [ObservableProperty] private BaseViewModel? _currentViewModel;
    [ObservableProperty] private string _currentViewTitle = "No View Selected";
    [ObservableProperty] private MeshViewModel _currentMeshView = new MeshViewModel();
    [ObservableProperty] private SceneModel _currentSceneModel;

    //mesh info
    [ObservableProperty] private bool _infoVisible = false;
    [ObservableProperty] private bool _meshLoaded = false;
    [ObservableProperty] private string _filePath = NoFileText;
    [ObservableProperty] private string _fileSize = NoFileText;
    [ObservableProperty] private string _triangleCount = NoFileText;
    [ObservableProperty] private string _volumeText = NoFileText;

    private SceneModel _sceneModel;

    #region Stores
    private BolusStore BolusStore { get; set; }
    #endregion

    private void NavigateTo(BaseViewModel viewModel) {

        if (CurrentViewModel is not null) {
            CurrentViewModel.Dispose(); //to ensure multiple view models dont listen in at the same time
            _sceneModel?.Dispose();
        }

        CurrentViewModel = viewModel;
        CurrentViewTitle = viewModel.TitleText;

        //based on the view
        _sceneModel = viewModel.GetSceneModel;
        _sceneModel.SceneUpdated += CurrentMeshView.SetModel; //the scene model updates the latest mesh view model's scene
    }

    public MainViewModel()
    {
        BolusStore = new();

        //messages
        WeakReferenceMessenger.Default.Register<BolusUpdatedMessage>(this, (r, m) => BolusUpdated(m.bolus));

        NavigateTo(new ImportViewModel());
    }

    private void BolusUpdated(BolusModel bolus) {
        MeshLoaded = bolus.IsLoaded;
    }

    #region Commands
    [RelayCommand] public async Task SwitchToImportView() => NavigateTo(new ImportViewModel());
    [RelayCommand] public async Task SwitchToRotationView() => NavigateTo(new RotateViewModel());
    /*
    [RelayCommand] public async Task SwitchToSmoothingView() => NavigateTo(new SmoothingViewModel());
    [RelayCommand] public async Task SwitchToAirChannelView() => NavigateTo(new AirChannelViewModel());
    [RelayCommand] public async Task SwitchToMoldView() => NavigateTo(new MoldViewModel());
    [RelayCommand] public async Task SwitchToExportView() => NavigateTo(new ExportViewModel());
    */
    #endregion
}
