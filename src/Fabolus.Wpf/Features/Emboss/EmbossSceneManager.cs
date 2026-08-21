using System.Numerics;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common;
using Fabolus.Core.Features.Emboss;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common.Helpers;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Viewport;
using HelixToolkit.Wpf.SharpDX;

namespace Fabolus.Wpf.Features.Emboss;

public sealed class EmbossSceneManager : ISceneManager
{
    private enum DragMode
    {
        None,
        Move,
        Rotate
    }

    private readonly IGeometryEngine _engine;
    private readonly IMessenger _messenger;

    private Element3D? _grid;
    private readonly Material _targetSkin = Skins.Surface.Gray;
    private readonly Material _previewDecalSkin = Skins.Primitive.Cyan;
    private readonly Material _rotateHandleSkin = Skins.Primitive.Amber;

    private IMesh? TargetMesh { get; set; }
    private TextDecal? CurrentDecal { get; set; }
    private DecalFrame? CurrentFrame { get; set; }

    private Guid _targetMeshId = Guid.Empty;
    private Guid _previewDecalId = Guid.Empty;
    private Guid _gizmoLineId = Guid.Empty;
    private Guid _rotateHandleId = Guid.Empty;

    private MeshGeometryModel3D? _targetModel;
    private MeshGeometryModel3D? _decalModel;
    private LineGeometryModel3D? _gizmoLineModel;
    private MeshGeometryModel3D? _rotateHandleModel;

    private DragMode _currentDrag = DragMode.None;
    private Vector3 _grabOffset = Vector3.Zero;
    private bool _isPicking = false;

    public bool IsPicking
    {
        get => _isPicking;
        set => _isPicking = value;
    }

    public event Action<Element3D>? VisualAddedOrUpdated;
    public event Action<Guid>? VisualRemovedById;
    public event Action? VisualsCleared;

    public event Action<Vector3, Vector3>? DecalPlaced;
    public event Action<Vector3, Vector3>? DecalMoved;
    public event Action<float>? DecalRotated;
    public event Action<Vector3, Vector3>? DecalHovered;

    public EmbossSceneManager(IGeometryEngine engine, IMessenger messenger)
    {
        _engine = engine;
        _messenger = messenger;

        var width = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.PrintBedWidthLabel)).Response;
        var depth = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.PrintBedDepthLabel)).Response;
        var show = (bool)_messenger.Send(new AppPreferenceRequestMessage(UISettings.ShowBedGridLabel)).Response;
        _grid = SceneHelpers.GenerateGrid(width, depth, 10, show);

        _messenger.Register<AppPreferenceUpdateMessage>(this, (r, m) =>
        {
            if (m.Key == UISettings.PrintBedWidthLabel || m.Key == UISettings.PrintBedDepthLabel || m.Key == UISettings.ShowBedGridLabel)
            {
                var w = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.PrintBedWidthLabel)).Response;
                var d = (float)_messenger.Send(new AppPreferenceRequestMessage(UISettings.PrintBedDepthLabel)).Response;
                var s = (bool)_messenger.Send(new AppPreferenceRequestMessage(UISettings.ShowBedGridLabel)).Response;

                if (_grid != null)
                    VisualRemovedById?.Invoke(_grid.GUID);

                _grid = SceneHelpers.GenerateGrid(w, d, 10, s);
                VisualAddedOrUpdated?.Invoke(_grid);
            }
        });
    }

    public Result UpdateMesh(IMesh mesh)
    {
        if (_targetMeshId != Guid.Empty)
            VisualRemovedById?.Invoke(_targetMeshId);

        TargetMesh = mesh;

        var geometryResult = TargetMesh.ToHelixMesh(_engine);
        if (geometryResult.IsFailure)
            return geometryResult.Error;

        _targetModel = new MeshGeometryModel3D
        {
            Geometry = geometryResult.Value,
            Material = _targetSkin,
            CullMode = SharpDX.Direct3D11.CullMode.None,
        };

        SceneVisual.SetIsModelGeometry(_targetModel, true);
        _targetMeshId = _targetModel.GUID;
        VisualAddedOrUpdated?.Invoke(_targetModel);

        return Result.Success();
    }

    public void ReleaseMesh()
    {
        TargetMesh = null;
        _targetModel = null;
    }

    public void UpdatePreview(TextDecal decal, TextMetrics metrics, IGlyphOutlineSource outlineSource)
    {
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

        if (decal.Mirror)
            outlines = outlines.MirrorX();

        float sink = decal.Operation == EmbossOperation.Emboss ? -0.25f : -decal.Depth;
        float overshoot = 0.5f;
        float maxEdge = decal.ProjectOntoSurface ? Math.Max(0.5f, decal.CapHeight / 6.0f) : 0f;

        var prismResult = _engine.Generators.BuildTextPrism(outlines, frame, decal.Depth, sink, overshoot, maxEdge);
        if (prismResult.IsFailure) return;

        var prismMesh = prismResult.Value;

        if (decal.ProjectOntoSurface)
        {
            var projectResult = _engine.Generators.ProjectTextPrism(TargetMesh, frame, prismMesh);
            if (projectResult.IsSuccess)
                prismMesh = projectResult.Value;
        }

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

        var p0 = frame.ToWorld(-halfW, -halfH, zOff);
        var p1 = frame.ToWorld(halfW, -halfH, zOff);
        var p2 = frame.ToWorld(halfW, halfH, zOff);
        var p3 = frame.ToWorld(-halfW, halfH, zOff);

        var linePositions = new Vector3Collection
        {
            new SharpDX.Vector3(p0.X, p0.Y, p0.Z),
            new SharpDX.Vector3(p1.X, p1.Y, p1.Z),
            new SharpDX.Vector3(p1.X, p1.Y, p1.Z),
            new SharpDX.Vector3(p2.X, p2.Y, p2.Z),
            new SharpDX.Vector3(p2.X, p2.Y, p2.Z),
            new SharpDX.Vector3(p3.X, p3.Y, p3.Z),
            new SharpDX.Vector3(p3.X, p3.Y, p3.Z),
            new SharpDX.Vector3(p0.X, p0.Y, p0.Z),
        };

        var lineGeometry = new LineGeometry3D { Positions = linePositions };

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

    public void ClearPreviewVisuals()
    {
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
        if (_grid != null)
            VisualAddedOrUpdated?.Invoke(_grid);
    }

    public void OnDeactivated()
    {
        ClearPreviewVisuals();
    }

    public bool OnKeyDown(Key key)
    {
        if (key == Key.Escape && _isPicking)
        {
            _isPicking = false;
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

        // 2. Check decal preview hit for move
        if (_decalModel != null && hit.ModelHit == _decalModel && CurrentDecal != null)
        {
            _currentDrag = DragMode.Move;
            var hitPt = new Vector3(hit.PointHit.X, hit.PointHit.Y, hit.PointHit.Z);
            _grabOffset = hitPt - CurrentDecal.Anchor;
            return true;
        }

        // 3. Check target mesh hit
        if (_targetModel != null && hit.ModelHit == _targetModel)
        {
            var hitPt = new Vector3(hit.PointHit.X, hit.PointHit.Y, hit.PointHit.Z);
            var hitNorm = new Vector3(hit.NormalAtHit.X, hit.NormalAtHit.Y, hit.NormalAtHit.Z);

            if (_isPicking)
            {
                _isPicking = false;
                DecalPlaced?.Invoke(hitPt, hitNorm);
                return true;
            }

            // Normal move if clicking on decal footprint
            if (CurrentFrame != null && CurrentDecal != null)
            {
                var local = CurrentFrame.ToLocal(hitPt);
                if (MathF.Abs(local.X) < CurrentDecal.CapHeight * 4.0f && MathF.Abs(local.Y) < CurrentDecal.CapHeight * 1.5f)
                {
                    _currentDrag = DragMode.Move;
                    _grabOffset = hitPt - CurrentDecal.Anchor;
                    return true;
                }
            }
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
                return true;
            }

            if (hit?.ModelHit is MeshGeometryModel3D meshHit && meshHit.GUID == _targetMeshId)
            {
                var pt = new Vector3(hit.PointHit.X, hit.PointHit.Y, hit.PointHit.Z);
                var norm = new Vector3(hit.NormalAtHit.X, hit.NormalAtHit.Y, hit.NormalAtHit.Z);
                DecalMoved?.Invoke(pt - _grabOffset, norm);
                return true;
            }
            return true;
        }

        if (_currentDrag == DragMode.Rotate)
        {
            if (Mouse.LeftButton == MouseButtonState.Released)
            {
                _currentDrag = DragMode.None;
                return true;
            }

            if (CurrentFrame != null && hit != null)
            {
                var pt = new Vector3(hit.PointHit.X, hit.PointHit.Y, hit.PointHit.Z);
                var toPt = pt - CurrentFrame.Origin;
                float uComp = Vector3.Dot(toPt, CurrentFrame.U);
                float vComp = Vector3.Dot(toPt, CurrentFrame.V);

                float angleDeg = MathF.Atan2(uComp, vComp) * 180f / MathF.PI;

                // Snap to 15 degrees if Shift is held, otherwise round to whole degree
                bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                float snapDeg = shift ? MathF.Round(angleDeg / 15f) * 15f : MathF.Round(angleDeg);

                DecalRotated?.Invoke(snapDeg);
                return true;
            }
            return true;
        }

        if (_isPicking && hit?.ModelHit is MeshGeometryModel3D && hit.ModelHit == _targetModel)
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
            return true;
        }
        return false;
    }
}
