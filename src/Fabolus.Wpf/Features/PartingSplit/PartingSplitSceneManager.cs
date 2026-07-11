using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Features.Viewport;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using CoreVector3 = System.Numerics.Vector3;

namespace Fabolus.Wpf.Features.PartingSplit;

/// <summary>
/// Renders the mesh being split, a rotation-only gizmo for the pull direction, and - once
/// generated - the parting line loops, a translucent preview of the two resulting regions,
/// and the combined tool solid used to cut them.
/// </summary>
public class PartingSplitSceneManager : ISceneManager
{
    private readonly IGeometryEngine _engine;
    private readonly Material _meshSkin = new PhongMaterial { DiffuseColor = new Color4(0.75f, 0.78f, 0.82f, 0.55f) };
    private readonly Material _positiveSkin = DiffuseMaterials.SkyBlue;
    private readonly Material _negativeSkin = DiffuseMaterials.Orange;
    private readonly Material _toolSkin = new PhongMaterial { DiffuseColor = new Color4(0.1f, 0.9f, 0.3f, 0.25f) };

    private readonly Element3D _grid;
    private MeshGeometryModel3D? _meshModel;
    private MeshGeometryModel3D? _positiveRegionModel;
    private MeshGeometryModel3D? _negativeRegionModel;
    private MeshGeometryModel3D? _toolModel;
    private LineGeometryModel3D? _partingLineModel;
    private UICompositeManipulator3D? _manipulator;
    private bool _isUpdating;

    private IMesh? _activeMesh;
    private CoreVector3 _direction = CoreVector3.UnitY;

    public event Action<Element3D>? VisualAddedOrUpdated;
    public event Action<Guid>? VisualRemovedById;
    public event Action? VisualsCleared;

    /// <summary>Raised when the user drags the direction gizmo.</summary>
    public event Action<CoreVector3>? DirectionChanged;

    public PartingSplitSceneManager(IGeometryEngine engine)
    {
        _engine = engine;
        _grid = SceneHelpers.GenerateGrid();
    }

    public void UpdateMesh(IMesh mesh)
    {
        _activeMesh = mesh;
        RebuildMeshVisual();
    }

    public void ReleaseMesh()
    {
        _activeMesh = null;

        if (_meshModel != null) VisualRemovedById?.Invoke(_meshModel.GUID);
        if (_manipulator != null) VisualRemovedById?.Invoke(_manipulator.GUID);
        ClearPartingPreview();

        _meshModel = null;
        _manipulator = null;
    }

    public void UpdateDirection(CoreVector3 direction)
    {
        if (direction == CoreVector3.Zero) return;
        _direction = CoreVector3.Normalize(direction);
        UpdateManipulatorTransform();
    }

    /// <summary>Removes the parting line / region / tool preview visuals, leaving just the mesh and gizmo.</summary>
    public void ClearPartingPreview()
    {
        if (_partingLineModel != null) { VisualRemovedById?.Invoke(_partingLineModel.GUID); _partingLineModel = null; }
        if (_positiveRegionModel != null) { VisualRemovedById?.Invoke(_positiveRegionModel.GUID); _positiveRegionModel = null; }
        if (_negativeRegionModel != null) { VisualRemovedById?.Invoke(_negativeRegionModel.GUID); _negativeRegionModel = null; }
        if (_toolModel != null) { VisualRemovedById?.Invoke(_toolModel.GUID); _toolModel = null; }
    }

    /// <summary>
    /// Shows the parting line loops as tubes, the combined tool solid translucently, and (when
    /// they can be built) the two resulting regions in different colors.
    /// </summary>
    public void ShowPartingPreview(PartingLine partingLine, CoreVector3 direction, IMesh? tool, IMesh? positiveRegion, IMesh? negativeRegion)
    {
        ClearPartingPreview();
        if (_activeMesh is null) return;

        var lineBuilder = new LineBuilder();
        foreach (var loop in partingLine.Loops)
        {
            if (loop.Count < 2) continue;

            for (int i = 0; i < loop.Count; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % loop.Count];
                lineBuilder.AddLine(new Vector3(a.X, a.Y, a.Z), new Vector3(b.X, b.Y, b.Z));
            }
        }

        _partingLineModel = new LineGeometryModel3D
        {
            Geometry = lineBuilder.ToLineGeometry3D(),
            Color = Colors.Yellow,
            Thickness = 2.5,
            IsHitTestVisible = false
        };
        VisualAddedOrUpdated?.Invoke(_partingLineModel);

        if (tool != null)
        {
            var toolGeometry = tool.ToHelixMesh(_engine);
            if (toolGeometry.IsSuccess)
            {
                _toolModel = new MeshGeometryModel3D
                {
                    Geometry = toolGeometry.Value,
                    Material = _toolSkin,
                    IsTransparent = true,
                    CullMode = SharpDX.Direct3D11.CullMode.None,
                    IsHitTestVisible = false
                };
                VisualAddedOrUpdated?.Invoke(_toolModel);
            }
        }

        if (positiveRegion != null)
        {
            var geo = positiveRegion.ToHelixMesh(_engine);
            if (geo.IsSuccess)
            {
                _positiveRegionModel = new MeshGeometryModel3D { Geometry = geo.Value, Material = _positiveSkin, IsHitTestVisible = false };
                VisualAddedOrUpdated?.Invoke(_positiveRegionModel);
            }
        }

        if (negativeRegion != null)
        {
            var geo = negativeRegion.ToHelixMesh(_engine);
            if (geo.IsSuccess)
            {
                _negativeRegionModel = new MeshGeometryModel3D { Geometry = geo.Value, Material = _negativeSkin, IsHitTestVisible = false };
                VisualAddedOrUpdated?.Invoke(_negativeRegionModel);
            }
        }

        // Hide the plain mesh while the colored region preview is showing, to avoid z-fighting.
        if (_meshModel != null && (_positiveRegionModel != null || _negativeRegionModel != null))
        {
            VisualRemovedById?.Invoke(_meshModel.GUID);
        }
    }

    private void RebuildMeshVisual()
    {
        if (_meshModel != null) VisualRemovedById?.Invoke(_meshModel.GUID);
        if (_activeMesh is null) return;

        var geometryResult = _activeMesh.ToHelixMesh(_engine);
        if (geometryResult.IsFailure) return;

        _meshModel = new MeshGeometryModel3D
        {
            Geometry = geometryResult.Value,
            Material = _meshSkin,
            IsTransparent = true,
        };
        VisualAddedOrUpdated?.Invoke(_meshModel);

        EnsureManipulator();
    }

    private void EnsureManipulator()
    {
        if (_activeMesh is null) return;

        if (_manipulator == null)
        {
            _manipulator = new UICompositeManipulator3D
            {
                CanTranslateX = false,
                CanTranslateY = false,
                CanTranslateZ = false,
                CanRotateX = true,
                CanRotateY = true,
                CanRotateZ = true,
                Diameter = 40.0
            };

            var binding = new System.Windows.Data.Binding(nameof(UICompositeManipulator3D.TargetTransform))
            {
                Source = _manipulator,
                Mode = System.Windows.Data.BindingMode.OneWay
            };
            System.Windows.Data.BindingOperations.SetBinding(_manipulator, Element3D.TransformProperty, binding);

            var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                UICompositeManipulator3D.TargetTransformProperty, typeof(UICompositeManipulator3D));

            descriptor.AddValueChanged(_manipulator, (s, e) =>
            {
                if (_isUpdating) return;

                System.Windows.Media.Media3D.Matrix3D m;
                if (_manipulator.TargetTransform is System.Windows.Media.Media3D.MatrixTransform3D mt) m = mt.Matrix;
                else if (_manipulator.TargetTransform is System.Windows.Media.Media3D.Transform3DGroup tg) m = tg.Value;
                else return;

                // Rotating the gizmo carries UnitY (our baseline direction) with it.
                var rotatedY = new CoreVector3((float)m.M21, (float)m.M22, (float)m.M23);
                if (rotatedY == CoreVector3.Zero) return;

                _isUpdating = true;
                DirectionChanged?.Invoke(CoreVector3.Normalize(rotatedY));
                _isUpdating = false;
            });

            VisualAddedOrUpdated?.Invoke(_manipulator);
        }

        UpdateManipulatorTransform();
    }

    private void UpdateManipulatorTransform()
    {
        if (_manipulator is null) return;

        var axis = CoreVector3.Cross(CoreVector3.UnitY, _direction);
        float dot = CoreVector3.Dot(CoreVector3.UnitY, _direction);
        System.Numerics.Quaternion q;
        if (dot < -0.9999f) q = System.Numerics.Quaternion.CreateFromAxisAngle(CoreVector3.UnitX, (float)Math.PI);
        else if (dot > 0.9999f) q = System.Numerics.Quaternion.Identity;
        else q = System.Numerics.Quaternion.Normalize(new System.Numerics.Quaternion(axis, 1 + dot));

        var transform = new System.Windows.Media.Media3D.RotateTransform3D(
            new System.Windows.Media.Media3D.QuaternionRotation3D(new System.Windows.Media.Media3D.Quaternion(q.X, q.Y, q.Z, q.W)));

        if (!_isUpdating)
        {
            _manipulator.TargetTransform = transform;
        }
    }

    public void OnActivated()
    {
        VisualsCleared?.Invoke();
        VisualAddedOrUpdated?.Invoke(_grid);
        if (_meshModel != null) VisualAddedOrUpdated?.Invoke(_meshModel);
        if (_manipulator != null) VisualAddedOrUpdated?.Invoke(_manipulator);
        if (_partingLineModel != null) VisualAddedOrUpdated?.Invoke(_partingLineModel);
        if (_toolModel != null) VisualAddedOrUpdated?.Invoke(_toolModel);
        if (_positiveRegionModel != null) VisualAddedOrUpdated?.Invoke(_positiveRegionModel);
        if (_negativeRegionModel != null) VisualAddedOrUpdated?.Invoke(_negativeRegionModel);
    }

    public void OnDeactivated() { }

    public bool OnKeyDown(Key key) => false;
    public bool OnKeyUp(Key key) => false;
    public bool OnMouseDown(MouseDown3DEventArgs eventArgs) => false;
    public bool OnMouseMove(HelixToolkit.Wpf.SharpDX.HitTestResult? hit) => false;
    public bool OnMouseUp(MouseUp3DEventArgs eventArgs) => false;
}
