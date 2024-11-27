using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Pages.Import;
using Fabolus.Wpf.Stores;
using HelixToolkit.Wpf.SharpDX;

namespace Fabolus.Wpf.Pages.MainWindow;
public partial class MainViewModel : ObservableObject {
    private const string NoFileText = "No file loaded";

    [ObservableProperty] private BaseViewModel? _currentViewModel;
    [ObservableProperty] private BaseMeshViewModel? _currentMeshView;
    [ObservableProperty] private string _currentViewTitle = "No View Selected";

    //mesh info
    [ObservableProperty] private bool _infoVisible = false;
    [ObservableProperty] private bool _meshLoaded = false;
    [ObservableProperty] private string _filePath = NoFileText;
    [ObservableProperty] private string _fileSize = NoFileText;
    [ObservableProperty] private string _triangleCount = NoFileText;
    [ObservableProperty] private string _volumeText = NoFileText;


    #region Stores
    private BolusStore BolusStore { get; set; }
    #endregion

    private void NavigateTo(BaseViewModel viewModel) {
        //copying camera position
        var meshView = viewModel.MeshViewModel;

        if (CurrentMeshView is null) {
            meshView.Camera = new PerspectiveCamera();
            meshView.Camera.Reset();
        } else {
            meshView.Camera = CurrentMeshView.Camera;
        }

        CurrentViewModel = viewModel;
        CurrentMeshView = meshView;
        CurrentViewTitle = viewModel.TitleText;

    }

    public MainViewModel()
    {
        BolusStore = new();

        NavigateTo(new ImportViewModel());
    }

    #region Commands
    [RelayCommand] public async Task SwitchToImportView() => NavigateTo(new ImportViewModel());
    /*
    [RelayCommand] public async Task SwitchToSmoothingView() => NavigateTo(new SmoothingViewModel());
    [RelayCommand] public async Task SwitchToRotationView() => NavigateTo(new RotationViewModel());
    [RelayCommand] public async Task SwitchToAirChannelView() => NavigateTo(new AirChannelViewModel());
    [RelayCommand] public async Task SwitchToMoldView() => NavigateTo(new MoldViewModel());
    [RelayCommand] public async Task SwitchToExportView() => NavigateTo(new ExportViewModel());
    */
    #endregion
}
