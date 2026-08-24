using System.Collections.ObjectModel;
using System.Numerics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common;
using Fabolus.Core.Features.Emboss;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.Viewport;

namespace Fabolus.Wpf.Features.Emboss;

public partial class EmbossViewModel : ObservableObject, IViewState, IDisposable
{
    private readonly IMessenger _messenger;
    private readonly IAlertDialog _alert;
    private readonly IGeometryEngine _engine;
    private readonly IGlyphOutlineSource _outlineSource;
    private readonly TextEmbossTool _tool;
    private readonly ClearTextEmboss _clearTextEmboss;
    private readonly EmbossSceneManager _sceneManager;

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

    [ObservableProperty] private Guid _selectedDecalId = Guid.Empty;
    [ObservableProperty] private bool _isDecalsExpanded = true;
    [ObservableProperty] private string _labelText = "FABOLUS";
    [ObservableProperty] private EmbossOperation _operation = EmbossOperation.Emboss;
    [ObservableProperty] private EmbossTarget _target = EmbossTarget.Base;
    [ObservableProperty] private DecalFont _font = DecalFont.Sans;
    [ObservableProperty] private float _capHeight = 6.0f;
    [ObservableProperty] private float _depth = 0.8f;
    [ObservableProperty] private float _tracking = 0.4f;
    [ObservableProperty] private int _rotation = 0;
    [ObservableProperty] private bool _isPicking = false;
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
    public string Hint => IsPicking
        ? "Move over the surface and click to drop a new decal (Esc to cancel)"
        : "Click a decal to select/drag · adjust rotation and parameters in the panel";
    public string TextStatusLine =>
        $"{Operation} · {LabelText.Length} glyphs · {CapHeight:0.0} mm cap · {Depth:0.0} mm {DepthLabel.ToLower()} · {Rotation}°";

    public ISceneManager SceneManager => _sceneManager;

    private IReadOnlyList<DecalPresetPoint> _mouldPresetPoints = [];
    public IReadOnlyList<DecalPresetPoint> MouldPresetPoints => _mouldPresetPoints;

    public EmbossViewModel(IMessenger messenger, IAlertDialog alert, IGeometryEngine engine, IGlyphOutlineSource outlineSource)
    {
        _messenger = messenger;
        _alert = alert;
        _engine = engine;
        _outlineSource = outlineSource;
        _tool = new TextEmbossTool(_outlineSource);
        _clearTextEmboss = new ClearTextEmboss(_engine);

        _sceneManager = new EmbossSceneManager(_engine, _messenger);
        _sceneManager.DecalSelected += OnDecalSelected;
        _sceneManager.DecalPlaced += OnDecalPlaced;
        _sceneManager.DecalMoved += OnDecalMoved;
        _sceneManager.DecalHovered += OnDecalHovered;
        _sceneManager.PickingCancelled += OnPickingCancelled;
        _sceneManager.PresetPointSelected += OnPresetPointSelected;

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

    private void OnPresetPointSelected(DecalPresetPoint preset)
    {
        ApplyPreset(preset);
    }

    [RelayCommand]
    public void ApplyPreset(DecalPresetPoint? preset)
    {
        if (preset == null) return;

        Target = preset.Target;
        UpdateTargetMesh();

        if (SelectedDecalId == Guid.Empty || IsPicking)
        {
            OnDecalPlaced(preset.Position, preset.Normal);
        }
        else
        {
            Anchor = preset.Position;
            AnchorNormal = preset.Normal;
            SyncActiveDecal();
            UpdateUVReadout();
            Invalidate();
        }
    }

    [RelayCommand]
    public void ApplyPresetByName(string name)
    {
        var preset = _mouldPresetPoints.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (preset != null)
        {
            ApplyPreset(preset);
        }
    }

    private void UpdatePresets()
    {
        if (HasMould && _mouldMesh != null)
        {
            _mouldPresetPoints = MouldPresetPointsCalculator.Calculate(_engine, _mouldMesh);
        }
        else
        {
            _mouldPresetPoints = [];
        }
        OnPropertyChanged(nameof(MouldPresetPoints));
        _sceneManager.UpdatePresetPoints(_mouldPresetPoints, isVisible: !IsApplied && Target == EmbossTarget.Mould);
    }

    private void OnPickingCancelled()
    {
        IsPicking = false;
        if (SelectedDecalId == Guid.Empty && _decals.Count > 0)
        {
            SelectedDecalId = _decals[^1].Id;
        }
        OnPropertyChanged(nameof(Hint));
    }

    private void UpdateTargetMesh()
    {
        _targetMesh = (Target == EmbossTarget.Mould && _mouldMesh != null) ? _mouldMesh : _baseMesh;
        if (_targetMesh != null)
        {
            _sceneManager.UpdateMesh(_targetMesh);
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

    partial void OnSelectedDecalIdChanged(Guid value)
    {
        foreach (var item in DecalList)
        {
            item.IsSelected = item.Id == value;
        }

        if (value == Guid.Empty) return;

        if (IsPicking)
        {
            IsPicking = false;
            _sceneManager.IsPicking = false;
            OnPropertyChanged(nameof(Hint));
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
        if (_isSyncingFromModel || _isActivating || IsPicking) return;
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
        if (!IsPicking && SelectedDecalId != Guid.Empty)
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
        if (_isActivating) return;
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

    private void OnDecalPlaced(Vector3 point, Vector3 normal)
    {
        IsPicking = false;
        _sceneManager.IsPicking = false;
        OnPropertyChanged(nameof(Hint));

        var newDecal = new TextDecal
        {
            Id = Guid.NewGuid(),
            Text = LabelText,
            Operation = Operation,
            Target = Target,
            Font = Font,
            CapHeight = CapHeight,
            Depth = Depth,
            Tracking = Tracking,
            RotationDeg = Rotation,
            Anchor = point,
            AnchorNormal = normal
        };

        _decals.Add(newDecal);
        SelectedDecalId = newDecal.Id;
        SyncDecalList();
        OnPropertyChanged(nameof(DecalCount));
        Invalidate();
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
                Anchor = point;
                AnchorNormal = normal;
                _isSyncingFromModel = false;
                UpdateUVReadout();
                _sceneManager.UpdateDragPreview(_decals[idx], _metrics);
            }
        }
    }

    private void OnDecalHovered(Vector3 point, Vector3 normal)
    {
        // Visual hover feedback if needed
    }

    [RelayCommand]
    private void StartPlacing()
    {
        EnsureCleanMeshForPreview();
        IsPicking = !IsPicking;
        _sceneManager.IsPicking = IsPicking;
        if (IsPicking)
        {
            SelectedDecalId = Guid.Empty;
            foreach (var item in DecalList)
            {
                item.IsSelected = false;
            }
        }
        else if (_decals.Count > 0)
        {
            SelectedDecalId = _decals[^1].Id;
        }
        OnPropertyChanged(nameof(Hint));
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
            SelectedDecalId = _decals.LastOrDefault()?.Id ?? Guid.Empty;
        }
        if (SelectedDecalId == Guid.Empty)
        {
            IsPicking = true;
            _sceneManager.IsPicking = true;
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
        _decals.Clear();
        SelectedDecalId = Guid.Empty;
        IsPicking = true;
        _sceneManager.IsPicking = true;
        SyncDecalList();
        OnPropertyChanged(nameof(DecalCount));
        _sceneManager.ClearPreviewVisuals();
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
        var activeMesh = _activeMesh ?? Workspace.GetActiveMesh().Value;
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
            var baseApplyResult = await Task.Run(() => _tool.Apply(_engine, cleanBaseMesh, baseDecals, warnings));
            if (baseApplyResult.IsFailure)
            {
                ErrorText = baseApplyResult.Error.Description;
                _alert.ShowError(baseApplyResult.Error.Description);
                return;
            }
            appliedBaseMesh = baseApplyResult.Value;

            var stats = _engine.Evaluators.GetStatistics(appliedBaseMesh);
            var topo = _engine.Evaluators.ValidateTopology(appliedBaseMesh);

            baseMetadata = cleanBaseMesh.Metadata
                .WithProperties(m =>
                {
                    if (stats.IsSuccess) m.Set(MeshIOKeys.Stats, stats.Value);
                    if (topo.IsSuccess) m.Set(MeshIOKeys.Topology, topo.Value);
                })
                .WithCommand(new TextEmbossCommand(baseDecals, _outlineSource))
                .WithTextDecals(decalsSnapshot);

            appliedBaseMesh = appliedBaseMesh.WithMetadata(baseMetadata);
        }

        IMesh meshToSave = appliedBaseMesh;

        // 3. If Mould exists, re-generate mould from appliedBaseMesh, then apply Mould Decals
        if (HasMould && _mouldMesh != null)
        {
            var mouldDef = _mouldMesh.Metadata.MouldDefinition();
            if (mouldDef.HasValue)
            {
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
                    var mouldDecalApplyResult = await Task.Run(() => _tool.Apply(_engine, rawMouldMesh, mouldDecals, warnings));
                    if (mouldDecalApplyResult.IsFailure)
                    {
                        ErrorText = mouldDecalApplyResult.Error.Description;
                        _alert.ShowError(mouldDecalApplyResult.Error.Description);
                        return;
                    }

                    appliedMouldMesh = mouldDecalApplyResult.Value;
                    mouldMetadata = mouldMetadata.WithCommand(new MouldTextEmbossCommand(mouldDecals, _outlineSource));
                }

                var mouldStats = _engine.Evaluators.GetStatistics(appliedMouldMesh);
                var mouldTopo = _engine.Evaluators.ValidateTopology(appliedMouldMesh);

                mouldMetadata = mouldMetadata
                    .WithProperties(m =>
                    {
                        if (mouldStats.IsSuccess) m.Set(MeshIOKeys.Stats, mouldStats.Value);
                        if (mouldTopo.IsSuccess) m.Set(MeshIOKeys.Topology, mouldTopo.Value);
                    })
                    .WithTextDecals(decalsSnapshot);

                appliedMouldMesh = appliedMouldMesh.WithMetadata(mouldMetadata);
                meshToSave = appliedMouldMesh;
                _mouldMesh = appliedMouldMesh;
                _baseMesh = appliedBaseMesh;
            }
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
        _targetMesh = (Target == EmbossTarget.Mould && _mouldMesh != null) ? _mouldMesh : _baseMesh;
        _sceneManager.UpdateMesh(_targetMesh);
        _sceneManager.ClearPreviewVisuals();
        _sceneManager.ClearPresetVisuals();
        IsApplied = true;
        OnPropertyChanged(nameof(StatusWord));
        OnPropertyChanged(nameof(StatusColor));
        _messenger.Send(new WorkspaceChangedMessage(Workspace));
    }

    [RelayCommand]
    public void ClearText()
    {
        if (!IsApplied) return;

        var result = _clearTextEmboss.Execute(Workspace);
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

            if (_decals.Count > 0)
            {
                SelectedDecalId = _decals[0].Id;
                Target = _decals[0].Target;
                IsPicking = false;
                _sceneManager.IsPicking = false;
            }
            else
            {
                SelectedDecalId = Guid.Empty;
                IsPicking = true;
                _sceneManager.IsPicking = true;
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
                var baseMeshAtStage = CommandReplay.GetMeshAtStage(_engine, _activeMesh, CommandPriority.TextEmboss);
                _baseMesh = baseMeshAtStage.IsSuccess ? baseMeshAtStage.Value : _activeMesh;
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
                SelectedDecalId = _decals[0].Id;
                Target = _decals[0].Target;
                IsPicking = false;
                _sceneManager.IsPicking = false;
            }
            else
            {
                IsApplied = false;
                var defaultDecal = new TextDecal
                {
                    Id = Guid.NewGuid(),
                    Text = "FABOLUS",
                    Operation = EmbossOperation.Emboss,
                    Target = EmbossTarget.Base,
                    Font = DecalFont.Sans,
                    CapHeight = 6.0f,
                    Depth = 0.8f,
                    Tracking = 0.4f,
                    RotationDeg = 0f,
                    Anchor = _meshCenter,
                    AnchorNormal = Vector3.UnitZ
                };
                _decals = [defaultDecal];
                SelectedDecalId = defaultDecal.Id;
                IsPicking = false;
                _sceneManager.IsPicking = false;
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
            OnPropertyChanged(nameof(Hint));

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
    public string Summary => HasMould
        ? $"{CapHeight:0.0} mm · {Operation} · {Target}"
        : $"{CapHeight:0.0} mm · {Operation}";

    partial void OnTextChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayText));
    }

    partial void OnOperationChanged(EmbossOperation value)
    {
        OnPropertyChanged(nameof(OperationText));
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnTargetChanged(EmbossTarget value)
    {
        OnPropertyChanged(nameof(TargetText));
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnCapHeightChanged(float value)
    {
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnHasMouldChanged(bool value)
    {
        OnPropertyChanged(nameof(Summary));
    }
}
