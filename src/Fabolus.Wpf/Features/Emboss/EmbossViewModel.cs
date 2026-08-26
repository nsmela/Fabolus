using System.Collections.ObjectModel;
using System.IO;
using System.Numerics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.Viewport;

namespace Fabolus.Wpf.Features.Decal;

public partial class DecalViewModel : ObservableObject, IViewState, IDisposable
{
    private readonly IMessenger _messenger;
    private readonly IAlertDialog _alert;
    private readonly IGeometryEngine _engine;
    private readonly IGlyphOutlineSource _outlineSource;
    private readonly GenerateDecals _generator;
    private readonly ClearDecals _clearDecals;
    private readonly DecalSceneManager _sceneManager;

    private Workspace Workspace { get; set; } = Workspace.CreateEmpty();
    private IMesh? _activeMesh;
    private IMesh? _baseMesh;
    private IMesh? _mouldMesh;
    private IMesh? _targetMesh;

    private List<TextDecal> _decals = [];
    public IReadOnlyList<TextDecal> Decals => _decals;
    public int DecalCount => _decals.Count;

    public ObservableCollection<TextDecalItemViewModel> DecalList { get; } = [];

    private TextMetrics _metrics = TextMetrics.Empty;
    private Vector3 _meshCenter = Vector3.Zero;
    private Vector2 _uv = Vector2.Zero;

    private bool _isActivating;
    private bool _isSyncingFromModel;
    private readonly DispatcherTimer _previewTimer;
    private bool _previewPending;

    /// <summary>
    /// How close a decal anchor has to sit to a preset for that preset to count as taken.
    /// Shared by "find a free anchor" (AddDecal) and "did they click an occupied one" (ApplyPreset)
    /// so the two can never disagree about the same anchor.
    /// </summary>
    private const float AnchorOccupiedRadiusMm = 3.0f;

    [ObservableProperty] private Guid _selectedDecalId = Guid.Empty;
    [ObservableProperty] private bool _isDecalsExpanded = true;
    [ObservableProperty] private string _labelText = "FABOLUS";
    [ObservableProperty] private EmbossOperation _operation = EmbossOperation.Engrave;
    [ObservableProperty] private EmbossTarget _target = EmbossTarget.Base;
    [ObservableProperty] private DecalFont _font = DecalFont.Sans;
    [ObservableProperty] private float _capHeight = 6.0f;
    [ObservableProperty] private float _depth = 0.8f;
    [ObservableProperty] private float _tracking = 0.4f;
    [ObservableProperty] private int _rotation = 0;
    [ObservableProperty] private bool _isApplied = false;
    [ObservableProperty] private bool _hasMould = false;
    [ObservableProperty] private Vector3 _anchor = Vector3.Zero;
    [ObservableProperty] private Vector3 _anchorNormal = Vector3.UnitZ;
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private string _warningText = string.Empty;

    // Read-only dynamic properties
    public string DepthLabel => Operation == EmbossOperation.Engrave ? "Depth" : "Height";
    public string ApplyLabel => "Apply decals";
    public string Footprint => $"{_metrics.WidthMm:0.0} × {CapHeight:0.0} mm";
    public string PositionU => $"{_uv.X:0.0} mm";
    public string PositionV => $"{_uv.Y:0.0} mm";
    public string PositionUv => $"{_uv.X:0.0} mm, {_uv.Y:0.0} mm";
    public string StatusWord => IsApplied ? "Applied" : "Preview";
    public string StatusColor => IsApplied ? "#2FA36B" : "#E0A024";
    public string Hint =>
        "Click a decal to select/drag · snap to preset locations or adjust parameters in the panel";
    public string TextStatusLine =>
        $"{Operation} · {LabelText.Length} glyphs · {CapHeight:0.0} mm cap · {Depth:0.0} mm {DepthLabel.ToLower()} · {Rotation}°";

    public ISceneManager SceneManager => _sceneManager;

    private IReadOnlyList<DecalPresetPoint> _basePresetPoints = [];
    public IReadOnlyList<DecalPresetPoint> BasePresetPoints => _basePresetPoints;

    private IReadOnlyList<DecalPresetPoint> _mouldPresetPoints = [];
    public IReadOnlyList<DecalPresetPoint> MouldPresetPoints => _mouldPresetPoints;

    // Meshes the cached preset lists were calculated from. Meshes are immutable - every edit
    // produces a new instance - so reference identity is a sound cache key.
    private IMesh? _basePresetSource;
    private IMesh? _mouldPresetSource;

    public IReadOnlyList<DecalPresetPoint> ActivePresetPoints => Target == EmbossTarget.Mould ? _mouldPresetPoints : _basePresetPoints;

    public DecalViewModel(IMessenger messenger, IAlertDialog alert, IGeometryEngine engine, IGlyphOutlineSource outlineSource)
    {
        _messenger = messenger;
        _alert = alert;
        _engine = engine;
        _outlineSource = outlineSource;
        // Fallback only. App startup registers the DI singleton as the provider; overwriting it
        // here would swap in a second instance every time this view is opened, splitting any
        // glyph caching across the two.
        GlyphOutlineSourceProvider.Default ??= outlineSource;
        _generator = new GenerateDecals(_outlineSource);
        _clearDecals = new ClearDecals(_engine);

        _sceneManager = new DecalSceneManager(_engine, _messenger);
        _sceneManager.DecalSelected += OnDecalSelected;
        _sceneManager.DecalMoved += OnDecalMoved;
        _sceneManager.DecalDragCompleted += OnDecalDragCompleted;
        _sceneManager.PresetPointSelected += OnPresetPointSelected;
        _sceneManager.PresetPointHovered += OnPresetPointHovered;

        // Throttling timer (30ms) to coalesce rapid property updates before re-generating 3D preview geometry.
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _previewTimer.Tick += (s, e) =>
        {
            if (_previewPending)
            {
                _previewPending = false;
                ExecutePreviewUpdate();
            }
        };
        _previewTimer.Start();
    }

    private void OnPresetPointHovered(DecalPresetPoint? preset)
    {
        if (preset is null)
        {
            _sceneManager.ClearPresetHoverPreview();
            return;
        }

        var activeDecal = _decals.FirstOrDefault(d => d.Id == SelectedDecalId) ?? new TextDecal
        {
            Id = Guid.NewGuid(),
            Text = LabelText,
            Operation = Operation,
            Target = preset.Target,
            Font = Font,
            CapHeight = CapHeight,
            Depth = Depth,
            Tracking = Tracking,
            RotationDeg = (int)preset.RotationDeg,
            Anchor = preset.Position,
            AnchorNormal = preset.Normal
        };

        _sceneManager.UpdatePresetHoverPreview(preset, activeDecal, _outlineSource);
    }

    private void OnPresetPointSelected(DecalPresetPoint preset)
    {
        ApplyPreset(preset);
    }

    [RelayCommand]
    public void ApplyPreset(DecalPresetPoint? preset)
    {
        if (preset == null) return;

        _sceneManager.ClearPresetHoverPreview();

        Target = preset.Target;

        // If a decal already sits on this preset, select it!
        var existingDecal = _decals.FirstOrDefault(d => d.Target == preset.Target && Vector3.Distance(d.Anchor, preset.Position) < AnchorOccupiedRadiusMm);
        if (existingDecal != null)
        {
            SelectedDecalId = existingDecal.Id;
            return;
        }

        if (SelectedDecalId != Guid.Empty)
        {
            Rotation = (int)preset.RotationDeg;

            float span = ResolveSpan(preset, preset.Target);
            if (span > 0f)
            {
                CapHeight = MouldPresetPointsCalculator.CalculateSuggestedCapHeight(span, LabelText.Length);
            }

            UpdateTargetMesh();
            Anchor = preset.Position;
            AnchorNormal = preset.Normal;
            SyncActiveDecal();
            UpdateUVReadout();
            Invalidate();
        }
        else
        {
            // If no decal is selected (or is picking) and no decal at this preset, add a new decal here
            string text = TextDecal.DefaultText;
            float capHeight = SuggestedCapHeight(preset, preset.Target, text.Length, CapHeight);

            var newDecal = new TextDecal
            {
                Id = Guid.NewGuid(),
                Text = text,
                Operation = Operation,
                Target = preset.Target,
                Font = Font,
                CapHeight = capHeight,
                Depth = Depth,
                Tracking = Tracking,
                RotationDeg = (int)preset.RotationDeg,
                Anchor = preset.Position,
                AnchorNormal = preset.Normal
            };

            _decals.Add(newDecal);
            SelectedDecalId = newDecal.Id;
            SyncDecalList();
            OnPropertyChanged(nameof(DecalCount));
            Invalidate();
        }
    }

    [RelayCommand]
    public void ApplyPresetByName(string name)
    {
        var preset = ActivePresetPoints.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? _mouldPresetPoints.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? _basePresetPoints.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (preset is not null)
        {
            ApplyPreset(preset);
        }
    }

    /// <summary>
    /// The run of surface a decal has to fit into at this anchor: the preset's own span when it
    /// has one, otherwise the target mesh's extent along the text direction - X for horizontal
    /// anchors, Z for the 90-degree ones. Returns 0 when no mesh statistics are available.
    /// </summary>
    private float ResolveSpan(DecalPresetPoint? preset, EmbossTarget target)
    {
        float span = preset?.AvailableSpan ?? 0f;
        if (span > 0f) return span;

        var meshForStats = target == EmbossTarget.Mould ? _mouldMesh : _baseMesh;
        if (meshForStats is null) return 0f;

        var stats = _engine.Evaluators.GetStatistics(meshForStats);
        if (stats.IsFailure) return 0f;

        return (preset?.RotationDeg ?? 0f) == 0f
            ? (float)(stats.Value.MaxX - stats.Value.MinX)
            : (float)(stats.Value.MaxZ - stats.Value.MinZ);
    }

    /// <summary>
    /// Cap height that fits <paramref name="charCount"/> characters into the anchor's span,
    /// falling back to <paramref name="fallback"/> when the span cannot be determined.
    /// </summary>
    private float SuggestedCapHeight(DecalPresetPoint? preset, EmbossTarget target, int charCount, float fallback)
    {
        float span = ResolveSpan(preset, target);
        return span > 0f
            ? MouldPresetPointsCalculator.CalculateSuggestedCapHeight(span, charCount)
            : fallback;
    }

    private void UpdatePresets()
    {
        // Each Calculate() raycasts the whole mesh dozens of times (3 for the base, 76 for the
        // mould), so recompute only when the source mesh actually changes. This runs on every
        // Base/Mould toggle, which previously paid for both sets every time.
        if (!ReferenceEquals(_basePresetSource, _baseMesh))
        {
            _basePresetPoints = _baseMesh is not null
                ? BasePresetPointsCalculator.Calculate(_engine, _baseMesh)
                : [];
            _basePresetSource = _baseMesh;
        }

        var mouldPresetSource = HasMould ? _mouldMesh : null;
        if (!ReferenceEquals(_mouldPresetSource, mouldPresetSource))
        {
            _mouldPresetPoints = mouldPresetSource is not null
                ? MouldPresetPointsCalculator.Calculate(_engine, mouldPresetSource)
                : [];
            _mouldPresetSource = mouldPresetSource;
        }

        OnPropertyChanged(nameof(BasePresetPoints));
        OnPropertyChanged(nameof(MouldPresetPoints));
        OnPropertyChanged(nameof(ActivePresetPoints));

        var activePoints = ActivePresetPoints;
        _sceneManager.UpdatePresetPoints(activePoints, isVisible: !IsApplied);
    }

    private void UpdateTargetMesh()
    {
        bool isMould = IsApplied
            ? (HasMould && _mouldMesh is not null)
            : (Target == EmbossTarget.Mould && _mouldMesh is not null);

        _targetMesh = isMould ? _mouldMesh : _baseMesh;

        if (_targetMesh is not null)
        {
            _sceneManager.UpdateMesh(_targetMesh, isMould);
            var stats = _engine.Evaluators.GetStatistics(_targetMesh);
            if (stats.IsSuccess)
            {
                var s = stats.Value;
                _meshCenter = new Vector3(
                    (float)(s.MinX + s.MaxX) * 0.5f,
                    (float)(s.MinY + s.MaxY) * 0.5f,
                    (float)s.MaxZ);
            }
        }
    }

    partial void OnHasMouldChanged(bool value)
    {
        foreach (var item in DecalList)
        {
            item.HasMould = value;
        }
    }

    partial void OnIsAppliedChanged(bool value)
    {
        if (value)
        {
            IsDecalsExpanded = false;
        }
    }

    public bool HasSelectedDecal => SelectedDecalId != Guid.Empty;

    partial void OnSelectedDecalIdChanged(Guid value)
    {
        foreach (var item in DecalList)
        {
            item.IsSelected = item.Id == value;
        }

        OnPropertyChanged(nameof(HasSelectedDecal));

        if (value == Guid.Empty)
        {
            _sceneManager.SelectedDecalId = Guid.Empty;
            Invalidate();
            return;
        }

        var decal = _decals.FirstOrDefault(d => d.Id == value);
        if (decal != null)
        {
            _isSyncingFromModel = true;
            try
            {
                LabelText = decal.Text;
                Operation = decal.Operation;
                Target = decal.Target;
                Font = decal.Font;
                CapHeight = decal.CapHeight;
                Depth = decal.Depth;
                Tracking = decal.Tracking;
                Rotation = (int)decal.RotationDeg;
                Anchor = decal.Anchor;
                AnchorNormal = decal.AnchorNormal;
            }
            finally
            {
                _isSyncingFromModel = false;
            }

            UpdateTargetMesh();
            UpdateUVReadout();
            Invalidate();
        }
    }

    private void SyncActiveDecal()
    {
        if (_isSyncingFromModel || _isActivating) return;
        if (SelectedDecalId == Guid.Empty) return;

        var idx = _decals.FindIndex(d => d.Id == SelectedDecalId);
        if (idx >= 0)
        {
            _decals[idx] = new TextDecal
            {
                Id = SelectedDecalId,
                Text = LabelText,
                Operation = Operation,
                Target = Target,
                Font = Font,
                CapHeight = CapHeight,
                Depth = Depth,
                Tracking = Tracking,
                RotationDeg = Rotation,
                Anchor = Anchor,
                AnchorNormal = AnchorNormal
            };
        }

        var listItem = DecalList.FirstOrDefault(item => item.Id == SelectedDecalId);
        if (listItem != null)
        {
            listItem.Text = LabelText;
            listItem.Operation = Operation;
            listItem.Target = Target;
            listItem.CapHeight = CapHeight;
            listItem.HasMould = HasMould;
        }
    }

    private void SyncDecalList()
    {
        var currentIds = new HashSet<Guid>(_decals.Select(d => d.Id));

        for (int i = DecalList.Count - 1; i >= 0; i--)
        {
            if (!currentIds.Contains(DecalList[i].Id))
                DecalList.RemoveAt(i);
        }

        for (int i = 0; i < _decals.Count; i++)
        {
            var d = _decals[i];
            var existing = DecalList.FirstOrDefault(item => item.Id == d.Id);
            if (existing != null)
            {
                existing.Text = d.Text;
                existing.Operation = d.Operation;
                existing.Target = d.Target;
                existing.CapHeight = d.CapHeight;
                existing.IsSelected = d.Id == SelectedDecalId;
                existing.HasMould = HasMould;

                int oldIdx = DecalList.IndexOf(existing);
                if (oldIdx != i)
                {
                    DecalList.Move(oldIdx, i);
                }
            }
            else
            {
                var newItem = new TextDecalItemViewModel
                {
                    Id = d.Id,
                    Text = d.Text,
                    Operation = d.Operation,
                    Target = d.Target,
                    CapHeight = d.CapHeight,
                    IsSelected = d.Id == SelectedDecalId,
                    HasMould = HasMould
                };
                DecalList.Insert(i, newItem);
            }
        }
    }

    partial void OnTargetChanged(EmbossTarget value)
    {
        UpdateTargetMesh();
        if (SelectedDecalId != Guid.Empty)
        {
            SyncActiveDecal();
        }
        UpdatePresets();
        UpdateUVReadout();
        Invalidate();
    }

    partial void OnLabelTextChanged(string value) { SyncActiveDecal(); Invalidate(); }
    partial void OnOperationChanged(EmbossOperation value)
    {
        OnPropertyChanged(nameof(DepthLabel));
        OnPropertyChanged(nameof(ApplyLabel));
        OnPropertyChanged(nameof(TextStatusLine));
        SyncActiveDecal();
        Invalidate();
    }
    partial void OnFontChanged(DecalFont value) { SyncActiveDecal(); Invalidate(); }
    partial void OnCapHeightChanged(float value) { SyncActiveDecal(); Invalidate(); }
    partial void OnDepthChanged(float value) { SyncActiveDecal(); Invalidate(); }
    partial void OnTrackingChanged(float value) { SyncActiveDecal(); Invalidate(); }
    partial void OnRotationChanged(int value) { SyncActiveDecal(); Invalidate(); }
    partial void OnAnchorChanged(Vector3 value) { SyncActiveDecal(); Invalidate(); }
    partial void OnAnchorNormalChanged(Vector3 value) { SyncActiveDecal(); Invalidate(); }

    private void EnsureCleanMeshForPreview()
    {
        if (IsApplied && _activeMesh != null)
        {
            var cleanBase = CommandReplay.GetMeshAtStage(_engine, _activeMesh, CommandPriority.Transform);
            if (cleanBase.IsSuccess)
            {
                _baseMesh = cleanBase.Value;
            }

            if (HasMould && _mouldMesh != null)
            {
                var cleanMould = CommandReplay.GetMeshAtStage(_engine, _mouldMesh, CommandPriority.Mould);
                if (cleanMould.IsSuccess)
                {
                    _mouldMesh = cleanMould.Value;
                }
            }

            _targetMesh = (Target == EmbossTarget.Mould && _mouldMesh != null) ? _mouldMesh : _baseMesh;
            if (_targetMesh != null)
            {
                _sceneManager.UpdateMesh(_targetMesh);
            }

            IsApplied = false;
            OnPropertyChanged(nameof(StatusWord));
            OnPropertyChanged(nameof(StatusColor));
        }
    }

    private void Invalidate()
    {
        if (_isSyncingFromModel || _isActivating) return;
        EnsureCleanMeshForPreview();
        _metrics = _outlineSource.MeasureText(LabelText, Font, CapHeight, Tracking);
        OnPropertyChanged(nameof(Footprint));
        OnPropertyChanged(nameof(TextStatusLine));
        OnPropertyChanged(nameof(DecalCount));
        UpdateUVReadout();
        _previewPending = true;
    }

    private void ExecutePreviewUpdate()
    {
        if (_targetMesh == null) return;
        _sceneManager.UpdateDecals(_decals, SelectedDecalId, _outlineSource, Target);
    }

    private void UpdateUVReadout()
    {
        if (_targetMesh == null) return;
        var diff = Anchor - _meshCenter;
        var frame = DecalFrame.FromHit(Anchor, AnchorNormal, Rotation);
        _uv = new Vector2(Vector3.Dot(diff, frame.U), Vector3.Dot(diff, frame.V));
        OnPropertyChanged(nameof(PositionU));
        OnPropertyChanged(nameof(PositionV));
        OnPropertyChanged(nameof(PositionUv));
    }

    private void OnDecalSelected(Guid decalId)
    {
        SelectedDecalId = decalId;
    }

    private void OnDecalMoved(Guid decalId, Vector3 point, Vector3 normal)
    {
        EnsureCleanMeshForPreview();
        var idx = _decals.FindIndex(d => d.Id == decalId);
        if (idx >= 0)
        {
            _decals[idx] = _decals[idx] with { Anchor = point, AnchorNormal = normal };
            if (decalId == SelectedDecalId)
            {
                _isSyncingFromModel = true;
                try
                {
                    Anchor = point;
                    AnchorNormal = normal;
                }
                finally
                {
                    _isSyncingFromModel = false;
                }
                UpdateUVReadout();
                _sceneManager.UpdateDragPreview(_decals[idx], _metrics);
            }
        }
    }

    private void OnDecalDragCompleted(Guid decalId)
    {
        Invalidate();
    }

    [RelayCommand]
    public void AddDecal()
    {
        EnsureCleanMeshForPreview();
        var target = Target;
        var presets = ActivePresetPoints;

        // Find the first free anchor in the currently viewed target
        DecalPresetPoint? freeAnchor = null;
        if (presets.Count > 0)
        {
            foreach (var preset in presets)
            {
                bool isOccupied = _decals.Any(d => d.Target == target && Vector3.Distance(d.Anchor, preset.Position) < AnchorOccupiedRadiusMm);
                if (!isOccupied)
                {
                    freeAnchor = preset;
                    break;
                }
            }
            freeAnchor ??= presets[0];
        }

        string text = TextDecal.DefaultText;
        float capHeight = SuggestedCapHeight(freeAnchor, target, text.Length, CapHeight);

        var newDecal = new TextDecal
        {
            Id = Guid.NewGuid(),
            Text = text,
            Operation = Operation,
            Target = target,
            Font = Font,
            CapHeight = capHeight,
            Depth = Depth,
            Tracking = Tracking,
            RotationDeg = freeAnchor != null ? (int)freeAnchor.RotationDeg : Rotation,
            Anchor = freeAnchor?.Position ?? _meshCenter,
            AnchorNormal = freeAnchor?.Normal ?? Vector3.UnitZ
        };

        _decals.Add(newDecal);
        SelectedDecalId = newDecal.Id;
        SyncDecalList();
        OnPropertyChanged(nameof(DecalCount));
        Invalidate();
    }

    [RelayCommand]
    public void SelectDecal(Guid id)
    {
        SelectedDecalId = id;
    }

    [RelayCommand]
    public void DeleteDecalById(Guid id)
    {
        if (_decals.Count == 0) return;

        _decals = _decals.Where(d => d.Id != id).ToList();
        if (SelectedDecalId == id)
        {
            SelectedDecalId = Guid.Empty;
        }
        SyncDecalList();
        OnPropertyChanged(nameof(DecalCount));
        Invalidate();
    }

    [RelayCommand]
    public void DeleteSelectedDecal()
    {
        if (SelectedDecalId == Guid.Empty || _decals.Count == 0) return;

        DeleteDecalById(SelectedDecalId);
    }

    [RelayCommand]
    public void ClearDecals()
    {
        // If the decals are already booleaned into the mesh, revert that first. Otherwise the
        // list empties while the geometry keeps them, and nothing short of Clear reconciles the two.
        if (IsApplied)
        {
            ClearText();
            if (IsApplied) return; // revert failed; ClearText has already surfaced the error
        }

        _decals.Clear();
        SelectedDecalId = Guid.Empty;
        SyncDecalList();
        OnPropertyChanged(nameof(DecalCount));
        _sceneManager.ClearPreviewVisuals();
        Invalidate();
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (_decals.Count == 0) return;

        ErrorText = string.Empty;
        WarningText = string.Empty;

        var warnings = new List<string>();
        var decalsSnapshot = _decals.ToList();

        var baseDecals = decalsSnapshot.Where(d => d.Target == EmbossTarget.Base).ToList();
        var mouldDecals = (HasMould && _mouldMesh != null)
            ? decalsSnapshot.Where(d => d.Target == EmbossTarget.Mould).ToList()
            : new List<TextDecal>();

        if (!HasMould)
        {
            baseDecals = decalsSnapshot;
            mouldDecals = [];
        }

        // 1. Obtain clean stage base mesh (at Transform stage)
        IMesh activeMesh;
        if (_activeMesh is not null)
        {
            activeMesh = _activeMesh;
        }
        else
        {
            var activeMeshResult = Workspace.GetActiveMesh();
            if (activeMeshResult.IsFailure)
            {
                ErrorText = activeMeshResult.Error.Description;
                _alert.ShowError(activeMeshResult.Error.Description);
                return;
            }
            activeMesh = activeMeshResult.Value;
        }

        var cleanBaseResult = CommandReplay.GetMeshAtStage(_engine, activeMesh, CommandPriority.Transform);
        if (cleanBaseResult.IsFailure)
        {
            ErrorText = cleanBaseResult.Error.Description;
            _alert.ShowError(cleanBaseResult.Error.Description);
            return;
        }
        var cleanBaseMesh = cleanBaseResult.Value;

        // 2. Apply Base Decals if any
        IMesh appliedBaseMesh = cleanBaseMesh;
        MeshMetadata baseMetadata = cleanBaseMesh.Metadata;

        if (baseDecals.Count > 0)
        {
            var baseApplyResult = await _generator.ExecuteAsync(_engine, cleanBaseMesh, baseDecals, warnings);
            if (baseApplyResult.IsFailure)
            {
                ErrorText = baseApplyResult.Error.Description;
                _alert.ShowError(baseApplyResult.Error.Description);
                return;
            }
            appliedBaseMesh = baseApplyResult.Value;

            baseMetadata = cleanBaseMesh.Metadata
                .WithCommand(new DecalCommand(baseDecals));

            appliedBaseMesh = appliedBaseMesh.WithRefreshedStatsAndTopology(_engine, baseMetadata);
        }

        IMesh meshToSave = appliedBaseMesh;

        // 3. If Mould exists, re-generate mould from appliedBaseMesh, then apply Mould Decals
        if (HasMould && _mouldMesh is not null)
        {
            var mouldDef = _mouldMesh.Metadata.MouldDefinition();
            if (mouldDef.HasNoValue)
            {
                // Without a definition the mould cannot be regenerated over the embossed base,
                // so saving here would leave the workspace holding a mould that no longer matches.
                const string message = "This mould has no saved definition, so it cannot be rebuilt around the decals. Regenerate the mould and try again.";
                ErrorText = message;
                _alert.ShowError(message);
                return;
            }

            var mouldApplyResult = await Task.Run(() => mouldDef.Value.Apply(_engine, appliedBaseMesh));
            if (mouldApplyResult.IsFailure)
            {
                ErrorText = mouldApplyResult.Error.Description;
                _alert.ShowError(mouldApplyResult.Error.Description);
                return;
            }

            var rawMouldMesh = mouldApplyResult.Value;

            var mouldMetadata = appliedBaseMesh.Metadata
                .WithCommand(mouldDef.Value with { TargetMeshId = appliedBaseMesh.Metadata.Id });

            IMesh appliedMouldMesh = rawMouldMesh;

            if (mouldDecals.Count > 0)
            {
                var mouldDecalApplyResult = await _generator.ExecuteAsync(_engine, rawMouldMesh, mouldDecals, warnings);
                if (mouldDecalApplyResult.IsFailure)
                {
                    ErrorText = mouldDecalApplyResult.Error.Description;
                    _alert.ShowError(mouldDecalApplyResult.Error.Description);
                    return;
                }

                appliedMouldMesh = mouldDecalApplyResult.Value;
                mouldMetadata = mouldMetadata.WithCommand(new MouldDecalCommand(mouldDecals));
            }

            appliedMouldMesh = appliedMouldMesh.WithRefreshedStatsAndTopology(_engine, mouldMetadata);
            meshToSave = appliedMouldMesh;
            _mouldMesh = appliedMouldMesh;
            _baseMesh = appliedBaseMesh;
        }
        else
        {
            _baseMesh = appliedBaseMesh;
            _mouldMesh = null;
        }

        if (warnings.Count > 0)
        {
            WarningText = string.Join(" · ", warnings.Distinct());
        }

        var updateResult = Workspace.UpdateMesh(meshToSave);
        if (updateResult.IsFailure)
        {
            ErrorText = updateResult.Error.Description;
            _alert.ShowError(updateResult.Error.Description);
            return;
        }

        Workspace = updateResult.Value;
        _activeMesh = meshToSave;
        IsApplied = true;

        UpdateTargetMesh();

        _sceneManager.ClearPreviewVisuals();
        _sceneManager.ClearPresetVisuals();
        OnPropertyChanged(nameof(StatusWord));
        OnPropertyChanged(nameof(StatusColor));
        _messenger.Send(new WorkspaceChangedMessage(Workspace));
    }

    [RelayCommand]
    public void ClearText()
    {
        if (!IsApplied) return;

        var result = _clearDecals.Execute(Workspace);
        if (result.IsFailure)
        {
            _alert.ShowError(result.Error.Description);
            return;
        }

        Workspace = result.Value;
        IsApplied = false;
        IsDecalsExpanded = true;

        var activeResult = Workspace.GetActiveMesh();
        if (activeResult.IsSuccess)
        {
            _activeMesh = activeResult.Value;
            var mouldDef = _activeMesh.Metadata.MouldDefinition();
            HasMould = mouldDef.HasValue;
            if (HasMould)
            {
                _mouldMesh = _activeMesh;
                var baseMeshAtStage = CommandReplay.GetMeshAtStage(_engine, _activeMesh, CommandPriority.Transform);
                _baseMesh = baseMeshAtStage.IsSuccess ? baseMeshAtStage.Value : _activeMesh;
            }
            else
            {
                _baseMesh = _activeMesh;
                _mouldMesh = null;
                Target = EmbossTarget.Base;
            }

            SelectedDecalId = Guid.Empty;
            if (_decals.Count > 0)
            {
                Target = _decals[0].Target;
            }

            UpdateTargetMesh();
            UpdatePresets();
            SyncDecalList();
            Invalidate();
        }

        OnPropertyChanged(nameof(StatusWord));
        OnPropertyChanged(nameof(StatusColor));
        _messenger.Send(new WorkspaceChangedMessage(Workspace));
    }

    [RelayCommand]
    public void Clear() => ClearText();

    public async Task ActivateAsync(Workspace workspace)
    {
        _isActivating = true;
        // Restarted here rather than only in the constructor, so a view model that is
        // deactivated and activated again keeps updating its preview.
        _previewTimer.Start();
        try
        {
            await Task.Yield();
            Workspace = workspace;

            var activeResult = Workspace.GetActiveMesh();
            if (activeResult.IsFailure) return;

            _activeMesh = activeResult.Value;

            // Check if active mesh has a MouldDefinition
            var mouldDef = _activeMesh.Metadata.MouldDefinition();
            HasMould = mouldDef.HasValue;

            if (HasMould)
            {
                _mouldMesh = _activeMesh;
                // Transform stage, matching ClearText and EnsureCleanMeshForPreview: the base mesh
                // under the mould must be free of decals, or previews stack on top of applied ones.
                var baseMeshAtStage = CommandReplay.GetMeshAtStage(_engine, _activeMesh, CommandPriority.Transform);
                _baseMesh = baseMeshAtStage.IsSuccess ? baseMeshAtStage.Value : _activeMesh;
                Target = EmbossTarget.Mould;
            }
            else
            {
                _baseMesh = _activeMesh;
                _mouldMesh = null;
                Target = EmbossTarget.Base;
            }

            var savedDecals = _activeMesh.Metadata.TextDecals();
            if (savedDecals.HasNoValue && _baseMesh != null)
                savedDecals = _baseMesh.Metadata.TextDecals();

            if (savedDecals.HasValue && savedDecals.Value.Count > 0)
            {
                _decals = savedDecals.Value.ToList();
                IsApplied = true;
                SelectedDecalId = Guid.Empty;
                Target = _decals[0].Target;
            }
            else
            {
                IsApplied = false;
                Target = HasMould ? EmbossTarget.Mould : EmbossTarget.Base;
                UpdatePresets();

                if (HasMould && _mouldPresetPoints.Count > 0)
                {
                    // 1. File name decal on Front anchor
                    var frontPreset = _mouldPresetPoints.FirstOrDefault(p => p.Name == "Front") ?? _mouldPresetPoints[0];
                    string rawName = !string.IsNullOrWhiteSpace(_baseMesh?.Metadata.Name)
                        ? _baseMesh.Metadata.Name
                        : !string.IsNullOrWhiteSpace(_activeMesh?.Metadata.Name)
                            ? _activeMesh.Metadata.Name
                            : "FABOLUS";
                    string fileName = Path.GetFileNameWithoutExtension(rawName);
                    if (string.IsNullOrWhiteSpace(fileName)) fileName = "FABOLUS";

                    float capHeight1 = SuggestedCapHeight(frontPreset, EmbossTarget.Mould, fileName.Length, TextDecal.DefaultCapHeight);

                    var fileDecal = new TextDecal
                    {
                        Id = Guid.NewGuid(),
                        Text = fileName,
                        Operation = EmbossOperation.Engrave,
                        Target = EmbossTarget.Mould,
                        Font = DecalFont.Sans,
                        CapHeight = capHeight1,
                        Depth = 0.8f,
                        Tracking = 0.4f,
                        RotationDeg = (int)frontPreset.RotationDeg,
                        Anchor = frontPreset.Position,
                        AnchorNormal = frontPreset.Normal
                    };

                    // 2. Base mesh volume decal on Back anchor
                    var backPreset = _mouldPresetPoints.FirstOrDefault(p => p.Name == "Back")
                        ?? (_mouldPresetPoints.Count > 1 ? _mouldPresetPoints[1] : frontPreset);

                    double volume = 0.0;
                    if (_baseMesh != null)
                    {
                        var baseStats = _engine.Evaluators.GetStatistics(_baseMesh);
                        if (baseStats.IsSuccess)
                        {
                            volume = baseStats.Value.Volume;
                        }
                    }
                    string volumeText = volume > 0 ? $"{volume:0.0} cc" : "0.0 cc";

                    float capHeight2 = SuggestedCapHeight(backPreset, EmbossTarget.Mould, volumeText.Length, TextDecal.DefaultCapHeight);

                    var volumeDecal = new TextDecal
                    {
                        Id = Guid.NewGuid(),
                        Text = volumeText,
                        Operation = EmbossOperation.Engrave,
                        Target = EmbossTarget.Mould,
                        Font = DecalFont.Sans,
                        CapHeight = capHeight2,
                        Depth = 0.8f,
                        Tracking = 0.4f,
                        RotationDeg = (int)backPreset.RotationDeg,
                        Anchor = backPreset.Position,
                        AnchorNormal = backPreset.Normal
                    };

                    _decals = [fileDecal, volumeDecal];
                    SelectedDecalId = Guid.Empty;
                }
                else
                {
                    var presets = ActivePresetPoints;
                    var firstAnchor = presets.Count > 0 ? presets[0] : null;

                    string text = TextDecal.DefaultText;
                    float capHeight = SuggestedCapHeight(firstAnchor, Target, text.Length, TextDecal.DefaultCapHeight);

                    var defaultDecal = new TextDecal
                    {
                        Id = Guid.NewGuid(),
                        Text = text,
                        Operation = EmbossOperation.Engrave,
                        Target = Target,
                        Font = DecalFont.Sans,
                        CapHeight = capHeight,
                        Depth = 0.8f,
                        Tracking = 0.4f,
                        RotationDeg = firstAnchor != null ? (int)firstAnchor.RotationDeg : 0,
                        Anchor = firstAnchor?.Position ?? _meshCenter,
                        AnchorNormal = firstAnchor?.Normal ?? Vector3.UnitZ
                    };
                    _decals = [defaultDecal];
                    SelectedDecalId = Guid.Empty;
                }
            }

            UpdateTargetMesh();

            UpdatePresets();
            IsDecalsExpanded = !IsApplied;
            SyncDecalList();

            _metrics = _outlineSource.MeasureText(LabelText, Font, CapHeight, Tracking);
            UpdateUVReadout();
            OnPropertyChanged(nameof(StatusWord));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(TextStatusLine));
            OnPropertyChanged(nameof(ApplyLabel));
            OnPropertyChanged(nameof(DepthLabel));
            OnPropertyChanged(nameof(Footprint));
            OnPropertyChanged(nameof(DecalCount));

            if (!IsApplied)
            {
                ExecutePreviewUpdate();
            }
            else
            {
                _sceneManager.ClearPreviewVisuals();
            }
        }
        finally
        {
            _isActivating = false;
        }
    }

    public Task<Workspace> DeactivateAsync()
    {
        _previewPending = false;
        _previewTimer.Stop();
        _sceneManager.ReleaseMesh();
        return Task.FromResult(Workspace);
    }

    public void Dispose()
    {
        _previewTimer.Stop();
    }
}

public partial class TextDecalItemViewModel : ObservableObject
{
    public Guid Id { get; init; }
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private EmbossOperation _operation = EmbossOperation.Emboss;
    [ObservableProperty] private EmbossTarget _target = EmbossTarget.Base;
    [ObservableProperty] private float _capHeight = 6.0f;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _hasMould;

    public string DisplayText => string.IsNullOrWhiteSpace(Text) ? "(empty)" : Text;
    public string OperationText => Operation.ToString();
    public string TargetText => Target.ToString();

    partial void OnTextChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayText));
    }

    partial void OnOperationChanged(EmbossOperation value)
    {
        OnPropertyChanged(nameof(OperationText));
    }

    partial void OnTargetChanged(EmbossTarget value)
    {
        OnPropertyChanged(nameof(TargetText));
    }

}
