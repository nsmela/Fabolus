using HelixToolkit.Wpf.SharpDX;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using System.Windows;
using Camera = HelixToolkit.Wpf.SharpDX.Camera;

namespace Fabolus.Wpf.Features.Viewport;

/// <summary>
/// Interaction logic for MeshView.xaml
/// </summary>
public partial class ViewportControl : UserControl, IDisposable {
    private readonly Dictionary<Guid, Element3D> _registry = new();

    public Camera Camera { get; }
    public DefaultEffectsManager EffectsManager { get; }

    public ViewportControl() {
        InitializeComponent();

        EffectsManager = new DefaultEffectsManager();
        Camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera {
            Position = new Point3D(0, 0, 100),
            LookDirection = new Vector3D(0, 0, -100),
            UpDirection = new Vector3D(0, 1, 0)
        };

    }

    public ISceneManager? SceneManager {
        get => (ISceneManager?)GetValue(SceneManagerProperty);
        set => SetValue(SceneManagerProperty, value);
    }

    public static readonly DependencyProperty SceneManagerProperty =
        DependencyProperty.Register(
            nameof(SceneManager),
            typeof(ISceneManager),
            typeof(ViewportControl),
            new PropertyMetadata(null, OnSceneManagerChanged));

    private static void OnSceneManagerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is not ViewportControl control) return;

        if(e.OldValue is not null && e.OldValue is ISceneManager oldManager) {
            oldManager.VisualAddedOrUpdated -= control.OnAddOrUpdateVisual;
            oldManager.VisualRemovedById -= control.OnRemoveVisualById;
            oldManager.VisualsCleared -= control.OnClearAllVisuals;
            oldManager.OnDeactivated();
        }

        if (e.NewValue is not null && e.NewValue is ISceneManager newManager) {
            newManager.VisualAddedOrUpdated += control.OnAddOrUpdateVisual;
            newManager.VisualRemovedById += control.OnRemoveVisualById;
            newManager.VisualsCleared += control.OnClearAllVisuals;

            newManager.OnActivated();
        }
    }

    private void OnAddOrUpdateVisual(Element3D visual) => Dispatcher.BeginInvoke(new Action(() => {
        var id = visual.GUID;
        if (_registry.TryGetValue(id, out var registry)) {
            UpdateProperties(_registry[id], visual);
        } else {
            _registry[id] = visual;
            MainViewport.Items.Add(visual);
            //MainViewport.ZoomExtents();
        }
    }));

    private void OnClearAllVisuals() => Dispatcher.BeginInvoke(new Action(() => {
        foreach (var visual in _registry.Values) {
            visual.Dispose();
        }
        _registry.Clear();
        MainViewport.Items.Clear();
    }));

    private void OnRemoveVisualById(Guid id) => Dispatcher.BeginInvoke(new Action(() => {
        if (_registry.Remove(id, out var visual)) {
            MainViewport.Items.Remove(visual);
            _registry.Remove(id);
            visual.Dispose(); // Memory safety!
        }
    }));

    // --- INPUT ROUTING (From XAML events) ---

    private void MainViewport_MouseDown3D(object sender, RoutedEventArgs e) {
        var handled = false;
        if (e is MouseDown3DEventArgs args) {
            handled = SceneManager?.OnMouseDown(args) ?? false;
        }

        // normal function
    }

    private void MainViewport_MouseMove3D(object sender, RoutedEventArgs e) {
        if (e is MouseMove3DEventArgs args) {
            SceneManager?.OnMouseMove(args);
        }
    }

    private void MainViewport_MouseUp3D(object sender, RoutedEventArgs e) {
        var handled = false;
        if (e is MouseUp3DEventArgs args) {
            handled = SceneManager?.OnMouseUp(args) ?? false;
        }
    }

    private void MainViewport_KeyDown(object sender, KeyEventArgs e) {
        // 1. Handle global keys first (e.g., 'T' for Top View)
        // if (HandleGlobalCameraKeys(e.Key)) { e.Handled = true; return; }

        // 2. Pass to active tool
        if (SceneManager is null) return;

        e.Handled = SceneManager.OnKeyDown(e.Key);
        
    }

    private void MainViewport_KeyUp(object sender, KeyEventArgs e) {
        if (SceneManager is null) return;

        e.Handled = SceneManager.OnKeyUp(e.Key);
    }

    private bool _disposed = false;
    public void Dispose() {
        if (_disposed) return;

        if (SceneManager is not null && SceneManager is ISceneManager manager) {
            manager.VisualAddedOrUpdated -= OnAddOrUpdateVisual;
            manager.VisualRemovedById -= OnRemoveVisualById;
            manager.VisualsCleared -= OnClearAllVisuals;
            manager.OnDeactivated();
        }

        OnClearAllVisuals();

        EffectsManager?.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }

    private void UpdateProperties(Element3D existing, Element3D update) {
        existing.Visibility = update.Visibility;

        // MeshGeometryModel3D
        if (existing is MeshGeometryModel3D existingMesh && update is MeshGeometryModel3D updateMesh) {
            // Only update the Geometry/Material if they actually changed
            if (existingMesh.Geometry != updateMesh.Geometry)
                existingMesh.Geometry = updateMesh.Geometry;

            if (existingMesh.Material != updateMesh.Material)
                existingMesh.Material = updateMesh.Material;

            // Always update the transform (the most frequent operation)
            existingMesh.Transform = updateMesh.Transform;

            existingMesh.CullMode = updateMesh.CullMode;
            existingMesh.FillMode = updateMesh.FillMode;
            existingMesh.IsTransparent = updateMesh.IsTransparent;
            return;
        }

        // LineGeometryModel3D (For gizmos, grids, or path sweeping previews)
        if (existing is LineGeometryModel3D existingLine && update is LineGeometryModel3D updateLine) {
            existingLine.Geometry = updateLine.Geometry;
            existingLine.Color = updateLine.Color;
            existingLine.Thickness = updateLine.Thickness;
            existingLine.Transform = updateLine.Transform;
            return;
        }
    }

}


