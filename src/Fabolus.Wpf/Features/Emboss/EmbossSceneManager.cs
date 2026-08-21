using System.Numerics;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common;
using Fabolus.Core.Features.Emboss;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common.Helpers;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Features.Viewport;
using HelixToolkit.Wpf.SharpDX;

namespace Fabolus.Wpf.Features.Emboss;

public sealed class EmbossSceneManager : ISceneManager
{
    private readonly IGeometryEngine _engine;
    private readonly IMessenger _messenger;

    public event Action<Element3D>? VisualAddedOrUpdated;
    public event Action<Guid>? VisualRemovedById;
    public event Action? VisualsCleared;

    public event Action<Vector3, Vector3>? DecalPlaced;
    public event Action<Vector3, Vector3>? DecalMoved;
    public event Action<float>? DecalRotated;
    public event Action<Vector3, Vector3>? DecalHovered;

    private Guid _targetMeshId = Guid.Empty;
    private MeshGeometryModel3D? _targetModel;
    private Guid _previewDecalId = Guid.Empty;
    private MeshGeometryModel3D? _decalModel;
    private Guid _gizmoLineId = Guid.Empty;
    private LineGeometryModel3D? _gizmoLineModel;
    private Guid _rotateHandleId = Guid.Empty;
    private MeshGeometryModel3D? _rotateHandleModel;

    private readonly HelixToolkit.Wpf.SharpDX.Material _targetSkin;
    private readonly HelixToolkit.Wpf.SharpDX.Material _previewDecalSkin;
    private readonly HelixToolkit.Wpf.SharpDX.Material _rotateHandleSkin;

    public IMesh? TargetMesh { get; private set; }
    public TextDecal? CurrentDecal { get; private set; }
    public DecalFrame? CurrentFrame { get; private set; }

    public bool IsPicking { get; set; }

    private enum DragMode { None, Move, Rotate }
    private DragMode _currentDrag = DragMode.None;

    public EmbossSceneManager(IGeometryEngine engine, IMessenger messenger)
    {
        _engine = engine;
        _messenger = messenger;

        _targetSkin = Skins.Surface.Gray;
        _previewDecalSkin = Skins.Primitive.Cyan;
        _rotateHandleSkin = Skins.Primitive.Amber;
    }

    public Result UpdateMesh(IMesh mesh)
    {
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => UpdateMesh(mesh));
        }

        TargetMesh = mesh;
        var helixMeshResult = mesh.ToHelixMesh(_engine);
        if (helixMeshResult.IsFailure)
            return helixMeshResult;

        if (_targetModel == null || (Application.Current == null && !_targetModel.CheckAccess()))
        {
            _targetModel = new MeshGeometryModel3D
            {
                Geometry = helixMeshResult.Value,
                Material = _targetSkin,
                CullMode = SharpDX.Direct3D11.CullMode.Back
            };
            SceneVisual.SetIsModelGeometry(_targetModel, true);
            _targetMeshId = _targetModel.GUID;
            VisualAddedOrUpdated?.Invoke(_targetModel);
        }
        else
        {
            _targetModel.Geometry = helixMeshResult.Value;
            _targetModel.Visibility = Visibility.Visible;
            VisualAddedOrUpdated?.Invoke(_targetModel);
        }

        return Result.Success();
    }

    public void ReleaseMesh()
    {
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(ReleaseMesh);
            return;
        }

        ClearPreviewVisuals();
        if (_targetMeshId != Guid.Empty)
        {
            VisualRemovedById?.Invoke(_targetMeshId);
            _targetMeshId = Guid.Empty;
        }
        TargetMesh = null;
        _targetModel = null;
    }

    public void UpdatePreview(TextDecal decal, TextMetrics metrics, IGlyphOutlineSource outlineSource)
    {
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(() => UpdatePreview(decal, metrics, outlineSource));
            return;
        }
        CurrentDecal = decal;
        var frame = DecalFrame.FromHit(decal.Anchor, decal.AnchorNormal, decal.RotationDeg);
        CurrentFrame = frame;

        if (string.IsNullOrWhiteSpace(decal.Text) || TargetMesh == null)
        {
            ClearPreviewVisuals();
            return;
        }

        var outlines = outlineSource.GetOutlines(decal.Text, decal.Font, decal.CapHeight, decal.Tracking);
        if (outlines.Count == 0)
        {
            ClearPreviewVisuals();
            return;
        }

        float sink = -0.05f;
        float overshoot = 0.05f;
        float maxEdge = decal.ProjectOntoSurface ? Math.Max(0.4f, decal.CapHeight / 8.0f) : 0f;
        IMesh? surfaceTarget = decal.ProjectOntoSurface ? TargetMesh : null;

        var prismResult = _engine.Generators.BuildTextPrism(outlines, frame, decal.Depth, sink, overshoot, maxEdge, surfaceTarget);
        if (prismResult.IsFailure) return;

        var prismMesh = prismResult.Value;

        var helixPrismResult = prismMesh.ToHelixMesh(_engine);
        if (helixPrismResult.IsFailure) return;

        // 1. Decal Preview Model
        if (_decalModel == null)
        {
            _decalModel = new MeshGeometryModel3D
            {
                Geometry = helixPrismResult.Value,
                Material = _previewDecalSkin,
                IsTransparent = true,
                DepthBias = -50,
                SlopeScaledDepthBias = -1.0f,
                CullMode = SharpDX.Direct3D11.CullMode.None,
            };
            SceneVisual.SetIsModelGeometry(_decalModel, false);
            _previewDecalId = _decalModel.GUID;
            VisualAddedOrUpdated?.Invoke(_decalModel);
        }
        else
        {
            _decalModel.Geometry = helixPrismResult.Value;
            _decalModel.Visibility = Visibility.Visible;
            VisualAddedOrUpdated?.Invoke(_decalModel);
        }

        // 2. Gizmo Bounding Box Lines
        float halfW = metrics.WidthMm * 0.5f + 1.0f;
        float halfH = metrics.HeightMm * 0.5f + 1.0f;
        float zOff = decal.Depth + 0.5f;

        var lineGeometry = GenerateContouredBoundingBox(frame, halfW, halfH, zOff, decal.ProjectOntoSurface);

        if (_gizmoLineModel == null)
        {
            _gizmoLineModel = new LineGeometryModel3D
            {
                Geometry = lineGeometry,
                Color = System.Windows.Media.Colors.Cyan,
                Thickness = 1.5,
                IsHitTestVisible = false
            };
            _gizmoLineId = _gizmoLineModel.GUID;
            VisualAddedOrUpdated?.Invoke(_gizmoLineModel);
        }
        else
        {
            _gizmoLineModel.Geometry = lineGeometry;
            _gizmoLineModel.Visibility = Visibility.Visible;
            VisualAddedOrUpdated?.Invoke(_gizmoLineModel);
        }

        // 3. Rotation Handle Sphere
        var handlePos = frame.ToWorld(0, halfH + decal.CapHeight * 0.4f + 2.0f, zOff);
        var sphereResult = _engine.Generators.GenerateSphere(handlePos, Math.Max(1.0, decal.CapHeight * 0.25), 12);
        if (sphereResult.IsSuccess)
        {
            var helixSphere = sphereResult.Value.ToHelixMesh(_engine);
            if (helixSphere.IsSuccess)
            {
                if (_rotateHandleModel == null)
                {
                    _rotateHandleModel = new MeshGeometryModel3D
                    {
                        Geometry = helixSphere.Value,
                        Material = _rotateHandleSkin,
                        CullMode = SharpDX.Direct3D11.CullMode.Back
                    };
                    _rotateHandleId = _rotateHandleModel.GUID;
                    VisualAddedOrUpdated?.Invoke(_rotateHandleModel);
                }
                else
                {
                    _rotateHandleModel.Geometry = helixSphere.Value;
                    _rotateHandleModel.Visibility = Visibility.Visible;
                    VisualAddedOrUpdated?.Invoke(_rotateHandleModel);
                }
            }
        }
    }

    public void UpdateDragPreview(TextDecal decal, TextMetrics metrics)
    {
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(() => UpdateDragPreview(decal, metrics));
            return;
        }

        CurrentDecal = decal;
        var frame = DecalFrame.FromHit(decal.Anchor, decal.AnchorNormal, decal.RotationDeg);
        CurrentFrame = frame;

        if (TargetMesh == null) return;

        float halfW = metrics.WidthMm * 0.5f + 1.0f;
        float halfH = metrics.HeightMm * 0.5f + 1.0f;
        float zOff = 0.5f;

        // Hide rotation handle during drag
        if (_rotateHandleModel != null)
        {
            _rotateHandleModel.Visibility = Visibility.Collapsed;
        }

        var rectPolygon = new Polygon2D
        {
            OuterBoundary = new[]
            {
                new Vector2(-halfW, -halfH),
                new Vector2(halfW, -halfH),
                new Vector2(halfW, halfH),
                new Vector2(-halfW, halfH)
            }
        };

        float maxEdge = decal.ProjectOntoSurface ? Math.Max(0.5f, decal.CapHeight / 4.0f) : 0f;
        IMesh? surfaceTarget = decal.ProjectOntoSurface ? TargetMesh : null;

        var patchResult = _engine.Generators.BuildTextPrism(new[] { rectPolygon }, frame, 0.2f, -0.05f, 0.05f, maxEdge, surfaceTarget);
        if (patchResult.IsSuccess)
        {
            var helixPatch = patchResult.Value.ToHelixMesh(_engine);
            if (helixPatch.IsSuccess)
            {
                if (_decalModel == null)
                {
                    _decalModel = new MeshGeometryModel3D
                    {
                        Geometry = helixPatch.Value,
                        Material = _previewDecalSkin,
                        IsTransparent = true,
                        DepthBias = -50,
                        SlopeScaledDepthBias = -1.0f,
                        CullMode = SharpDX.Direct3D11.CullMode.None,
                    };
                    SceneVisual.SetIsModelGeometry(_decalModel, false);
                    _previewDecalId = _decalModel.GUID;
                    VisualAddedOrUpdated?.Invoke(_decalModel);
                }
                else
                {
                    _decalModel.Geometry = helixPatch.Value;
                    _decalModel.Visibility = Visibility.Visible;
                    VisualAddedOrUpdated?.Invoke(_decalModel);
                }
            }
        }

        // Bounding Box Line Geometry
        var lineGeometry = GenerateContouredBoundingBox(frame, halfW, halfH, zOff, decal.ProjectOntoSurface);
        if (_gizmoLineModel == null)
        {
            _gizmoLineModel = new LineGeometryModel3D
            {
                Geometry = lineGeometry,
                Color = System.Windows.Media.Colors.Cyan,
                Thickness = 2.0,
                IsHitTestVisible = false
            };
            _gizmoLineId = _gizmoLineModel.GUID;
            VisualAddedOrUpdated?.Invoke(_gizmoLineModel);
        }
        else
        {
            _gizmoLineModel.Geometry = lineGeometry;
            _gizmoLineModel.Visibility = Visibility.Visible;
            VisualAddedOrUpdated?.Invoke(_gizmoLineModel);
        }
    }

    private LineGeometry3D GenerateContouredBoundingBox(DecalFrame frame, float halfW, float halfH, float zOff, bool projectOntoSurface)
    {
        var linePositions = new Vector3Collection();
        var p0 = frame.ToWorld(-halfW, -halfH, zOff);
        var p1 = frame.ToWorld(halfW, -halfH, zOff);
        var p2 = frame.ToWorld(halfW, halfH, zOff);
        var p3 = frame.ToWorld(-halfW, halfH, zOff);

        linePositions.Add(new SharpDX.Vector3(p0.X, p0.Y, p0.Z));
        linePositions.Add(new SharpDX.Vector3(p1.X, p1.Y, p1.Z));
        linePositions.Add(new SharpDX.Vector3(p1.X, p1.Y, p1.Z));
        linePositions.Add(new SharpDX.Vector3(p2.X, p2.Y, p2.Z));
        linePositions.Add(new SharpDX.Vector3(p2.X, p2.Y, p2.Z));
        linePositions.Add(new SharpDX.Vector3(p3.X, p3.Y, p3.Z));
        linePositions.Add(new SharpDX.Vector3(p3.X, p3.Y, p3.Z));
        linePositions.Add(new SharpDX.Vector3(p0.X, p0.Y, p0.Z));

        return new LineGeometry3D { Positions = linePositions };
    }

    public void ClearPreviewVisuals()
    {
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(ClearPreviewVisuals);
            return;
        }

        if (_previewDecalId != Guid.Empty)
        {
            VisualRemovedById?.Invoke(_previewDecalId);
            _previewDecalId = Guid.Empty;
            _decalModel = null;
        }

        if (_gizmoLineId != Guid.Empty)
        {
            VisualRemovedById?.Invoke(_gizmoLineId);
            _gizmoLineId = Guid.Empty;
            _gizmoLineModel = null;
        }

        if (_rotateHandleId != Guid.Empty)
        {
            VisualRemovedById?.Invoke(_rotateHandleId);
            _rotateHandleId = Guid.Empty;
            _rotateHandleModel = null;
        }
    }

    public void OnActivated()
    {
        VisualsCleared?.Invoke();
    }

    public void OnDeactivated()
    {
        ClearPreviewVisuals();
    }

    public bool OnKeyDown(Key key)
    {
        if (key == Key.Escape && IsPicking)
        {
            IsPicking = false;
            return true;
        }
        return false;
    }

    public bool OnKeyUp(Key key) => false;

    public bool OnMouseDown(MouseDown3DEventArgs eventArgs)
    {
        if (eventArgs.OriginalInputEventArgs is not MouseButtonEventArgs { ChangedButton: MouseButton.Left })
            return false;

        var hit = eventArgs.HitTestResult;
        if (hit == null || hit.ModelHit == null)
            return false;

        // 1. Check rotate handle hit
        if (_rotateHandleModel != null && hit.ModelHit == _rotateHandleModel)
        {
            _currentDrag = DragMode.Rotate;
            return true;
        }

        // 2. Click anywhere on target mesh or decal
        if (_targetModel != null && (hit.ModelHit == _targetModel || hit.ModelHit == _decalModel || hit.ModelHit == _gizmoLineModel))
        {
            var hitPt = new Vector3(hit.PointHit.X, hit.PointHit.Y, hit.PointHit.Z);
            var hitNorm = new Vector3(hit.NormalAtHit.X, hit.NormalAtHit.Y, hit.NormalAtHit.Z);

            _currentDrag = DragMode.Move;
            DecalMoved?.Invoke(hitPt, hitNorm);
            return true;
        }

        return false;
    }

    public bool OnMouseMove(HitTestResult? hit)
    {
        if (_currentDrag == DragMode.Move)
        {
            if (Mouse.LeftButton == MouseButtonState.Released)
            {
                _currentDrag = DragMode.None;
                if (CurrentDecal != null)
                {
                    DecalPlaced?.Invoke(CurrentDecal.Anchor, CurrentDecal.AnchorNormal);
                }
                return true;
            }

            if (hit?.ModelHit is MeshGeometryModel3D meshHit && meshHit.GUID == _targetMeshId)
            {
                var pt = new Vector3(hit.PointHit.X, hit.PointHit.Y, hit.PointHit.Z);
                var norm = new Vector3(hit.NormalAtHit.X, hit.NormalAtHit.Y, hit.NormalAtHit.Z);
                DecalMoved?.Invoke(pt, norm);
                return true;
            }
            return true;
        }

        if (_currentDrag == DragMode.Rotate)
        {
            if (Mouse.LeftButton == MouseButtonState.Released)
            {
                _currentDrag = DragMode.None;
                if (CurrentDecal != null)
                {
                    DecalPlaced?.Invoke(CurrentDecal.Anchor, CurrentDecal.AnchorNormal);
                }
                return true;
            }

            if (CurrentFrame != null && hit != null)
            {
                var pt = new Vector3(hit.PointHit.X, hit.PointHit.Y, hit.PointHit.Z);
                var toPt = pt - CurrentFrame.Origin;
                float uComp = Vector3.Dot(toPt, CurrentFrame.U);
                float vComp = Vector3.Dot(toPt, CurrentFrame.V);

                float angleDeg = MathF.Atan2(uComp, vComp) * 180f / MathF.PI;

                bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                float snapDeg = shift ? MathF.Round(angleDeg / 15f) * 15f : MathF.Round(angleDeg);

                DecalRotated?.Invoke(snapDeg);
                return true;
            }
            return true;
        }

        if (IsPicking && hit?.ModelHit is MeshGeometryModel3D && hit.ModelHit == _targetModel)
        {
            var pt = new Vector3(hit.PointHit.X, hit.PointHit.Y, hit.PointHit.Z);
            var norm = new Vector3(hit.NormalAtHit.X, hit.NormalAtHit.Y, hit.NormalAtHit.Z);
            DecalHovered?.Invoke(pt, norm);
            return true;
        }

        return false;
    }

    public bool OnMouseUp(MouseUp3DEventArgs eventArgs)
    {
        if (_currentDrag != DragMode.None)
        {
            _currentDrag = DragMode.None;
            if (CurrentDecal != null)
            {
                DecalPlaced?.Invoke(CurrentDecal.Anchor, CurrentDecal.AnchorNormal);
            }
            return true;
        }
        return false;
    }
}
