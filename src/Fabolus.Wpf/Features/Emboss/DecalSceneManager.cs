using System.Numerics;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Common.Helpers;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Features.AppPreferences;
using Fabolus.Wpf.Features.Viewport;
using HelixToolkit.Wpf.SharpDX;

namespace Fabolus.Wpf.Features.Decal;

public sealed class DecalSceneManager : ISceneManager
{
    private const float PreviewSinkOffset = -0.05f;
    private const float PreviewOvershootOffset = 0.05f;
    private const float MinPreviewEdgeLength = 0.4f;
    private const float PreviewCapHeightDivisor = 8.0f;
    private const float MinPatchEdgeLength = 0.5f;
    private const float PatchCapHeightDivisor = 4.0f;
    private const float PatchDepth = 0.2f;
    private const float PatchSink = -0.05f;
    private const float PatchOvershoot = 0.05f;
    private const float DefaultBoxPaddingMm = 1.0f;
    private const float DefaultBoxZOffset = 0.5f;
    private const float DragPreviewZOffset = 0.5f;
    private const float SpherePresetRadius = 3.0f;
    private const int SphereTessellation = 16;
    private const float DuplicateAnchorDistanceThreshold = 1.0f;
    private const float MinNormalLengthSquared = 1e-4f;

    private readonly IGeometryEngine _engine;
    private Element3D? _grid;

    public IMesh? TargetMesh { get; private set; }
    private Guid _targetMeshId = Guid.Empty;

    // Visual elements
    private readonly Dictionary<Guid, MeshGeometryModel3D> _decalVisuals = [];
    private readonly Dictionary<Guid, Guid> _visualToDecalId = [];

    // Preset sphere markers
    private readonly Dictionary<Guid, MeshGeometryModel3D> _presetSphereVisuals = [];
    private readonly Dictionary<Guid, DecalPresetPoint> _visualToPreset = [];

    // Hover preset preview visuals
    private MeshGeometryModel3D? _presetHoverDecalModel;
    private Guid _presetHoverDecalId = Guid.Empty;
    private LineGeometryModel3D? _presetHoverBoxModel;
    private Guid _presetHoverBoxId = Guid.Empty;
    private Guid _hoveredPresetVisualId = Guid.Empty;

    // Selection gizmo (cyan wireframe bounding box)
    private LineGeometryModel3D? _gizmoLineModel;
    private Guid _gizmoLineId = Guid.Empty;

    // Materials
    private readonly Material _targetSkin;
    private readonly Material _mouldSkin;
    private readonly Material _embossSkin;
    private readonly Material _engraveSkin;
    private readonly Material _presetSkin;
    private readonly Material _presetHoverSkin;
    private readonly Material _presetHoverDecalSkin;

    // Interaction state
    public Guid SelectedDecalId { get; set; } = Guid.Empty;

    private bool _isDragging;
    private Guid _dragDecalId = Guid.Empty;

    public event Action<Element3D>? VisualAddedOrUpdated;
    public event Action<Guid>? VisualRemovedById;
    public event Action? VisualsCleared;

    public event Action<Guid>? DecalSelected;
    public event Action<Guid, Vector3, Vector3>? DecalMoved;
    public event Action<Guid>? DecalDragCompleted;
    public event Action<DecalPresetPoint>? PresetPointSelected;
    public event Action<DecalPresetPoint?>? PresetPointHovered;

    public DecalSceneManager(IGeometryEngine engine, IMessenger messenger)
    {
        _engine = engine;

        var width = (float)messenger.Send(new AppPreferenceRequestMessage(UISettings.PrintBedWidthLabel)).Response;
        var depth = (float)messenger.Send(new AppPreferenceRequestMessage(UISettings.PrintBedDepthLabel)).Response;
        var show = (bool)messenger.Send(new AppPreferenceRequestMessage(UISettings.ShowBedGridLabel)).Response;
        _grid = SceneHelpers.GenerateGrid(width, depth, 10, show);

        messenger.Register<AppPreferenceUpdateMessage>(this, (r, m) =>
        {
            if (m.Key == UISettings.PrintBedWidthLabel || m.Key == UISettings.PrintBedDepthLabel || m.Key == UISettings.ShowBedGridLabel)
            {
                var w = (float)messenger.Send(new AppPreferenceRequestMessage(UISettings.PrintBedWidthLabel)).Response;
                var d = (float)messenger.Send(new AppPreferenceRequestMessage(UISettings.PrintBedDepthLabel)).Response;
                var s = (bool)messenger.Send(new AppPreferenceRequestMessage(UISettings.ShowBedGridLabel)).Response;

                if (_grid is not null)
                {
                    VisualRemovedById?.Invoke(_grid.GUID);
                }
                _grid = SceneHelpers.GenerateGrid(w, d, 10, s);
                VisualAddedOrUpdated?.Invoke(_grid);
            }
        });

        _targetSkin = Skins.Surface.Gray;
        _mouldSkin = Skins.Surface.TranslucentGray;
        _embossSkin = Skins.Primitive.Emerald;
        _engraveSkin = Skins.Primitive.Ruby;
        _presetSkin = Skins.Primitive.TranslucentCyan;
        _presetHoverSkin = Skins.Primitive.TranslucentAmber;
        _presetHoverDecalSkin = Skins.Primitive.TranslucentAmber;
    }

    public Result UpdateMesh(IMesh mesh, bool isMould = false)
    {
        if (mesh is null)
            return MeshErrors.NullSource;

        if (_targetMeshId != Guid.Empty)
            VisualRemovedById?.Invoke(_targetMeshId);

        TargetMesh = mesh;
        var helixMeshResult = mesh.ToHelixMesh(_engine);
        if (helixMeshResult.IsFailure)
            return helixMeshResult;

        var model = new MeshGeometryModel3D
        {
            Geometry = helixMeshResult.Value,
            Material = isMould ? _mouldSkin : _targetSkin,
            IsTransparent = isMould,
            CullMode = isMould ? SharpDX.Direct3D11.CullMode.None : SharpDX.Direct3D11.CullMode.Back
        };
        SceneVisual.SetIsModelGeometry(model, true);
        _targetMeshId = model.GUID;
        VisualAddedOrUpdated?.Invoke(model);

        return Result.Success();
    }

    public void ReleaseMesh()
    {
        ClearPreviewVisuals();
        if (_targetMeshId != Guid.Empty)
        {
            VisualRemovedById?.Invoke(_targetMeshId);
            _targetMeshId = Guid.Empty;
        }
        TargetMesh = null;
    }

    public void UpdateDecals(IReadOnlyList<TextDecal> decals, Guid selectedId, IGlyphOutlineSource outlineSource, EmbossTarget? currentTarget = null)
    {
        SelectedDecalId = selectedId;

        if (TargetMesh is null)
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
            var outlineResult = outlineSource.GetOutlines(decal.Text, decal.Font, decal.CapHeight, decal.Tracking);
            if (outlineResult.IsFailure || outlineResult.Value.Count == 0)
            {
                RemoveDecalVisual(decal.Id);
                continue;
            }

            float sink = PreviewSinkOffset;
            float overshoot = PreviewOvershootOffset;
            float maxEdge = Math.Max(MinPreviewEdgeLength, decal.CapHeight / PreviewCapHeightDivisor);
            IMesh? surfaceTarget = TargetMesh;

            var prismResult = _engine.Generators.BuildTextPrism(outlineResult.Value, frame, decal.Depth, sink, overshoot, maxEdge, surfaceTarget);
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
        if (selectedDecal is not null)
        {
            var selFrame = DecalFrame.FromHit(selectedDecal.Anchor, selectedDecal.AnchorNormal, selectedDecal.RotationDeg);
            var metrics = outlineSource.MeasureText(selectedDecal.Text, selectedDecal.Font, selectedDecal.CapHeight, selectedDecal.Tracking);
            float halfW = metrics.WidthMm * 0.5f + DefaultBoxPaddingMm;
            float halfH = metrics.HeightMm * 0.5f + DefaultBoxPaddingMm;
            float zOff = selectedDecal.Depth + DefaultBoxZOffset;

            var lineGeometry = GenerateContouredBoundingBox(selFrame, halfW, halfH, zOff);

            if (_gizmoLineModel is null)
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
        if (TargetMesh is null) return;

        var frame = DecalFrame.FromHit(decal.Anchor, decal.AnchorNormal, decal.RotationDeg);
        float halfW = metrics.WidthMm * 0.5f + DefaultBoxPaddingMm;
        float halfH = metrics.HeightMm * 0.5f + DefaultBoxPaddingMm;
        float zOff = DragPreviewZOffset;

        var rectPolygon = new Polygon2D
        {
            OuterBoundary =
            [
                new Vector2(-halfW, -halfH),
                new Vector2(halfW, -halfH),
                new Vector2(halfW, halfH),
                new Vector2(-halfW, halfH)
            ]
        };

        float maxEdge = Math.Max(MinPatchEdgeLength, decal.CapHeight / PatchCapHeightDivisor);
        IMesh? surfaceTarget = TargetMesh;

        var patchResult = _engine.Generators.BuildTextPrism(new[] { rectPolygon }, frame, PatchDepth, PatchSink, PatchOvershoot, maxEdge, surfaceTarget);
        if (patchResult.IsSuccess)
        {
            var helixPatch = patchResult.Value.ToHelixMesh(_engine);
            if (helixPatch.IsSuccess)
            {
                if (_decalVisuals.TryGetValue(decal.Id, out var existingModel))
                {
                    existingModel.Geometry = helixPatch.Value;
                    existingModel.Visibility = Visibility.Visible;
                    VisualAddedOrUpdated?.Invoke(existingModel);
                }
            }
        }

        var lineGeometry = GenerateContouredBoundingBox(frame, halfW, halfH, zOff);
        if (_gizmoLineModel is null)
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
        ClearPresetVisuals();

        if (!isVisible || presetPoints is null || presetPoints.Count == 0)
            return;

        foreach (var preset in presetPoints)
        {
            var mb = new HelixToolkit.Wpf.SharpDX.MeshBuilder();
            mb.AddSphere(new SharpDX.Vector3(preset.Position.X, preset.Position.Y, preset.Position.Z), SpherePresetRadius, SphereTessellation, SphereTessellation);
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
        if (TargetMesh is null) return;

        // If the active decal is already at this exact preset position, don't double render
        if (Vector3.Distance(decal.Anchor, preset.Position) < DuplicateAnchorDistanceThreshold && Math.Abs(decal.RotationDeg - (int)preset.RotationDeg) < 1)
        {
            ClearPresetHoverPreview();
            return;
        }

        float span = preset.AvailableSpan;
        float capHeight = span > 0f
            ? MouldPresetPointsCalculator.CalculateSuggestedCapHeight(span, decal.Text.Length)
            : decal.CapHeight;

        var text = string.IsNullOrWhiteSpace(decal.Text) ? TextDecal.DefaultText : decal.Text;
        var outlineResult = outlineSource.GetOutlines(text, decal.Font, capHeight, decal.Tracking);
        if (outlineResult.IsFailure || outlineResult.Value.Count == 0)
        {
            ClearPresetHoverPreview();
            return;
        }

        var frame = DecalFrame.FromHit(preset.Position, preset.Normal, preset.RotationDeg);
        float sink = PreviewSinkOffset;
        float overshoot = PreviewOvershootOffset;
        float maxEdge = Math.Max(MinPreviewEdgeLength, capHeight / PreviewCapHeightDivisor);
        IMesh? surfaceTarget = TargetMesh;

        var prismResult = _engine.Generators.BuildTextPrism(outlineResult.Value, frame, decal.Depth, sink, overshoot, maxEdge, surfaceTarget);
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

        if (_presetHoverDecalModel is null)
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
        float halfW = metrics.WidthMm * 0.5f + DefaultBoxPaddingMm;
        float halfH = metrics.HeightMm * 0.5f + DefaultBoxPaddingMm;
        float zOff = decal.Depth + DefaultBoxZOffset;
        var boxGeometry = GenerateContouredBoundingBox(frame, halfW, halfH, zOff);

        if (_presetHoverBoxModel is null)
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
        if (_presetHoverDecalId != Guid.Empty)
        {
            VisualRemovedById?.Invoke(_presetHoverDecalId);
            _presetHoverDecalId = Guid.Empty;
            _presetHoverDecalModel = null;
        }
        if (_presetHoverBoxId != Guid.Empty)
        {
            VisualRemovedById?.Invoke(_presetHoverBoxId);
            _presetHoverBoxId = Guid.Empty;
            _presetHoverBoxModel = null;
        }
    }

    public void ClearPresetVisuals()
    {
        ClearPresetHoverPreview();
        foreach (var guid in _presetSphereVisuals.Keys)
        {
            VisualRemovedById?.Invoke(guid);
        }
        _presetSphereVisuals.Clear();
        _visualToPreset.Clear();
    }

    public void ClearPreviewVisuals()
    {
        ClearPresetVisuals();

        foreach (var guid in _decalVisuals.Values.Select(v => v.GUID))
        {
            VisualRemovedById?.Invoke(guid);
        }
        _decalVisuals.Clear();
        _visualToDecalId.Clear();

        if (_gizmoLineId != Guid.Empty)
        {
            VisualRemovedById?.Invoke(_gizmoLineId);
            _gizmoLineId = Guid.Empty;
            _gizmoLineModel = null;
        }
    }

    public void OnActivated() => VisualsCleared?.Invoke();

    public void OnDeactivated() => ClearPreviewVisuals();

    public bool OnKeyDown(Key key) => false;

    public bool OnKeyUp(Key key) => false;

    public bool OnMouseDown(MouseDown3DEventArgs eventArgs)
    {
        if (eventArgs.OriginalInputEventArgs is not MouseButtonEventArgs { ChangedButton: MouseButton.Left })
            return false;

        var hit = eventArgs.HitTestResult;
        if (hit is null || hit.ModelHit is null)
        {
            if (SelectedDecalId != Guid.Empty)
            {
                SelectedDecalId = Guid.Empty;
                DecalSelected?.Invoke(Guid.Empty);
            }
            return false;
        }

        _isDragging = false;
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
        Guid hitPresetGuid = hit?.ModelHit is MeshGeometryModel3D sphereModel && _presetSphereVisuals.ContainsKey(sphereModel.GUID)
            ? sphereModel.GUID
            : Guid.Empty;

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

        if (Mouse.LeftButton != MouseButtonState.Pressed || _dragDecalId == Guid.Empty)
            return false;

        var targetHit = allHits?.FirstOrDefault(h => h.ModelHit is MeshGeometryModel3D m && m.GUID == _targetMeshId)
            ?? (hit?.ModelHit is MeshGeometryModel3D meshHit && meshHit.GUID == _targetMeshId ? hit : null);

        if (targetHit is null)
            return false;

        _isDragging = true;
        var p = new Vector3((float)targetHit.PointHit.X, (float)targetHit.PointHit.Y, (float)targetHit.PointHit.Z);

        // Length is checked before normalising, not after: normalising a zero vector yields NaN,
        // and every comparison against NaN is false, so a post-normalise guard never fires.
        var rawNormal = new Vector3((float)targetHit.NormalAtHit.X, (float)targetHit.NormalAtHit.Y, (float)targetHit.NormalAtHit.Z);
        var n = rawNormal.LengthSquared() < MinNormalLengthSquared
            ? Vector3.UnitZ
            : Vector3.Normalize(rawNormal);

        DecalMoved?.Invoke(_dragDecalId, p, n);
        return true;
    }

    public bool OnMouseUp(MouseUp3DEventArgs eventArgs)
    {
        if (eventArgs.OriginalInputEventArgs is not MouseButtonEventArgs { ChangedButton: MouseButton.Left })
            return false;

        if (_isDragging && _dragDecalId != Guid.Empty)
        {
            var finishedId = _dragDecalId;
            _isDragging = false;
            _dragDecalId = Guid.Empty;
            DecalDragCompleted?.Invoke(finishedId);
            return true;
        }

        _isDragging = false;
        _dragDecalId = Guid.Empty;
        return false;
    }
}
