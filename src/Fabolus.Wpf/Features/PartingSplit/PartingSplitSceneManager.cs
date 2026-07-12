using Fabolus.Core.Features.PartingSplit;
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
using PartingLine = Fabolus.Core.Geometry.PartingLine;

namespace Fabolus.Wpf.Features.PartingSplit;

/// <summary>
/// Renders the multi-step parting split view.
/// </summary>
public class PartingSplitSceneManager : ISceneManager
{
    private readonly IGeometryEngine _engine;
    private readonly ComputePartingDirectionColors _colorsFeature;

    private readonly Material _meshSkin = DiffuseMaterials.LightGray;
    private readonly Material _positiveSkin = DiffuseMaterials.SkyBlue;
    private readonly Material _negativeSkin = DiffuseMaterials.Orange;
    private readonly Material _toolSkin = new PhongMaterial { DiffuseColor = new Color4(1.0f, 0.0f, 0.0f, 0.8f) };
    private readonly Material _heatMapSkin = new VertColorMaterial();

    private readonly Element3D _grid;
    private MeshGeometryModel3D? _baseMeshModel;
    private MeshGeometryModel3D? _mouldMeshModel;
    private MeshGeometryModel3D? _positiveRegionModel;
    private MeshGeometryModel3D? _negativeRegionModel;
    private MeshGeometryModel3D? _toolModel;
    private LineGeometryModel3D? _partingLineModel;
    private MeshGeometryModel3D? _arrowModel;
    private UICompositeManipulator3D? _manipulator;
    
    private bool _isUpdating;
    private PartingSplitState _currentState;

    private IMesh? _activeMouldMesh;
    private IMesh? _baseTransformMesh;
    private CoreVector3 _direction = CoreVector3.UnitY;

    public event Action<Element3D>? VisualAddedOrUpdated;
    public event Action<Guid>? VisualRemovedById;
    public event Action? VisualsCleared;

    /// <summary>Raised when the user drags the direction gizmo.</summary>
    public event Action<CoreVector3>? DirectionChanged;

    public PartingSplitSceneManager(IGeometryEngine engine, ComputePartingDirectionColors colorsFeature)
    {
        _engine = engine;
        _colorsFeature = colorsFeature;
        _grid = SceneHelpers.GenerateGrid();
    }

    public void UpdateMeshes(IMesh mouldMesh, IMesh baseTransformMesh)
    {
        _activeMouldMesh = mouldMesh;
        _baseTransformMesh = baseTransformMesh;

        if (_baseMeshModel != null) VisualRemovedById?.Invoke(_baseMeshModel.GUID);
        if (_mouldMeshModel != null) VisualRemovedById?.Invoke(_mouldMeshModel.GUID);

        var baseGeo = _baseTransformMesh.ToHelixMesh(_engine);
        if (baseGeo.IsSuccess)
        {
            _baseMeshModel = new MeshGeometryModel3D { Geometry = baseGeo.Value, Material = _meshSkin };
        }

        var mouldGeo = _activeMouldMesh.ToHelixMesh(_engine);
        if (mouldGeo.IsSuccess)
        {
            _mouldMeshModel = new MeshGeometryModel3D { Geometry = mouldGeo.Value, Material = _meshSkin };
        }

        EnsureManipulator();
        RecomputeDirectionColors();
    }

    public void ReleaseMeshes()
    {
        _activeMouldMesh = null;
        _baseTransformMesh = null;

        if (_baseMeshModel != null) VisualRemovedById?.Invoke(_baseMeshModel.GUID);
        if (_mouldMeshModel != null) VisualRemovedById?.Invoke(_mouldMeshModel.GUID);
        if (_manipulator != null) VisualRemovedById?.Invoke(_manipulator.GUID);
        if (_arrowModel != null) VisualRemovedById?.Invoke(_arrowModel.GUID);
        
        ClearPartingPreview();

        _baseMeshModel = null;
        _mouldMeshModel = null;
        _manipulator = null;
        _arrowModel = null;
    }

    public void UpdateDirection(CoreVector3 direction)
    {
        if (direction == CoreVector3.Zero) return;
        _direction = CoreVector3.Normalize(direction);
        UpdateManipulatorTransform();
        
        if (_currentState == PartingSplitState.DirectionSelection)
        {
            RecomputeDirectionColors();
        }
    }

    private void RecomputeDirectionColors()
    {
        if (_baseTransformMesh == null || _baseMeshModel?.Geometry == null) return;
        var colorsResult = _colorsFeature.Execute(_baseTransformMesh, _direction);
        if (colorsResult.IsSuccess && _baseMeshModel.Geometry is MeshGeometry3D geo)
        {
            var colors = colorsResult.Value;
            var colorCollection = new Color4Collection();
            for (int i = 0; i < colors.Length; i += 3)
                colorCollection.Add(new Color4((float)colors[i], (float)colors[i + 1], (float)colors[i + 2], 1.0f));
            
            geo.Colors = colorCollection;
        }
    }

    public void ClearPartingPreview()
    {
        if (_partingLineModel != null) { VisualRemovedById?.Invoke(_partingLineModel.GUID); _partingLineModel = null; }
        if (_positiveRegionModel != null) { VisualRemovedById?.Invoke(_positiveRegionModel.GUID); _positiveRegionModel = null; }
        if (_negativeRegionModel != null) { VisualRemovedById?.Invoke(_negativeRegionModel.GUID); _negativeRegionModel = null; }
        if (_toolModel != null) { VisualRemovedById?.Invoke(_toolModel.GUID); _toolModel = null; }
    }

    public void SetPreviewData(PartingLine partingLine, IMesh? tool, IMesh? positiveRegion, IMesh? negativeRegion)
    {
        ClearPartingPreview();

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
                    CullMode = SharpDX.Direct3D11.CullMode.Back,
                    IsHitTestVisible = false
                };
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("FAILED TO CREATE HELIX MESH FOR TOOL");
            }
        }

        if (positiveRegion != null)
        {
            var geo = positiveRegion.ToHelixMesh(_engine);
            if (geo.IsSuccess)
            {
                _positiveRegionModel = new MeshGeometryModel3D { Geometry = geo.Value, Material = _positiveSkin, IsHitTestVisible = false };
            }
        }

        if (negativeRegion != null)
        {
            var geo = negativeRegion.ToHelixMesh(_engine);
            if (geo.IsSuccess)
            {
                _negativeRegionModel = new MeshGeometryModel3D { Geometry = geo.Value, Material = _negativeSkin, IsHitTestVisible = false };
            }
        }
    }

    private void SetVisibility(Element3D? element, bool isVisible)
    {
        if (element != null)
        {
            element.Visibility = isVisible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            if (element is HelixToolkit.Wpf.SharpDX.GeometryModel3D geom)
            {
                geom.IsRendering = isVisible;
            }
            else if (element is HelixToolkit.Wpf.SharpDX.UICompositeManipulator3D manip)
            {
                manip.IsRendering = isVisible;
            }
        }
    }

    public void UpdateState(PartingSplitState state)
    {
        _currentState = state;

        // Ensure all available models are added to the scene (VisualAddedOrUpdated gracefully ignores duplicates).
        VisualAddedOrUpdated?.Invoke(_grid);
        if (_baseMeshModel != null) VisualAddedOrUpdated?.Invoke(_baseMeshModel);
        if (_mouldMeshModel != null) VisualAddedOrUpdated?.Invoke(_mouldMeshModel);
        if (_manipulator != null) VisualAddedOrUpdated?.Invoke(_manipulator);
        if (_arrowModel != null) VisualAddedOrUpdated?.Invoke(_arrowModel);
        if (_partingLineModel != null) VisualAddedOrUpdated?.Invoke(_partingLineModel);
        if (_toolModel != null) VisualAddedOrUpdated?.Invoke(_toolModel);
        if (_positiveRegionModel != null) VisualAddedOrUpdated?.Invoke(_positiveRegionModel);
        if (_negativeRegionModel != null) VisualAddedOrUpdated?.Invoke(_negativeRegionModel);

        // Hide everything initially
        SetVisibility(_baseMeshModel, false);
        SetVisibility(_mouldMeshModel, false);
        SetVisibility(_manipulator, false);
        SetVisibility(_arrowModel, false);
        SetVisibility(_partingLineModel, false);
        SetVisibility(_toolModel, false);
        SetVisibility(_positiveRegionModel, false);
        SetVisibility(_negativeRegionModel, false);

        switch (state)
        {
            case PartingSplitState.DirectionSelection:
                if (_baseMeshModel != null)
                {
                    _baseMeshModel.Material = _heatMapSkin;
                    RecomputeDirectionColors();
                    SetVisibility(_baseMeshModel, true);
                }
                SetVisibility(_manipulator, true);
                SetVisibility(_arrowModel, true);
                break;

            case PartingSplitState.PartingLinePreview:
                if (_baseMeshModel != null)
                {
                    _baseMeshModel.Material = _meshSkin;
                    SetVisibility(_baseMeshModel, true);
                }
                SetVisibility(_partingLineModel, true);
                break;

            case PartingSplitState.ToolWithBaseMesh:
                if (_baseMeshModel != null)
                {
                    _baseMeshModel.Material = _meshSkin;
                    SetVisibility(_baseMeshModel, true);
                }
                SetVisibility(_toolModel, true);
                break;

            case PartingSplitState.ToolWithMould:
                SetVisibility(_mouldMeshModel, true);
                SetVisibility(_toolModel, true);
                break;

            case PartingSplitState.FinalPartedMould:
                SetVisibility(_positiveRegionModel, true);
                SetVisibility(_negativeRegionModel, true);
                break;
        }
    }

    private void EnsureManipulator()
    {
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

            var arrowBuilder = new MeshBuilder();
            arrowBuilder.AddArrow(Vector3.Zero, new Vector3(0, 40, 0), 2);
            _arrowModel = new MeshGeometryModel3D
            {
                Geometry = arrowBuilder.ToMeshGeometry3D(),
                Material = DiffuseMaterials.Red
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

                var rotatedY = new CoreVector3((float)m.M21, (float)m.M22, (float)m.M23);
                if (rotatedY == CoreVector3.Zero) return;

                _isUpdating = true;
                DirectionChanged?.Invoke(CoreVector3.Normalize(rotatedY));
                _isUpdating = false;
            });
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
            if (_arrowModel != null) _arrowModel.Transform = transform;
        }
    }

    public void OnActivated()
    {
        VisualsCleared?.Invoke();
        UpdateState(_currentState);
    }

    public void OnDeactivated() { }

    public bool OnKeyDown(Key key) => false;
    public bool OnKeyUp(Key key) => false;
    public bool OnMouseDown(MouseDown3DEventArgs eventArgs) => false;
    public bool OnMouseMove(HelixToolkit.Wpf.SharpDX.HitTestResult? hit) => false;
    public bool OnMouseUp(MouseUp3DEventArgs eventArgs) => false;
}
