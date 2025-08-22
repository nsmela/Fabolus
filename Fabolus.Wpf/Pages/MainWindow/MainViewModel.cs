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
using System.Configuration;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Pages.Preferences;
using Fabolus.Wpf.Pages.Split;

namespace Fabolus.Wpf.Pages.MainWindow;
public partial class MainViewModel : ObservableObject {
    private readonly IMessenger _messenger = WeakReferenceMessenger.Default;

    private const string NoFileText = "No file loaded";

    [ObservableProperty] private UserControl _currentView;
    [ObservableProperty] private string _currentViewTitle = "No View Selected";
    [ObservableProperty] private MeshInfoViewModel _currentMeshInfo;
    [ObservableProperty] private bool _meshLoaded;

    //debug info
    [ObservableProperty] private string _debugText = NoFileText;

    // display views
    [ObservableProperty] private bool _showSplitView;

    // Stores: passively monitor messages to store or retreive data for multiple views
    private AppPreferencesStore AppPreferences { get; } = new();
    private BolusStore BolusStore { get; } = new();
    private AirChannelsStore AirChannelsStore { get; } = new();
    private MouldStore MouldStore { get; } = new();

    public MainViewModel() {

        CurrentMeshInfo = new();
        _messenger.Register<BolusUpdatedMessage>(this, (r, m) => BolusUpdated());
        _messenger.Register<PreferencesSetSplitViewMessage>(this, (r,m) => ShowSplitView = m.SplitViewEnabled);

        ShowSplitView = _messenger.Send(new PreferencesSplitViewRequest()).Response;

        SwitchToImportView();
    }

    private void BolusUpdated() {
        var boli = _messenger.Send(new AllBolusRequestMessage()).Response;

        MeshLoaded = boli.Length > 0;
    }

    // Commands

    [RelayCommand]
    public void SwitchToImportView() {
        CurrentViewTitle = "import";
        CurrentView = new ImportView();
    }

    [RelayCommand]
    public void SwitchToRotationView() {
        CurrentViewTitle = "rotate";
        CurrentView = new RotateView();
    }

    [RelayCommand]
    public void SwitchToSmoothingView() {
        CurrentViewTitle = "smooth";
        CurrentView = new SmoothingView();
    }

    [RelayCommand]
    public void SwitchToAirChannelView() {
        CurrentViewTitle = "channels";
        CurrentView = new ChannelsView();
    }

    [RelayCommand]
    public void SwitchToMouldView() {
        CurrentViewTitle = "mould";
        CurrentView = new MouldView();
    }

    [RelayCommand]
    public void SwitchToExportView() {
        CurrentViewTitle = "export";
        CurrentView = new ExportView();
    }

    [RelayCommand]
    public void SwitchToSplitView() {
        if (!ShowSplitView) { return; }
        CurrentViewTitle = "split";
        CurrentView = new SplitView();
    }

    [RelayCommand] public void ToggleWireframe() => _messenger.Send(new WireframeToggleMessage());

    [RelayCommand] public void CaptureScreenshot() {
        var viewport = _messenger.Send(new ViewportRequestMessage()).Response;
        var bitmap = ViewportExtensions.RenderBitmap(viewport);

        var info = _messenger.Send(new MeshInfoRequestMessage()).Response;
        RenderTargetBitmap renderInfo = new((int)viewport.ActualWidth, (int)viewport.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        renderInfo.Render(info);

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
            DebugText = $"Error copying screenshot to clipboard: {e.Message}";
        }

    }

    [RelayCommand]
    public void OpenPreferences() {
        PreferencesView preferences = 
            Application.Current.Windows.OfType<PreferencesView>().SingleOrDefault() 
            ?? new PreferencesView();

        preferences.Show();
        preferences.WindowState = WindowState.Normal;
        preferences.Activate();

    }

}
