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
using Fabolus.Wpf.Pages.Export;
using HelixToolkit.Wpf.SharpDX;
using System.Diagnostics;
using System.Windows;
using Fabolus.Wpf.Pages.MainWindow.MeshInfo;
using System.Drawing;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HelixToolkit.Wpf.SharpDX.Utilities;
using System.Windows.Controls;

namespace Fabolus.Wpf.Pages.MainWindow;
public partial class MainViewModel : ObservableObject {
    private const string NoFileText = "No file loaded";

    [ObservableProperty] private BaseViewModel? _currentViewModel;
    [ObservableProperty] private string _currentViewTitle = "No View Selected";
    [ObservableProperty] private MeshViewModel _currentMeshView = new();
    [ObservableProperty] private SceneManager _currentSceneModel;
    [ObservableProperty] private MeshInfoViewModel _currentMeshInfo = new();

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
    [RelayCommand] public async Task SwitchToExportView() => NavigateTo(new ExportViewModel());

    [RelayCommand] public async Task CaptureScreenshot() {
        var viewport = WeakReferenceMessenger.Default.Send(new ViewportRequestMessage()).Response;
        var bitmap = ViewportExtensions.RenderBitmap(viewport);
        var info = WeakReferenceMessenger.Default.Send(new MeshInfoRequestMessage()).Response;
        var rect = new Rect(
            viewport.ActualWidth - info.ActualWidth - info.Margin.Right,
            info.Margin.Top,
            info.ActualWidth,
            info.ActualHeight
        );

        RenderTargetBitmap renderInfo = new((int)viewport.ActualWidth, (int)viewport.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        renderInfo.Render(info);
        var isloaded = info.IsLoaded;
        DrawingVisual visual = new();
        using (DrawingContext context = visual.RenderOpen()) {
            context.DrawImage(bitmap, new Rect(0, 0, viewport.ActualWidth, viewport.ActualHeight));
            context.DrawImage(renderInfo, new Rect(0, 0, viewport.ActualWidth, viewport.ActualHeight));
        }

        RenderTargetBitmap result = new((int)viewport.ActualWidth, (int)viewport.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);

        try {
            Clipboard.Clear();
            Clipboard.SetImage(result);
        } catch (Exception e) {
            Debug.WriteLine($"Error copying screenshot to clipboard: {e.Message}");
        }
    }

    #endregion
}
