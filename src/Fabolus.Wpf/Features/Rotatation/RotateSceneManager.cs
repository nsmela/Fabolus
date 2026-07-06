using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Features.Overhangs;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Features.Viewport;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Fabolus.Wpf.Features.Rotatation;

internal class RotateSceneManager : ISceneManager {
    private readonly IGeometryEngine _engine;
    private readonly Element3D _grid;
    private readonly ComputeOverhangColors _overhangFeature;

    // Unlit material: renders per-vertex Colors directly, with no lighting term.
    // (A PhongMaterial would shade to black in a scene with no lights.)
    private static readonly HelixToolkit.Wpf.SharpDX.Material _skin = new VertColorMaterial();

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

    public RotateSceneManager(IGeometryEngine engine) {
        _engine = engine;
        _grid = SceneHelpers.GenerateGrid();
        _overhangFeature = new ComputeOverhangColors(_engine);
        OverhangSettings = new OverhangSettings(
            OverhangDirection.MouldDefault,
            ColourGradient.Overhang);
    }

    public void ShowAxisRotation(System.Numerics.Vector3 axis) {
        if (_activeGizmoId != Guid.Empty) {
            VisualRemovedById?.Invoke(_activeGizmoId);
            _activeGizmo = new();
            _activeGizmoId = Guid.Empty;
        }

        var vector = new SharpDX.Vector3(axis.X, axis.Y, axis.Z);

        if (vector == Vector3.Zero || ActiveMesh is null) return;

        _activeGizmo = GenerateAxisGizmo(vector, ActiveMesh);
        _activeGizmoId = _activeGizmo.GUID;
        VisualAddedOrUpdated?.Invoke(_activeGizmo);
    }

    public void OnActivated() {
        VisualsCleared?.Invoke();
        VisualAddedOrUpdated?.Invoke(_grid);
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
            new System.Numerics.Vector3((float)v.X, (float)v.Y, (float)v.Z)).Value;
        var colouringResult = _overhangFeature.Execute(
            mesh,
            OverhangSettings with { Direction = direction });
        if (colouringResult.IsFailure) {
            return;
        }

        var lv = matrix.Transform(new Vector3D(_lightDirection.X, _lightDirection.Y, _lightDirection.Z));
        var lightLocal = Vector3.Normalize(new Vector3((float)lv.X, (float)lv.Y, (float)lv.Z));

        var geometryResult = mesh.ToHelixMesh(_engine, colouringResult.Value.Colors);
        if (geometryResult.IsFailure) {
            return;
        }

        var geometry = geometryResult.Value;
        ApplyFixedShading(geometry, lightLocal); // VertColorMaterial is unlit, so fold form into the colours

        _mesh = new MeshGeometryModel3D {
            Geometry = geometry,
            Material = _skin,
            Transform = TempRotation,
        };

        _activeId = _mesh.GUID;
        VisualAddedOrUpdated?.Invoke(_mesh);
    }

    // World-fixed key light + ambient floor. VertColorMaterial is unlit, so we bake Lambert
    // shading into the vertex colours. The light is transformed by R^-1 each update so it stays
    // put in the world while the mesh rotates. Tweak _lightDirection / _ambient to taste.
    private static readonly Vector3 _lightDirection = Vector3.Normalize(new Vector3(0.3f, 0.4f, 0.85f));
    private const float _ambient = 0.35f;

    private static void ApplyFixedShading(HelixToolkit.Wpf.SharpDX.MeshGeometry3D geometry, Vector3 lightDirection) {
        if (geometry.Colors is null || geometry.Normals is null) {
            return;
        }

        var colors = geometry.Colors;
        var normals = geometry.Normals;

        for (int i = 0; i < colors.Count; i++) {
            float nDotL = Math.Max(0f, Vector3.Dot(normals[i], lightDirection));
            float shade = _ambient + (1f - _ambient) * nDotL;

            var c = colors[i];
            colors[i] = new Color4(c.Red * shade, c.Green * shade, c.Blue * shade, c.Alpha);
        }
    }

    private static LineGeometryModel3D GenerateAxisGizmo(Vector3 axis, IMesh activeMesh) {

        var stats = activeMesh.Metadata.MeshStats().Value;
 
        // Calculate radius based on bounding box
        double radius = axis switch {
            var a when a == Vector3.UnitX => Math.Max(stats.MaxY - stats.MinY, stats.MaxZ - stats.MinZ),
            var a when a == Vector3.UnitY => Math.Max(stats.MaxX - stats.MinX, stats.MaxZ - stats.MinZ),
            _ => Math.Max(stats.MaxX - stats.MinX, stats.MaxY - stats.MinY)
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
        Vector3 right = Vector3.Normalize(Vector3.Cross(axis, helper));
        Vector3 up = Vector3.Cross(axis, right);

        for (int i = 0; i < 64; i++) {
            float a1 = (float)(2 * Math.PI * i / 64);
            float a2 = (float)(2 * Math.PI * (i + 1) / 64);

            builder.AddLine(
                (right * (float)Math.Cos(a1) + up * (float)Math.Sin(a1)) * radius,
                (right * (float)Math.Cos(a2) + up * (float)Math.Sin(a2)) * radius
            );
        }
        return builder.ToLineGeometry3D();
    }

    public void OnDeactivated() {
    }

    public bool OnKeyDown(Key key) => false;
    public bool OnKeyUp(Key key) => false;
    public bool OnMouseDown(MouseDown3DEventArgs eventArgs) => false;
    public bool OnMouseMove(HelixToolkit.Wpf.SharpDX.HitTestResult? hit) => false;
    public bool OnMouseUp(MouseUp3DEventArgs eventArgs) => false;
}