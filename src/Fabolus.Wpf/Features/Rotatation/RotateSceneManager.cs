using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Features.Overhangs;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Features.Viewport;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.Maths;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Features.AppPreferences;

namespace Fabolus.Wpf.Features.Rotatation;

internal class RotateSceneManager : ISceneManager {
    private readonly IGeometryEngine _engine;
    private readonly IMessenger _messenger;
    
    private readonly PrintBedGrid _grid;
    private readonly ComputeOverhangColors _overhangFeature;

    // The +X and +Y axes drawn on the bed. Rotation is the one view where which way the model is
    // facing is the whole question, and the bed grid alone is symmetric enough to give no answer.
    // They follow the grid's own visibility: they are bed furniture, and a user who turned the
    // grid off is asking for a bare bed.
    private IReadOnlyList<Element3D> _axes = [];

    // Unlit material: renders per-vertex Colors directly, with no lighting term.
    // (A PhongMaterial would shade to black in a scene with no lights.)
    //
    // Per instance, not static. A Material is a WPF DependencyObject and so belongs to the thread
    // that created it; assigning one to a model on any other thread throws inside
    // AddInheritanceContext. The app only ever has one UI thread and one of these managers, so a
    // static worked there, but it made the field an accidental rendezvous between threads - which
    // is exactly what the tests are, since each runs its body on an STA thread of its own.
    private readonly HelixToolkit.Wpf.SharpDX.Material _skin = new VertColorMaterial();

    // The same unlit vertex colours, for when the mesh is see-through. VertColorMaterial cannot do
    // this: the viewport draws transparent models through weighted OIT, and that material's only
    // shader writes one plain colour rather than what the OIT targets expect. This one has a real
    // OIT pass. With the blending factor at 1 the vertex colour replaces the diffuse colour,
    // alpha included, so the opacity travels in the vertex colours just as the overhangs do.
    //
    // Both sides stay drawn, as they are when opaque. Culling the back faces reads a little cleaner
    // but takes the far wall away, which is half of what looking through the mesh is for, and an
    // open scan would show holes wherever its inside faces the camera.
    private readonly HelixToolkit.Wpf.SharpDX.Material _seeThroughSkin = new HelixToolkit.Wpf.SharpDX.DiffuseMaterial {
        EnableUnLit = true,
        VertexColorBlendingFactor = 1f,
    };

    private float _meshOpacity = 1f;

    // The viewport's gradient, repeated inside the scene while the mesh is see-through. The OIT
    // composite only writes colour, never alpha, so over an empty background the viewport's image
    // stays alpha 0 and WPF adds the mesh on top of the gradient instead of blending it: pale over
    // Graphite, and washed out to white over Light steel, overhang colours included. A backdrop
    // gives the composite real colour to blend with and leaves the image opaque, and it sits at the
    // far plane, so the bed and axes draw in front of it whichever order they come in.
    private ScreenQuadModel3D? _backdrop;
    private bool _isBackdropShown;
    private ViewportBackground _viewportBackground;

    private MeshGeometryModel3D _mesh = new();
    private LineGeometryModel3D _activeGizmo;
    private Guid _activeId;
    private Guid _activeGizmoId;

    private IMesh? ActiveMesh { get; set; }
    private OverhangSettings OverhangSettings { get; set; }
    private RotateTransform3D TempRotation { get; set; } = new();

    public event Action<Element3D> VisualAddedOrUpdated;
    public event Action<Guid> VisualRemovedById;
    public event Action VisualsCleared;

    public RotateSceneManager(IGeometryEngine engine, IMessenger messenger) {
        _engine = engine;
        _messenger = messenger;

        _grid = new PrintBedGrid(_messenger);
        _grid.Replaced += (replacedId, grid) => {
            VisualRemovedById?.Invoke(replacedId);
            VisualAddedOrUpdated?.Invoke(grid);

            // The grid is only ever replaced because the bed preferences changed, so the axes are
            // the wrong length now too.
            RebuildAxes();
        };

        // Taken off the message rather than read back from the store, so this doesn't depend on
        // which recipient the messenger notifies first.
        _viewportBackground = _messenger.GetSection(GeneralPreferences.Default).ViewportBackground;
        _messenger.Register<RotateSceneManager, PreferenceSectionUpdateMessage<GeneralPreferences>>(
            this, (recipient, message) => recipient.OnViewportBackgroundChanged(message.Section.ViewportBackground));

        _axes = BuildAxes();

        _overhangFeature = new ComputeOverhangColors(_engine);
        OverhangSettings = new OverhangSettings(
            OverhangDirection.MouldDefault,
            ColourGradient.Overhang);
    }

    public void ShowAxisRotation(Vector3 axis) {
        if (_activeGizmoId != Guid.Empty) {
            VisualRemovedById?.Invoke(_activeGizmoId);
            _activeGizmo = new();
            _activeGizmoId = Guid.Empty;
        }

        

        if (axis == Vector3.Zero || ActiveMesh is null) return;

        _activeGizmo = GenerateAxisGizmo(axis, ActiveMesh);
        _activeGizmoId = _activeGizmo.GUID;
        VisualAddedOrUpdated?.Invoke(_activeGizmo);
    }

    public void OnActivated() {
        VisualsCleared?.Invoke();
        _isBackdropShown = false;
        VisualAddedOrUpdated?.Invoke(_grid.Current);

        foreach (var axis in _axes) {
            VisualAddedOrUpdated?.Invoke(axis);
        }
    }

    private IReadOnlyList<Element3D> BuildAxes() =>
        SceneHelpers.GenerateBedAxes(_grid.Bed.Width, _grid.Bed.Depth, _grid.Bed.ShowGrid);

    private void RebuildAxes() {
        foreach (var axis in _axes) {
            VisualRemovedById?.Invoke(axis.GUID);
        }

        _axes = BuildAxes();

        foreach (var axis in _axes) {
            VisualAddedOrUpdated?.Invoke(axis);
        }
    }

    public void ApplyTempRotation(Vector3D axis, float degree) {
        var rotation = new AxisAngleRotation3D(axis, degree);
        TempRotation = new RotateTransform3D(rotation);

        RenderActiveMesh();
    }

    /// <summary>
    /// Rebuilds the overhang gradient from the warning/critical slider angles and
    /// re-renders. Slider angles are overhang angles measured from horizontal:
    /// 0 deg = a vertical wall (safe), 90 deg = a downward-facing ceiling (worst).
    /// The feature measures the angle between a vertex normal and the overhang (down)
    /// direction, so a slider angle s maps to a feature angle of (90 - s).
    /// </summary>
    public void SetOverhangs(float warningAngle, float criticalAngle) {
        var safe = new RgbColour(0.8f, 0.8f, 0.8f);     // base grey
        var warning = new RgbColour(1f, 1f, 0f);        // yellow
        var critical = new RgbColour(1f, 0f, 0f);       // red

        // Convert slider angles to gradient positions in the feature's [0, 90] band.
        // warning < critical, so warningT > criticalT (both below the safe stop at 1.0).
        float criticalT = Math.Clamp((90f - criticalAngle) / 90f, 0f, 1f);
        float warningT = Math.Clamp((90f - warningAngle) / 90f, 0f, 1f);

        var gradientResult = ColourGradient.Create(
            new ColourStop(criticalT, critical),
            new ColourStop(warningT, warning),
            new ColourStop(1f, safe));

        if (gradientResult.IsFailure) {
            return; // invalid thresholds; keep the previous gradient
        }

        OverhangSettings = new OverhangSettings(
            OverhangDirection.MouldDefault,
            gradientResult.Value,
            MinAngleDegrees: 0f,
            MaxAngleDegrees: 90f);

        RenderActiveMesh();
    }

    /// <summary>
    /// How solid the mesh is drawn, from 0 (invisible) to 1. Fully opaque is the plain draw, so
    /// the blended pass is only ever used when something is actually meant to show through.
    /// </summary>
    public void SetMeshOpacity(float opacity) {
        _meshOpacity = Math.Clamp(opacity, 0f, 1f);

        RenderActiveMesh();
    }

    /// <summary>
    /// Takes ownership of <paramref name="mesh"/>: it's retained for temp-rotation and
    /// overhang re-renders, and disposed when replaced or on <see cref="ReleaseMesh"/>.
    /// </summary>
    public void UpdateMesh(IMesh mesh) {
        ActiveMesh = mesh;

        RenderActiveMesh();
    }

    /// <summary>
    /// Disposes the retained mesh. Called when the owning view deactivates - the scene
    /// manager dies with its view model, so nothing else will release it.
    /// </summary>
    public void ReleaseMesh() {
        ActiveMesh = null;
    }

    private void RenderActiveMesh() {
        if (ActiveMesh is null) {
            return;
        }
        var mesh = ActiveMesh;

        if (_activeId != Guid.Empty) {
            VisualRemovedById?.Invoke(_activeId);
        }

        var matrix = TempRotation.Value;
        matrix.Invert();

        var v = matrix.Transform(new Vector3D(0, 0, 1));
        var direction = OverhangDirection.Create(
            new Vector3(v.X, v.Y, v.Z)).Value;
        var colouringResult = _overhangFeature.Execute(
            mesh,
            OverhangSettings with { Direction = direction });
        if (colouringResult.IsFailure) {
            return;
        }

        var lv = matrix.Transform(new Vector3D(_lightDirection.X, _lightDirection.Y, _lightDirection.Z));
        var lightLocal = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3((float)lv.X, (float)lv.Y, (float)lv.Z));

        var geometryResult = mesh.ToHelixMesh(_engine, colouringResult.Value.Colors);
        if (geometryResult.IsFailure) {
            return;
        }

        var geometry = geometryResult.Value;
        ApplyFixedShading(geometry, lightLocal, _meshOpacity); // both skins are unlit, so fold form into the colours

        var seeThrough = _meshOpacity < 1f;
        ShowBackdrop(seeThrough);

        _mesh = new MeshGeometryModel3D {
            Geometry = geometry,
            Material = seeThrough ? _seeThroughSkin : _skin,
            IsTransparent = seeThrough,
            Transform = TempRotation,
        };

        SceneVisual.SetIsModelGeometry(_mesh, true);
        _activeId = _mesh.GUID;
        VisualAddedOrUpdated?.Invoke(_mesh);
    }

    private void ShowBackdrop(bool show) {
        if (show == _isBackdropShown) {
            return;
        }

        _isBackdropShown = show;
        if (show) {
            _backdrop ??= BuildBackdrop();
            VisualAddedOrUpdated?.Invoke(_backdrop);
        } else if (_backdrop is not null) {
            VisualRemovedById?.Invoke(_backdrop.GUID);
        }
    }

    private void OnViewportBackgroundChanged(ViewportBackground background) {
        if (background == _viewportBackground) {
            return;
        }

        _viewportBackground = background;

        var wasShown = _isBackdropShown;
        ShowBackdrop(false);
        _backdrop = null;
        ShowBackdrop(wasShown);
    }

    private ScreenQuadModel3D BuildBackdrop() {
        var (top, bottom) = ViewportBackgroundColours.GradientOf(_viewportBackground);

        const int rows = 256;
        var pixels = new Color4[rows];
        for (int row = 0; row < rows; row++) {
            float t = row / (rows - 1f);
            pixels[row] = new Color4(
                (top.R + ((bottom.R - top.R) * t)) / 255f,
                (top.G + ((bottom.G - top.G) * t)) / 255f,
                (top.B + ((bottom.B - top.B) * t)) / 255f,
                1f);
        }

        return new ScreenQuadModel3D { Texture = new TextureModel(pixels, 1, rows) };
    }

    // World-fixed key light + ambient floor. VertColorMaterial is unlit, so we bake Lambert
    // shading into the vertex colours. The light is transformed by R^-1 each update so it stays
    // put in the world while the mesh rotates. Tweak _lightDirection / _ambient to taste.
    private static readonly Vector3 _lightDirection = new Vector3(0.3, 0.4, 0.85).Normalize();
    private const float _ambient = 0.35f;

    private static void ApplyFixedShading(HelixToolkit.SharpDX.MeshGeometry3D geometry, System.Numerics.Vector3 lightDirection, float opacity) {
        if (geometry.Colors is null || geometry.Normals is null) {
            return;
        }

        var colors = geometry.Colors;
        var normals = geometry.Normals;

        for (int i = 0; i < colors.Count; i++) {
            float nDotL = Math.Max(0f, System.Numerics.Vector3.Dot(normals[i], lightDirection));
            float shade = _ambient + (1f - _ambient) * nDotL;

            var c = colors[i];
            colors[i] = new Color4(c.Red * shade, c.Green * shade, c.Blue * shade, opacity);
        }
    }

    private LineGeometryModel3D GenerateAxisGizmo(Vector3 axis, IMesh activeMesh) {

        var stats = activeMesh.Stats(_engine)
            ?? throw new InvalidOperationException("The rotation gizmo is sized from the mesh's bounds, which have not been measured.");
 
        // Calculate radius based on bounding box
        double radius = axis switch {
            var a when a == Vector3.UnitX => Math.Max(stats.BoundsMax.Y - stats.BoundsMin.Y, stats.BoundsMax.Z - stats.BoundsMin.Z),
            var a when a == Vector3.UnitY => Math.Max(stats.BoundsMax.X - stats.BoundsMin.X, stats.BoundsMax.Z - stats.BoundsMin.Z),
            _ => Math.Max(stats.BoundsMax.X - stats.BoundsMin.X, stats.BoundsMax.Y - stats.BoundsMin.Y)
        } * 0.7f;

        var geometry = CreateCircleGeometry(axis, (float)radius);

        var color = axis switch {
            var a when a == Vector3.UnitX => Colors.Red,
            var a when a == Vector3.UnitY => Colors.Green,
            _ => Colors.Blue,
        };

        return new LineGeometryModel3D {
            Geometry = geometry,
            IsHitTestVisible = false,
            Color = color,
            Thickness = 4.0
        };
    }

    private static LineGeometry3D CreateCircleGeometry(Vector3 axis, float radius) {

        var builder = new LineBuilder();
        Vector3 helper = Math.Abs(axis.X) > 0.9f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 right = axis.Cross(helper.Normalize());
        var dxRight = new System.Numerics.Vector3((float)right.X, (float)right.Y, (float)right.Z);
        Vector3 up = axis.Cross(right);
        var dxUp = new System.Numerics.Vector3((float)up.X, (float)up.Y, (float)up.Z);

        for (int i = 0; i < 64; i++) {
            float a1 = (float)(2 * Math.PI * i / 64);
            float a2 = (float)(2 * Math.PI * (i + 1) / 64);

            builder.AddLine(
                (dxRight * (float)Math.Cos(a1) + dxUp * (float)Math.Sin(a1)) * radius,
                (dxRight * (float)Math.Cos(a2) + dxUp * (float)Math.Sin(a2)) * radius
            );
        }
        return builder.ToLineGeometry3D();
    }

    public void OnDeactivated() {
    }

    public bool OnKeyDown(Key key) => false;
    public bool OnKeyUp(Key key) => false;
    public bool OnMouseDown(MouseDown3DEventArgs eventArgs) => false;
    public bool OnMouseMove(IList<HelixToolkit.SharpDX.HitTestResult> hits) => false;
    public bool OnMouseUp(MouseUp3DEventArgs eventArgs) => false;
}
