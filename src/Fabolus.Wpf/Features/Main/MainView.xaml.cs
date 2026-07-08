using MahApps.Metro.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Messaging;

namespace Fabolus.Wpf.Features.Main;

/// <summary>
/// Interaction logic for MainView.xaml
/// </summary>
public partial class MainView : MetroWindow {
    public MainView(MainViewModel viewModel, IMessenger messenger) {
        InitializeComponent();
        DataContext = viewModel;

        messenger.Register<CaptureScreenshotMessage>(this, (r, m) => CaptureScreenshot());
    }

    private void CaptureScreenshot()
    {
        try
        {
            var oldColor = ViewportControl.MainViewport.BackgroundColor;
            ViewportControl.MainViewport.BackgroundColor = (Color)ColorConverter.ConvertFromString("#FF2D2D35");
            var viewportBitmap = ViewportControl.RenderBitmap();
            ViewportControl.MainViewport.BackgroundColor = oldColor;

            var uiWidth = OverlayGrid.ActualWidth;
            var uiHeight = OverlayGrid.ActualHeight;

            if (uiWidth == 0 || uiHeight == 0) return;

            var source = PresentationSource.FromVisual(this);
            double dpiX = 96.0;
            double dpiY = 96.0;
            if (source?.CompositionTarget != null)
            {
                dpiX = 96.0 * source.CompositionTarget.TransformToDevice.M11;
                dpiY = 96.0 * source.CompositionTarget.TransformToDevice.M22;
            }

            int pixelWidth = (int)Math.Round(uiWidth * dpiX / 96.0);
            int pixelHeight = (int)Math.Round(uiHeight * dpiY / 96.0);

            // WPF's RenderTargetBitmap ALWAYS applies the visual's offset relative to the root visual.
            // To prevent the UI from being cut off, we must create an RTB large enough to include this offset,
            // render the UI exactly where WPF places it, and then crop it out.
            var root = source?.RootVisual;
            Point offset = new Point(0, 0);
            if (root != null)
            {
                offset = OverlayGrid.TransformToAncestor(root).Transform(new Point(0, 0));
            }

            int rtbWidth = (int)Math.Round((offset.X + uiWidth) * dpiX / 96.0);
            int rtbHeight = (int)Math.Round((offset.Y + uiHeight) * dpiY / 96.0);

            var oldUiBackground = OverlayGrid.Background;
            var oldToolVisibility = ToolButtonsPanel.Visibility;
            OverlayGrid.Background = Brushes.Transparent;
            ToolButtonsPanel.Visibility = Visibility.Hidden;
            OverlayGrid.UpdateLayout();

            RenderTargetBitmap renderUi = new(rtbWidth, rtbHeight, dpiX, dpiY, PixelFormats.Pbgra32);
            renderUi.Render(OverlayGrid);

            OverlayGrid.Background = oldUiBackground;
            ToolButtonsPanel.Visibility = oldToolVisibility;

            // Crop out exactly the OverlayGrid's physical pixels
            var cropRect = new Int32Rect(
                (int)Math.Round(offset.X * dpiX / 96.0),
                (int)Math.Round(offset.Y * dpiY / 96.0),
                pixelWidth,
                pixelHeight
            );
            CroppedBitmap croppedUi = new CroppedBitmap(renderUi, cropRect);

            // Composite background, 3D viewport, and UI
            DrawingVisual visual = new();
            using (DrawingContext context = visual.RenderOpen())
            {
                if (viewportBitmap != null)
                {
                    context.DrawImage(viewportBitmap, new Rect(0, 0, uiWidth, uiHeight));
                }

                context.DrawImage(croppedUi, new Rect(0, 0, uiWidth, uiHeight));
            }

            RenderTargetBitmap result = new(pixelWidth, pixelHeight, dpiX, dpiY, PixelFormats.Pbgra32);
            result.Render(visual);

            Clipboard.Clear();
            Clipboard.SetImage(result);
        } catch (Exception e) {
            // Depending on architecture, we might want to alert the user here or log.
            // Zoran's philosophy prefers Result objects, but this is an event handler.
            if (DataContext is MainViewModel vm) {
                vm.DebugText = $"Error copying screenshot to clipboard: {e.Message}";
            }
        }
    }

    void OnMinimize(object sender, RoutedEventArgs e)
    => WindowState = WindowState.Minimized;

    void OnMaximize(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    void OnClose(object sender, RoutedEventArgs e) => Close();
}

