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

    private TextMetrics _metrics = TextMetrics.Empty;
    private Vector3 _meshCenter = Vector3.Zero;
    private Vector2 _uv = Vector2.Zero;

    private bool _isActivating;
    private readonly DispatcherTimer _previewTimer;
    private bool _previewPending;

    [ObservableProperty] private string _labelText = "FABOLUS";
    [ObservableProperty] private EmbossOperation _operation = EmbossOperation.Emboss;
    [ObservableProperty] private EmbossTarget _target = EmbossTarget.Base;
    [ObservableProperty] private DecalFont _font = DecalFont.Sans;
    [ObservableProperty] private float _capHeight = 6.0f;
    [ObservableProperty] private float _depth = 0.8f;
    [ObservableProperty] private float _tracking = 0.4f;
    [ObservableProperty] private int _rotation = 0;
    [ObservableProperty] private bool _projectOntoSurface = true;
    [ObservableProperty] private bool _isPicking = false;
    [ObservableProperty] private bool _isApplied = false;
    [ObservableProperty] private bool _hasMould = false;
    [ObservableProperty] private Vector3 _anchor = Vector3.Zero;
    [ObservableProperty] private Vector3 _anchorNormal = Vector3.UnitZ;
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private string _warningText = string.Empty;

    // Read-only dynamic properties
    public string DepthLabel => Operation == EmbossOperation.Engrave ? "Depth" : "Height";
    public string ApplyLabel => Operation == EmbossOperation.Engrave ? "Apply engraving" : "Apply emboss";
    public string Footprint => $"{_metrics.WidthMm:0.0} × {CapHeight:0.0} mm";
    public string PositionU => $"{_uv.X:0.0} mm";
    public string PositionV => $"{_uv.Y:0.0} mm";
    public string PositionUv => $"{_uv.X:0.0} mm, {_uv.Y:0.0} mm";
    public string StatusWord => IsApplied ? "Applied" : "Preview";
    public string StatusColor => IsApplied ? "#2FA36B" : "#E0A024";
    public string Hint => IsPicking
        ? "Move over the surface, click to drop the label (Esc to cancel)"
        : "Drag the label to move · drag the top handle to rotate";
    public string TextStatusLine =>
        $"{Operation} · {LabelText.Length} glyphs · {CapHeight:0.0} mm cap · {Depth:0.0} mm {DepthLabel.ToLower()} · {Rotation}°";

    public ISceneManager SceneManager => _sceneManager;

    public EmbossViewModel(IMessenger messenger, IAlertDialog alert, IGeometryEngine engine, IGlyphOutlineSource outlineSource)
    {
        _messenger = messenger;
        _alert = alert;
        _engine = engine;
        _outlineSource = outlineSource;
        _tool = new TextEmbossTool(_outlineSource);
        _clearTextEmboss = new ClearTextEmboss(_engine);

        _sceneManager = new EmbossSceneManager(_engine, _messenger);
        _sceneManager.DecalPlaced += OnDecalPlaced;
        _sceneManager.DecalMoved += OnDecalMoved;
        _sceneManager.DecalRotated += OnDecalRotated;
        _sceneManager.DecalHovered += OnDecalHovered;

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

    partial void OnTargetChanged(EmbossTarget value)
    {
        UpdateTargetMesh();
        if (_targetMesh != null && !IsApplied)
        {
            Anchor = _meshCenter;
            AnchorNormal = Vector3.UnitZ;
        }
        UpdateUVReadout();
        Invalidate();
    }

    partial void OnLabelTextChanged(string value) => Invalidate();
    partial void OnOperationChanged(EmbossOperation value)
    {
        OnPropertyChanged(nameof(DepthLabel));
        OnPropertyChanged(nameof(ApplyLabel));
        OnPropertyChanged(nameof(TextStatusLine));
        Invalidate();
    }
    partial void OnFontChanged(DecalFont value) => Invalidate();
    partial void OnCapHeightChanged(float value) => Invalidate();
    partial void OnDepthChanged(float value) => Invalidate();
    partial void OnTrackingChanged(float value) => Invalidate();
    partial void OnRotationChanged(int value) => Invalidate();
    partial void OnProjectOntoSurfaceChanged(bool value) => Invalidate();
    partial void OnAnchorChanged(Vector3 value) => Invalidate();
    partial void OnAnchorNormalChanged(Vector3 value) => Invalidate();

    private void EnsureCleanMeshForPreview()
    {
        if (IsApplied && _activeMesh != null)
        {
            if (Target == EmbossTarget.Base)
            {
                var cleanBase = CommandReplay.GetMeshAtStage(_engine, _activeMesh, CommandPriority.Transform);
                if (cleanBase.IsSuccess)
                {
                    _baseMesh = cleanBase.Value;
                    _targetMesh = _baseMesh;
                    _sceneManager.UpdateMesh(_targetMesh);
                }
            }
            else if (Target == EmbossTarget.Mould && _mouldMesh != null)
            {
                var cleanMould = CommandReplay.GetMeshAtStage(_engine, _mouldMesh, CommandPriority.Mould);
                if (cleanMould.IsSuccess)
                {
                    _mouldMesh = cleanMould.Value;
                    _targetMesh = _mouldMesh;
                    _sceneManager.UpdateMesh(_targetMesh);
                }
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
        UpdateUVReadout();
        _previewPending = true;
    }

    private void ExecutePreviewUpdate()
    {
        if (_targetMesh == null) return;
        _sceneManager.UpdatePreview(ToDecal(), _metrics, _outlineSource);
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

    private void OnDecalPlaced(Vector3 point, Vector3 normal)
    {
        IsPicking = false;
        Anchor = point;
        AnchorNormal = normal;
        Invalidate();
    }

    private void OnDecalMoved(Vector3 point, Vector3 normal)
    {
        EnsureCleanMeshForPreview();
        Anchor = point;
        AnchorNormal = normal;
        UpdateUVReadout();
        _sceneManager.UpdateDragPreview(ToDecal(), _metrics);
    }

    private void OnDecalRotated(float angleDeg)
    {
        EnsureCleanMeshForPreview();
        Rotation = (int)Math.Round(angleDeg);
        UpdateUVReadout();
        ExecutePreviewUpdate();
    }

    private void OnDecalHovered(Vector3 point, Vector3 normal)
    {
        // Visual cue if needed
    }

    [RelayCommand]
    private void StartPlacing()
    {
        EnsureCleanMeshForPreview();
        IsPicking = !IsPicking;
        _sceneManager.IsPicking = IsPicking;
        OnPropertyChanged(nameof(Hint));
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (_targetMesh == null || string.IsNullOrWhiteSpace(LabelText)) return;

        ErrorText = string.Empty;
        WarningText = string.Empty;

        var decal = ToDecal();
        var warnings = new List<string>();

        // Obtain clean stage mesh if re-applying
        IMesh sourceMesh = _targetMesh;
        if (IsApplied && _activeMesh != null)
        {
            if (Target == EmbossTarget.Base)
            {
                var cleanBase = CommandReplay.GetMeshAtStage(_engine, _activeMesh, CommandPriority.Transform);
                if (cleanBase.IsSuccess) sourceMesh = cleanBase.Value;
            }
            else if (Target == EmbossTarget.Mould && _mouldMesh != null)
            {
                var cleanMould = CommandReplay.GetMeshAtStage(_engine, _mouldMesh, CommandPriority.Mould);
                if (cleanMould.IsSuccess) sourceMesh = cleanMould.Value;
            }
        }

        var result = await Task.Run(() => _tool.Apply(_engine, sourceMesh, decal, warnings));
        if (result.IsFailure)
        {
            ErrorText = result.Error.Description;
            _alert.ShowError(result.Error.Description);
            return;
        }

        if (warnings.Count > 0)
        {
            WarningText = string.Join(" · ", warnings.Distinct());
        }

        var newMesh = result.Value;

        var statsResult = _engine.Evaluators.GetStatistics(newMesh);
        var topoResult = _engine.Evaluators.ValidateTopology(newMesh);

        var metadata = sourceMesh.Metadata.WithProperties(m =>
        {
            if (statsResult.IsSuccess) m.Set(MeshIOKeys.Stats, statsResult.Value);
            if (topoResult.IsSuccess) m.Set(MeshIOKeys.Topology, topoResult.Value);
        });

        metadata = metadata.WithCommand(new TextEmbossCommand(decal, _outlineSource))
                           .WithTextDecal(decal);

        var updatedMesh = newMesh.WithMetadata(metadata);

        IMesh meshToSave = updatedMesh;

        if (Target == EmbossTarget.Base && _mouldMesh != null)
        {
            var mouldDef = _mouldMesh.Metadata.MouldDefinition();
            if (mouldDef.HasValue)
            {
                var mouldResult = mouldDef.Value.Apply(_engine, updatedMesh);
                if (mouldResult.IsFailure)
                {
                    ErrorText = mouldResult.Error.Description;
                    _alert.ShowError(mouldResult.Error.Description);
                    return;
                }

                var mouldMesh = mouldResult.Value;
                var mouldStats = _engine.Evaluators.GetStatistics(mouldMesh);
                var mouldTopo = _engine.Evaluators.ValidateTopology(mouldMesh);

                var mouldMetadata = updatedMesh.Metadata.WithProperties(m =>
                {
                    if (mouldStats.IsSuccess) m.Set(MeshIOKeys.Stats, mouldStats.Value);
                    if (mouldTopo.IsSuccess) m.Set(MeshIOKeys.Topology, mouldTopo.Value);
                });

                mouldMetadata = mouldMetadata.WithCommand(mouldDef.Value with { TargetMeshId = updatedMesh.Metadata.Id });
                meshToSave = mouldMesh.WithMetadata(mouldMetadata);
            }
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
        if (Target == EmbossTarget.Base && _mouldMesh != null)
        {
            _mouldMesh = meshToSave;
            _baseMesh = updatedMesh;
        }
        else if (Target == EmbossTarget.Mould)
        {
            _mouldMesh = meshToSave;
        }
        else
        {
            _baseMesh = meshToSave;
        }
        _targetMesh = (Target == EmbossTarget.Mould && _mouldMesh != null) ? _mouldMesh : _baseMesh;
        HasMould = _mouldMesh != null;
        _sceneManager.UpdateMesh(_targetMesh);
        _sceneManager.ClearPreviewVisuals();
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

        var activeResult = Workspace.GetActiveMesh();
        if (activeResult.IsSuccess)
        {
            _activeMesh = activeResult.Value;
            var mouldDef = _activeMesh.Metadata.MouldDefinition();
            if (mouldDef.HasValue)
            {
                _mouldMesh = _activeMesh;
                var baseMeshAtStage = CommandReplay.GetMeshAtStage(_engine, _activeMesh, CommandPriority.Transform);
                _baseMesh = baseMeshAtStage.IsSuccess ? baseMeshAtStage.Value : _activeMesh;
            }
            else
            {
                _baseMesh = _activeMesh;
                _mouldMesh = null;
            }

            HasMould = _mouldMesh != null;
            if (!HasMould) Target = EmbossTarget.Base;

            UpdateTargetMesh();
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
            if (mouldDef.HasValue)
            {
                _mouldMesh = _activeMesh;
                var hasTextCommand = _activeMesh.Metadata.TextDecal().HasValue;
                var targetStage = hasTextCommand ? CommandPriority.TextEmboss : CommandPriority.Transform;
                var baseMeshAtStage = CommandReplay.GetMeshAtStage(_engine, _activeMesh, targetStage);
                _baseMesh = baseMeshAtStage.IsSuccess ? baseMeshAtStage.Value : _activeMesh;
            }
            else
            {
                _baseMesh = _activeMesh;
                _mouldMesh = null;
            }

            HasMould = _mouldMesh != null;
            if (!HasMould) Target = EmbossTarget.Base;

            UpdateTargetMesh();

            var savedDecal = _activeMesh.Metadata.TextDecal();
            if (savedDecal.HasNoValue && _targetMesh != null)
                savedDecal = _targetMesh.Metadata.TextDecal();
            if (savedDecal.HasNoValue && _baseMesh != null)
                savedDecal = _baseMesh.Metadata.TextDecal();
            if (savedDecal.HasValue)
            {
                var d = savedDecal.Value;
                LabelText = d.Text;
                Operation = d.Operation;
                Target = d.Target;
                Font = d.Font;
                CapHeight = d.CapHeight;
                Depth = d.Depth;
                Tracking = d.Tracking;
                Rotation = (int)d.RotationDeg;
                ProjectOntoSurface = d.ProjectOntoSurface;
                Anchor = d.Anchor;
                AnchorNormal = d.AnchorNormal;
                IsApplied = true;

                if (mouldDef.HasValue && Target == EmbossTarget.Base)
                {
                    var baseMeshAtStage = CommandReplay.GetMeshAtStage(_engine, _activeMesh, CommandPriority.TextEmboss);
                    if (baseMeshAtStage.IsSuccess)
                    {
                        _baseMesh = baseMeshAtStage.Value;
                    }
                }

                UpdateTargetMesh();
            }
            else
            {
                if (_targetMesh != null)
                {
                    Anchor = _meshCenter;
                    AnchorNormal = Vector3.UnitZ;
                }
                IsApplied = false;
            }

            _metrics = _outlineSource.MeasureText(LabelText, Font, CapHeight, Tracking);
            UpdateUVReadout();
            OnPropertyChanged(nameof(StatusWord));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(TextStatusLine));
            OnPropertyChanged(nameof(ApplyLabel));
            OnPropertyChanged(nameof(DepthLabel));
            OnPropertyChanged(nameof(Footprint));

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

    public TextDecal ToDecal() => new()
    {
        Text = LabelText,
        Operation = Operation,
        Target = Target,
        Font = Font,
        CapHeight = CapHeight,
        Depth = Depth,
        Tracking = Tracking,
        RotationDeg = Rotation,
        ProjectOntoSurface = ProjectOntoSurface,
        Anchor = Anchor,
        AnchorNormal = AnchorNormal
    };
}
