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
        // Set before InitializeComponent: Camera and EffectsManager are plain CLR properties
        // with no change notification, so anything in the XAML bound to them - the viewport
        // itself, and the light directions bound through Camera.LookDirection - resolves once
        // and never recovers if it happens to resolve while they are still null.
        EffectsManager = new DefaultEffectsManager();
        Camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera {
            Position = new Point3D(0, 0, 100),
            LookDirection = new Vector3D(0, 0, -100),
            UpDirection = new Vector3D(0, 1, 0),

            // HelixToolkit defaults these to 0.01 and 1000. Depth buffer precision is governed by
            // the near plane, and a near plane that close to the eye spends nearly the whole
            // range within a millimetre of the camera: at 0.01 the smallest resolvable depth step
            // 260mm out is already ~0.4mm, so anything coincident - the wireframe overlay on its
            // own mesh, the mould against the bolus, the cut plane - dissolves into z-fighting as
            // soon as you zoom out. Everything here is millimetre-scale and 1mm is closer than
            // any of these tools need to get, which buys back a factor of 100.
            NearPlaneDistance = 1.0,
            FarPlaneDistance = 5000.0
        };

        InitializeComponent();
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

    public WireframeMode WireframeMode {
        get => (WireframeMode)GetValue(WireframeModeProperty);
        set => SetValue(WireframeModeProperty, value);
    }

    public static readonly DependencyProperty WireframeModeProperty =
        DependencyProperty.Register(
            nameof(WireframeMode),
            typeof(WireframeMode),
            typeof(ViewportControl),
            new PropertyMetadata(WireframeMode.None, OnWireframeModeChanged));

    private static void OnWireframeModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        if (d is not ViewportControl control) return;

        foreach (var visual in control._registry.Values) {
            control.ApplyWireframeMode(visual);
        }
    }

    // Edges are drawn as their own line geometry rather than with FillMode.Wireframe or
    // MeshGeometryModel3D.RenderWireframe. Both of those rasterise the triangles themselves, so
    // every shared edge is drawn twice at identical depth and the two draws z-fight into a
    // stippled, grainy mess. Deduplicating the edges up front draws each one exactly once, and a
    // LineGeometryModel3D expands them to screen-space quads, so they get real thickness and
    // antialiasing instead of ragged one-pixel hairlines.
    private readonly Dictionary<Guid, WireframeVisual> _wireframes = new();

    private sealed record WireframeVisual(LineGeometryModel3D Visual, HelixToolkit.Wpf.SharpDX.Geometry3D Source);

    // Dark enough to read as topology over the lit skins, light enough to read against the
    // viewport's dark background when the surfaces are hidden.
    private static readonly System.Windows.Media.Color WireframeOverlayColor =
        System.Windows.Media.Color.FromRgb(0x1E, 0x22, 0x28);
    private static readonly System.Windows.Media.Color WireframeOnlyColor =
        System.Windows.Media.Color.FromRgb(0x9F, 0xB0, 0xBE);

    // The scene managers' own intent for each mesh, kept because Only mode hides their surfaces
    // and that has to be undone when the mode is switched off.
    private readonly Dictionary<Guid, Visibility> _meshVisibility = new();

    // Scene managers build their visuals with no knowledge of the wireframe toggle, so the mode
    // has to be re-applied to everything they hand over, updates included.
    private void ApplyWireframeMode(Element3D visual) {
        // Only the user's own geometry follows the toggle. A type check alone is not enough:
        // HelixToolkit's manipulators are themselves MeshGeometryModel3D, so the drag handles
        // would be wireframed and, in Only mode, hidden outright.
        if (visual is not MeshGeometryModel3D mesh || !SceneVisual.GetIsModelGeometry(mesh)) return;

        var id = visual.GUID;
        var intended = _meshVisibility.TryGetValue(id, out var visibility) ? visibility : mesh.Visibility;

        // A mesh its own scene manager has hidden stays hidden, wireframe and all.
        if (WireframeMode == WireframeMode.None || intended != Visibility.Visible) {
            RemoveWireframe(id);
            mesh.Visibility = intended;
            return;
        }

        mesh.Visibility = WireframeMode == WireframeMode.Only ? Visibility.Collapsed : Visibility.Visible;
        UpdateWireframe(id, mesh);
    }

    private void UpdateWireframe(Guid id, MeshGeometryModel3D mesh) {
        if (mesh.Geometry is not HelixToolkit.Wpf.SharpDX.MeshGeometry3D geometry
            || geometry.Positions is null
            || geometry.Indices is null) {
            RemoveWireframe(id);
            return;
        }

        _wireframes.TryGetValue(id, out var existing);
        var wireframe = existing?.Visual;

        if (wireframe is null) {
            wireframe = new LineGeometryModel3D {
                Thickness = 0.8,
                IsHitTestVisible = false,
            };
            MainViewport.Items.Add(wireframe);
        }

        // Extracting the edges is O(triangles), so only redo it when the geometry itself changes.
        // Dragging a gizmo republishes the same geometry with a new transform every frame.
        if (!ReferenceEquals(existing?.Source, geometry)) {
            wireframe.Geometry = BuildWireframe(geometry);
        }

        wireframe.Color = WireframeMode == WireframeMode.Only ? WireframeOnlyColor : WireframeOverlayColor;
        wireframe.Transform = mesh.Transform;
        wireframe.Visibility = Visibility.Visible;

        _wireframes[id] = new WireframeVisual(wireframe, geometry);
    }

    // The lines trace edges that lie exactly on the surface, so in Overlay mode they z-fight with
    // it. Depth bias is the usual cure but it works in depth-buffer units, and the camera's near
    // plane sits at HelixToolkit's default 0.01 against a far plane of 1000: nearly the whole
    // depth range is spent within a millimetre of the camera, so a bias big enough to clear the
    // surface up close drags lines at 100mm about 20mm forward, and the far side of the mesh
    // punches through the near side. Nudging the vertices along the surface normal instead is a
    // plain world-space offset that behaves identically everywhere in the scene.
    private static LineGeometry3D BuildWireframe(HelixToolkit.Wpf.SharpDX.MeshGeometry3D geometry) {
        var edges = MeshGeometryHelper.FindEdges(geometry);
        var normals = geometry.Normals;

        if (normals is null || normals.Count != geometry.Positions.Count) {
            return new LineGeometry3D { Positions = geometry.Positions, Indices = edges };
        }

        // Proportional to the model, so it stays invisible on a large mesh and still clears the
        // surface on a small one.
        var offset = Math.Max(
            0.01f,
            (geometry.Bound.Maximum - geometry.Bound.Minimum).Length() * 0.0005f);

        var positions = new HelixToolkit.Wpf.SharpDX.Vector3Collection(geometry.Positions.Count);
        for (var i = 0; i < geometry.Positions.Count; i++) {
            positions.Add(geometry.Positions[i] + (normals[i] * offset));
        }

        return new LineGeometry3D { Positions = positions, Indices = edges };
    }

    private void RemoveWireframe(Guid id) {
        if (!_wireframes.Remove(id, out var wireframe)) return;

        MainViewport.Items.Remove(wireframe.Visual);
        wireframe.Visual.Dispose();
    }

    private void RemoveAllWireframes() {
        foreach (var wireframe in _wireframes.Values) {
            MainViewport.Items.Remove(wireframe.Visual);
            wireframe.Visual.Dispose();
        }
        _wireframes.Clear();
    }

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

        // Captured from the incoming visual, which always carries the scene manager's intent -
        // the registered copy may already be hidden by Only mode.
        if (SceneVisual.GetIsModelGeometry(visual)) {
            _meshVisibility[id] = visual.Visibility;
        }

        if (_registry.TryGetValue(id, out var registry)) {
            UpdateProperties(_registry[id], visual);
            ApplyWireframeMode(_registry[id]);
        } else {
            _registry[id] = visual;
            ApplyWireframeMode(visual);
            MainViewport.Items.Add(visual);
            //MainViewport.ZoomExtents();
        }
    }));

    private void OnClearAllVisuals() => Dispatcher.BeginInvoke(new Action(() => {
        // Remove only what the scene managers put here. MainViewport.Items also holds the
        // lights declared in XAML, and Items.Clear() destroys those too - after the first
        // scene change the scene has no lights at all and every Phong material renders black.
        foreach (var visual in _registry.Values) {
            MainViewport.Items.Remove(visual);
            visual.Dispose();
        }
        _registry.Clear();
        _meshVisibility.Clear();
        RemoveAllWireframes();
    }));

    private void OnRemoveVisualById(Guid id) => Dispatcher.BeginInvoke(new Action(() => {
        if (_registry.Remove(id, out var visual)) {
            MainViewport.Items.Remove(visual);
            _meshVisibility.Remove(id);
            RemoveWireframe(id);
            visual.Dispose(); // Memory safety!
        }
    }));

    public System.Windows.Media.Imaging.BitmapSource RenderBitmap() => 
        HelixToolkit.Wpf.SharpDX.ViewportExtensions.RenderBitmap(MainViewport);

    // --- INPUT ROUTING (From XAML events) ---

    private void MainViewport_MouseDown(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton == MouseButton.Left && SceneManager is not null) {
            var position = e.GetPosition(MainViewport);
            var hits = MainViewport.FindHits(position);
            if (hits.Count == 0) {
                SceneManager.OnMouseDown(new MouseDown3DEventArgs(MainViewport, null, position, MainViewport, e));
            }
        }
    }

    private void MainViewport_MouseDown3D(object sender, RoutedEventArgs e) {
        var handled = false;
        if (e is MouseDown3DEventArgs args) {
            handled = SceneManager?.OnMouseDown(args) ?? false;
        }

        // normal function
    }

    private void MainViewport_MouseMove(object sender, MouseEventArgs e) {
        if (SceneManager is null) return;

        var position = e.GetPosition(MainViewport);
        var hits = MainViewport.FindHits(position);

        SceneManager.OnMouseMove(hits.Count > 0 ? hits[0] : null, hits);
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


