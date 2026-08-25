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

    public event Action<Guid>? DecalSelected;
    public event Action<Vector3, Vector3>? DecalPlaced;
    public event Action<Guid, Vector3, Vector3>? DecalMoved;
    public event Action<Guid>? DecalDragCompleted;
    public event Action<Vector3, Vector3>? DecalHovered;
    public event Action? PickingCancelled;
    public event Action<DecalPresetPoint>? PresetPointSelected;
    public event Action<DecalPresetPoint?>? PresetPointHovered;

    private Guid _targetMeshId = Guid.Empty;
    private MeshGeometryModel3D? _targetModel;

    private readonly Dictionary<Guid, MeshGeometryModel3D> _decalVisuals = [];
    private readonly Dictionary<Guid, Guid> _visualToDecalId = [];

    private readonly Dictionary<Guid, MeshGeometryModel3D> _presetSphereVisuals = [];
    private readonly Dictionary<Guid, DecalPresetPoint> _visualToPreset = [];
    private Guid _hoveredPresetVisualId = Guid.Empty;

    private Guid _presetHoverDecalId = Guid.Empty;
    private MeshGeometryModel3D? _presetHoverDecalModel;
    private Guid _presetHoverBoxId = Guid.Empty;
    private LineGeometryModel3D? _presetHoverBoxModel;

    private Guid _gizmoLineId = Guid.Empty;
    private LineGeometryModel3D? _gizmoLineModel;

    private readonly HelixToolkit.Wpf.SharpDX.Material _targetSkin;
    private readonly HelixToolkit.Wpf.SharpDX.Material _translucentMouldSkin;
    private readonly HelixToolkit.Wpf.SharpDX.Material _embossSkin;
    private readonly HelixToolkit.Wpf.SharpDX.Material _engraveSkin;
    private readonly HelixToolkit.Wpf.SharpDX.Material _presetSkin;
    private readonly HelixToolkit.Wpf.SharpDX.Material _presetHoverSkin;
    private readonly HelixToolkit.Wpf.SharpDX.Material _presetHoverDecalSkin;

    private Guid _translucentMouldId = Guid.Empty;
    private MeshGeometryModel3D? _translucentMouldModel;

    public IMesh? TargetMesh { get; private set; }
    public Guid SelectedDecalId { get; set; } = Guid.Empty;
    public bool IsPicking { get; set; }

    private bool _isDragging;
    private Guid _dragDecalId = Guid.Empty;
    private Guid _pendingClickedDecalId = Guid.Empty;
    private System.Windows.Point _mouseDownPoint;

    public EmbossSceneManager(IGeometryEngine engine, IMessenger messenger)
    {
        _engine = engine;
        _messenger = messenger;

        _targetSkin = Skins.Surface.Gray;
        _translucentMouldSkin = Skins.Surface.TranslucentGray;
        _embossSkin = Skins.Primitive.Emerald;
        _engraveSkin = Skins.Primitive.Ruby;
        _presetSkin = Skins.Primitive.TranslucentCyan;
        _presetHoverSkin = Skins.Primitive.TranslucentAmber;
        _presetHoverDecalSkin = Skins.Primitive.TranslucentAmber;
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

    public Result UpdateAppliedMouldOverlay(IMesh? mouldMesh)
    {
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => UpdateAppliedMouldOverlay(mouldMesh));
        }

        if (mouldMesh == null)
        {
            if (_translucentMouldModel != null)
            {
                VisualRemovedById?.Invoke(_translucentMouldModel.GUID);
                _translucentMouldModel = null;
                _translucentMouldId = Guid.Empty;
            }
            return Result.Success();
        }

        var helixResult = mouldMesh.ToHelixMesh(_engine);
        if (helixResult.IsFailure)
            return helixResult;

        if (_translucentMouldModel == null || (Application.Current == null && !_translucentMouldModel.CheckAccess()))
        {
            _translucentMouldModel = new MeshGeometryModel3D
            {
                Geometry = helixResult.Value,
                Material = _translucentMouldSkin,
                IsTransparent = true,
                CullMode = SharpDX.Direct3D11.CullMode.None,
                IsHitTestVisible = false
            };
            SceneVisual.SetIsModelGeometry(_translucentMouldModel, true);
            _translucentMouldId = _translucentMouldModel.GUID;
            VisualAddedOrUpdated?.Invoke(_translucentMouldModel);
        }
        else
        {
            _translucentMouldModel.Geometry = helixResult.Value;
            _translucentMouldModel.Visibility = Visibility.Visible;
            VisualAddedOrUpdated?.Invoke(_translucentMouldModel);
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
        if (_translucentMouldModel != null)
        {
            VisualRemovedById?.Invoke(_translucentMouldModel.GUID);
            _translucentMouldModel = null;
            _translucentMouldId = Guid.Empty;
        }
        TargetMesh = null;
        _targetModel = null;
    }

    public void UpdateDecals(IReadOnlyList<TextDecal> decals, Guid selectedId, IGlyphOutlineSource outlineSource, EmbossTarget? currentTarget = null)
    {
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(() => UpdateDecals(decals, selectedId, outlineSource, currentTarget));
            return;
        }

        SelectedDecalId = selectedId;

        if (TargetMesh == null)
        {
            ClearPreviewVisuals();
            return;
        }

        var activeIds = new HashSet<Guid>();

        TextDecal? selectedDecal = null;

        foreach (var decal in decals)
        {
            if (currentTarget.HasValue && decal.Target != currentTarget.Value)
            {
                RemoveDecalVisual(decal.Id);
                continue;
            }

            activeIds.Add(decal.Id);
            if (decal.Id == selectedId)
            {
                selectedDecal = decal;
            }

            if (string.IsNullOrWhiteSpace(decal.Text))
            {
                RemoveDecalVisual(decal.Id);
                continue;
            }

            var frame = DecalFrame.FromHit(decal.Anchor, decal.AnchorNormal, decal.RotationDeg);
            var outlines = outlineSource.GetOutlines(decal.Text, decal.Font, decal.CapHeight, decal.Tracking);
            if (outlines.Count == 0)
            {
                RemoveDecalVisual(decal.Id);
                continue;
            }

            float sink = -0.05f;
            float overshoot = 0.05f;
            float maxEdge = Math.Max(0.4f, decal.CapHeight / 8.0f);
            IMesh? surfaceTarget = TargetMesh;

            var prismResult = _engine.Generators.BuildTextPrism(outlines, frame, decal.Depth, sink, overshoot, maxEdge, surfaceTarget);
            if (prismResult.IsFailure) continue;

            var helixPrismResult = prismResult.Value.ToHelixMesh(_engine);
            if (helixPrismResult.IsFailure) continue;

            var skin = decal.Operation == EmbossOperation.Emboss ? _embossSkin : _engraveSkin;

            if (!_decalVisuals.TryGetValue(decal.Id, out var model))
            {
                model = new MeshGeometryModel3D
                {
                    Geometry = helixPrismResult.Value,
                    Material = skin,
                    IsTransparent = true,
                    DepthBias = -50,
                    SlopeScaledDepthBias = -1.0f,
                    CullMode = SharpDX.Direct3D11.CullMode.None,
                };
                SceneVisual.SetIsModelGeometry(model, false);
                _decalVisuals[decal.Id] = model;
                _visualToDecalId[model.GUID] = decal.Id;
                VisualAddedOrUpdated?.Invoke(model);
            }
            else
            {
                model.Geometry = helixPrismResult.Value;
                model.Material = skin;
                model.Visibility = Visibility.Visible;
                VisualAddedOrUpdated?.Invoke(model);
            }
        }

        // Remove visuals that are no longer present
        var toRemove = _decalVisuals.Keys.Where(id => !activeIds.Contains(id)).ToList();
        foreach (var id in toRemove)
        {
            RemoveDecalVisual(id);
        }

        // Update cyan bounding box around the selected decal
        if (selectedDecal != null)
        {
            var selFrame = DecalFrame.FromHit(selectedDecal.Anchor, selectedDecal.AnchorNormal, selectedDecal.RotationDeg);
            var metrics = outlineSource.MeasureText(selectedDecal.Text, selectedDecal.Font, selectedDecal.CapHeight, selectedDecal.Tracking);
            float halfW = metrics.WidthMm * 0.5f + 1.0f;
            float halfH = metrics.HeightMm * 0.5f + 1.0f;
            float zOff = selectedDecal.Depth + 0.5f;

            var lineGeometry = GenerateContouredBoundingBox(selFrame, halfW, halfH, zOff);

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
        }
        else
        {
            if (_gizmoLineId != Guid.Empty)
            {
                VisualRemovedById?.Invoke(_gizmoLineId);
                _gizmoLineId = Guid.Empty;
                _gizmoLineModel = null;
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

        if (TargetMesh == null) return;

        var frame = DecalFrame.FromHit(decal.Anchor, decal.AnchorNormal, decal.RotationDeg);
        float halfW = metrics.WidthMm * 0.5f + 1.0f;
        float halfH = metrics.HeightMm * 0.5f + 1.0f;
        float zOff = 0.5f;

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

        float maxEdge = Math.Max(0.5f, decal.CapHeight / 4.0f);
        IMesh? surfaceTarget = TargetMesh;

        var patchResult = _engine.Generators.BuildTextPrism(new[] { rectPolygon }, frame, 0.2f, -0.05f, 0.05f, maxEdge, surfaceTarget);
        if (patchResult.IsSuccess)
        {
            var helixPatch = patchResult.Value.ToHelixMesh(_engine);
            if (helixPatch.IsSuccess)
            {
                if (_decalVisuals.TryGetValue(decal.Id, out var model))
                {
                    model.Geometry = helixPatch.Value;
                    model.Visibility = Visibility.Visible;
                    VisualAddedOrUpdated?.Invoke(model);
                }
            }
        }

        var lineGeometry = GenerateContouredBoundingBox(frame, halfW, halfH, zOff);
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

    private void RemoveDecalVisual(Guid decalId)
    {
        if (_decalVisuals.TryGetValue(decalId, out var model))
        {
            VisualRemovedById?.Invoke(model.GUID);
            _visualToDecalId.Remove(model.GUID);
            _decalVisuals.Remove(decalId);
        }
    }

    private static LineGeometry3D GenerateContouredBoundingBox(DecalFrame frame, float halfW, float halfH, float zOff)
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

    public void UpdatePresetPoints(IReadOnlyList<DecalPresetPoint> presetPoints, bool isVisible)
    {
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(() => UpdatePresetPoints(presetPoints, isVisible));
            return;
        }

        ClearPresetVisuals();

        if (!isVisible || presetPoints == null || presetPoints.Count == 0)
            return;

        foreach (var preset in presetPoints)
        {
            var mb = new HelixToolkit.Wpf.SharpDX.MeshBuilder();
            mb.AddSphere(new SharpDX.Vector3(preset.Position.X, preset.Position.Y, preset.Position.Z), 3.0f, 16, 16);
            var sphereGeom = mb.ToMeshGeometry3D();
            var sphereModel = new MeshGeometryModel3D
            {
                Geometry = sphereGeom,
                Material = _presetSkin,
                CullMode = SharpDX.Direct3D11.CullMode.Back
            };
            _presetSphereVisuals[sphereModel.GUID] = sphereModel;
            _visualToPreset[sphereModel.GUID] = preset;
            VisualAddedOrUpdated?.Invoke(sphereModel);
        }
    }

    public void UpdatePresetHoverPreview(DecalPresetPoint preset, TextDecal decal, IGlyphOutlineSource outlineSource)
    {
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(() => UpdatePresetHoverPreview(preset, decal, outlineSource));
            return;
        }

        if (TargetMesh == null) return;

        // If the active decal is already at this exact preset position, don't double render
        if (Vector3.Distance(decal.Anchor, preset.Position) < 1.0f && Math.Abs(decal.RotationDeg - (int)preset.RotationDeg) < 1)
        {
            ClearPresetHoverPreview();
            return;
        }

        float span = preset.AvailableSpan;
        float capHeight = span > 0f
            ? MouldPresetPointsCalculator.CalculateSuggestedCapHeight(span, decal.Text.Length)
            : decal.CapHeight;

        var text = string.IsNullOrWhiteSpace(decal.Text) ? "FABOLUS" : decal.Text;
        var outlines = outlineSource.GetOutlines(text, decal.Font, capHeight, decal.Tracking);
        if (outlines.Count == 0)
        {
            ClearPresetHoverPreview();
            return;
        }

        var frame = DecalFrame.FromHit(preset.Position, preset.Normal, preset.RotationDeg);
        float sink = -0.05f;
        float overshoot = 0.05f;
        float maxEdge = Math.Max(0.4f, capHeight / 8.0f);
        IMesh? surfaceTarget = TargetMesh;

        var prismResult = _engine.Generators.BuildTextPrism(outlines, frame, decal.Depth, sink, overshoot, maxEdge, surfaceTarget);
        if (prismResult.IsFailure)
        {
            ClearPresetHoverPreview();
            return;
        }

        var helixPrismResult = prismResult.Value.ToHelixMesh(_engine);
        if (helixPrismResult.IsFailure)
        {
            ClearPresetHoverPreview();
            return;
        }

        if (_presetHoverDecalModel == null)
        {
            _presetHoverDecalModel = new MeshGeometryModel3D
            {
                Geometry = helixPrismResult.Value,
                Material = _presetHoverDecalSkin,
                IsTransparent = true,
                DepthBias = -70,
                SlopeScaledDepthBias = -1.0f,
                CullMode = SharpDX.Direct3D11.CullMode.None,
                IsHitTestVisible = false
            };
            _presetHoverDecalId = _presetHoverDecalModel.GUID;
            VisualAddedOrUpdated?.Invoke(_presetHoverDecalModel);
        }
        else
        {
            _presetHoverDecalModel.Geometry = helixPrismResult.Value;
            _presetHoverDecalModel.Material = _presetHoverDecalSkin;
            _presetHoverDecalModel.Visibility = Visibility.Visible;
            VisualAddedOrUpdated?.Invoke(_presetHoverDecalModel);
        }

        // Also add amber bounding box around the preset preview
        var metrics = outlineSource.MeasureText(text, decal.Font, capHeight, decal.Tracking);
        float halfW = metrics.WidthMm * 0.5f + 1.0f;
        float halfH = metrics.HeightMm * 0.5f + 1.0f;
        float zOff = decal.Depth + 0.5f;
        var boxGeometry = GenerateContouredBoundingBox(frame, halfW, halfH, zOff);

        if (_presetHoverBoxModel == null)
        {
            _presetHoverBoxModel = new LineGeometryModel3D
            {
                Geometry = boxGeometry,
                Color = System.Windows.Media.Color.FromArgb(220, 255, 191, 0), // Amber
                Thickness = 1.5,
                IsHitTestVisible = false
            };
            _presetHoverBoxId = _presetHoverBoxModel.GUID;
            VisualAddedOrUpdated?.Invoke(_presetHoverBoxModel);
        }
        else
        {
            _presetHoverBoxModel.Geometry = boxGeometry;
            _presetHoverBoxModel.Visibility = Visibility.Visible;
            VisualAddedOrUpdated?.Invoke(_presetHoverBoxModel);
        }
    }

    public void ClearPresetHoverPreview()
    {
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(ClearPresetHoverPreview);
            return;
        }

        if (_presetHoverDecalId != Guid.Empty && _presetHoverDecalModel != null)
        {
            VisualRemovedById?.Invoke(_presetHoverDecalId);
            _presetHoverDecalId = Guid.Empty;
            _presetHoverDecalModel = null;
        }

        if (_presetHoverBoxId != Guid.Empty && _presetHoverBoxModel != null)
        {
            VisualRemovedById?.Invoke(_presetHoverBoxId);
            _presetHoverBoxId = Guid.Empty;
            _presetHoverBoxModel = null;
        }
    }

    public void ClearPresetVisuals()
    {
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(ClearPresetVisuals);
            return;
        }

        ClearPresetHoverPreview();

        foreach (var (guid, model) in _presetSphereVisuals.ToList())
        {
            VisualRemovedById?.Invoke(guid);
        }
        _presetSphereVisuals.Clear();
        _visualToPreset.Clear();
        _hoveredPresetVisualId = Guid.Empty;
    }

    public void ClearPreviewVisuals()
    {
        if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(ClearPreviewVisuals);
            return;
        }

        ClearPresetHoverPreview();

        foreach (var (decalId, model) in _decalVisuals.ToList())
        {
            VisualRemovedById?.Invoke(model.GUID);
        }
        _decalVisuals.Clear();
        _visualToDecalId.Clear();

        ClearPresetVisuals();

        if (_gizmoLineId != Guid.Empty)
        {
            VisualRemovedById?.Invoke(_gizmoLineId);
            _gizmoLineId = Guid.Empty;
            _gizmoLineModel = null;
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
            PickingCancelled?.Invoke();
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
        {
            if (SelectedDecalId != Guid.Empty)
            {
                SelectedDecalId = Guid.Empty;
                DecalSelected?.Invoke(Guid.Empty);
            }
            return false;
        }

        _mouseDownPoint = eventArgs.Position;
        _isDragging = false;
        _pendingClickedDecalId = Guid.Empty;
        _dragDecalId = Guid.Empty;

        // 1. Check if a preset sphere was clicked
        if (hit.ModelHit is MeshGeometryModel3D sphereHit && _visualToPreset.TryGetValue(sphereHit.GUID, out var preset))
        {
            ClearPresetHoverPreview();
            PresetPointSelected?.Invoke(preset);
            return true;
        }

        // 2. Check if an existing decal was clicked
        if (hit.ModelHit is MeshGeometryModel3D meshModel && _visualToDecalId.TryGetValue(meshModel.GUID, out var decalId))
        {
            _pendingClickedDecalId = decalId;
            _dragDecalId = decalId;
            if (SelectedDecalId != decalId)
            {
                SelectedDecalId = decalId;
                DecalSelected?.Invoke(decalId);
            }
            return true;
        }

        // 3. Click on target mesh (or background outside decals/presets) -> deselect
        if (SelectedDecalId != Guid.Empty)
        {
            SelectedDecalId = Guid.Empty;
            DecalSelected?.Invoke(Guid.Empty);
        }

        return false;
    }

    public bool OnMouseMove(HitTestResult? hit) => OnMouseMove(hit, null);

    public bool OnMouseMove(HitTestResult? hit, IList<HitTestResult>? allHits)
    {
        // Handle preset sphere hover highlight
        Guid hitPresetGuid = Guid.Empty;
        if (hit?.ModelHit is MeshGeometryModel3D sphereModel && _presetSphereVisuals.ContainsKey(sphereModel.GUID))
        {
            hitPresetGuid = sphereModel.GUID;
        }

        if (hitPresetGuid != _hoveredPresetVisualId)
        {
            if (_hoveredPresetVisualId != Guid.Empty && _presetSphereVisuals.TryGetValue(_hoveredPresetVisualId, out var prevSphere))
            {
                prevSphere.Material = _presetSkin;
            }
            if (hitPresetGuid != Guid.Empty && _presetSphereVisuals.TryGetValue(hitPresetGuid, out var currSphere))
            {
                currSphere.Material = _presetHoverSkin;
            }
            _hoveredPresetVisualId = hitPresetGuid;

            if (hitPresetGuid != Guid.Empty && _visualToPreset.TryGetValue(hitPresetGuid, out var hoveredPreset))
            {
                PresetPointHovered?.Invoke(hoveredPreset);
            }
            else
            {
                PresetPointHovered?.Invoke(null);
            }
        }

        if (Mouse.LeftButton == MouseButtonState.Pressed && _dragDecalId != Guid.Empty)
        {
            var targetHit = allHits?.FirstOrDefault(h => h.ModelHit is MeshGeometryModel3D m && m.GUID == _targetMeshId)
                ?? (hit?.ModelHit is MeshGeometryModel3D meshHit && meshHit.GUID == _targetMeshId ? hit : null);

            if (targetHit != null)
            {
                if (!_isDragging)
                {
                    _isDragging = true;
                    if (_pendingClickedDecalId != Guid.Empty && SelectedDecalId != _pendingClickedDecalId)
                    {
                        SelectedDecalId = _pendingClickedDecalId;
                        DecalSelected?.Invoke(_pendingClickedDecalId);
                    }
                }

                var pt = new Vector3(targetHit.PointHit.X, targetHit.PointHit.Y, targetHit.PointHit.Z);
                var norm = new Vector3(targetHit.NormalAtHit.X, targetHit.NormalAtHit.Y, targetHit.NormalAtHit.Z);
                DecalMoved?.Invoke(_dragDecalId, pt, norm);
                return true;
            }
            return true;
        }

        if (_isDragging && Mouse.LeftButton == MouseButtonState.Released)
        {
            var finishedDecalId = _dragDecalId;
            _isDragging = false;
            _dragDecalId = Guid.Empty;
            _pendingClickedDecalId = Guid.Empty;
            if (finishedDecalId != Guid.Empty)
            {
                DecalDragCompleted?.Invoke(finishedDecalId);
            }
            return true;
        }

        if (IsPicking)
        {
            var targetHit = allHits?.FirstOrDefault(h => h.ModelHit is MeshGeometryModel3D m && m.GUID == _targetMeshId)
                ?? (hit?.ModelHit is MeshGeometryModel3D meshHit && meshHit.GUID == _targetMeshId ? hit : null);

            if (targetHit != null)
            {
                var pt = new Vector3(targetHit.PointHit.X, targetHit.PointHit.Y, targetHit.PointHit.Z);
                var norm = new Vector3(targetHit.NormalAtHit.X, targetHit.NormalAtHit.Y, targetHit.NormalAtHit.Z);
                DecalHovered?.Invoke(pt, norm);
                return true;
            }
        }

        return false;
    }

    public bool OnMouseUp(MouseUp3DEventArgs eventArgs)
    {
        if (eventArgs.OriginalInputEventArgs is not MouseButtonEventArgs { ChangedButton: MouseButton.Left })
            return false;

        if (_isDragging && _dragDecalId != Guid.Empty)
        {
            var finishedDecalId = _dragDecalId;
            _isDragging = false;
            _dragDecalId = Guid.Empty;
            _pendingClickedDecalId = Guid.Empty;
            DecalDragCompleted?.Invoke(finishedDecalId);
            return true;
        }

        if (!_isDragging && _pendingClickedDecalId != Guid.Empty)
        {
            SelectedDecalId = _pendingClickedDecalId;
            DecalSelected?.Invoke(_pendingClickedDecalId);
            _pendingClickedDecalId = Guid.Empty;
            _dragDecalId = Guid.Empty;
            return true;
        }

        _isDragging = false;
        _dragDecalId = Guid.Empty;
        _pendingClickedDecalId = Guid.Empty;
        return false;
    }
}
