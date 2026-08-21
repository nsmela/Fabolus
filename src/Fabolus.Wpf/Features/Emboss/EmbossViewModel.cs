using System.Numerics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common;
using Fabolus.Core.Features.Emboss;
using Fabolus.Core.Features.MeshIO;
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
    private readonly EmbossSceneManager _sceneManager;

    private Workspace Workspace { get; set; } = Workspace.CreateEmpty();
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
    [ObservableProperty] private bool _mirror = false;
    [ObservableProperty] private bool _isPicking = false;
    [ObservableProperty] private bool _isApplied = false;
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

    partial void OnTargetChanged(EmbossTarget value)
    {
        // Target = Mould -> Mirror = true automatically, Target = Base -> Mirror = false
        Mirror = value == EmbossTarget.Mould;
        Invalidate();
    }

    partial void OnMirrorChanged(bool value)
    {
        Invalidate();
    }

    partial void OnLabelTextChanged(string value) => Invalidate();
    partial void OnOperationChanged(EmbossOperation value)
    {
        OnPropertyChanged(nameof(DepthLabel));
        OnPropertyChanged(nameof(ApplyLabel));
        Invalidate();
    }
    partial void OnFontChanged(DecalFont value) => Invalidate();
    partial void OnCapHeightChanged(float value) => Invalidate();
    partial void OnDepthChanged(float value) => Invalidate();
    partial void OnTrackingChanged(float value) => Invalidate();
    partial void OnRotationChanged(int value) => Invalidate();
    partial void OnProjectOntoSurfaceChanged(bool value) => Invalidate();

    partial void OnIsPickingChanged(bool value)
    {
        _sceneManager.IsPicking = value;
        OnPropertyChanged(nameof(Hint));
    }

    private void Invalidate()
    {
        if (_isActivating) return;

        IsApplied = false;
        ErrorText = string.Empty;
        WarningText = string.Empty;

        _metrics = _outlineSource.MeasureText(LabelText, Font, CapHeight, Tracking);
        OnPropertyChanged(nameof(Footprint));
        OnPropertyChanged(nameof(TextStatusLine));
        OnPropertyChanged(nameof(StatusWord));
        OnPropertyChanged(nameof(StatusColor));

        UpdateUVReadout();

        _previewPending = true;
        ApplyCommand.NotifyCanExecuteChanged();
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
        Anchor = point;
        AnchorNormal = normal;
        UpdateUVReadout();
        _previewPending = true;
    }

    private void OnDecalRotated(float degrees)
    {
        int rounded = (int)MathF.Round(degrees);
        // Normalize to -180..180
        while (rounded > 180) rounded -= 360;
        while (rounded < -180) rounded += 360;
        Rotation = rounded;
    }

    private void OnDecalHovered(Vector3 point, Vector3 normal)
    {
        Anchor = point;
        AnchorNormal = normal;
        _previewPending = true;
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
        Mirror = Mirror,
        Anchor = Anchor,
        AnchorNormal = AnchorNormal
    };

    [RelayCommand]
    private void StartPlacing()
    {
        IsPicking = !IsPicking;
    }

    private bool CanApply() => !string.IsNullOrWhiteSpace(LabelText) && !IsApplied && _targetMesh != null;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (_targetMesh == null) return;

        ErrorText = string.Empty;
        WarningText = string.Empty;

        var decal = ToDecal();
        var warnings = new List<string>();

        var result = await Task.Run(() => _tool.Apply(_engine, _targetMesh, decal, warnings));
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

        var metadata = _targetMesh.Metadata.WithProperties(m =>
        {
            if (statsResult.IsSuccess) m.Set(MeshIOKeys.Stats, statsResult.Value);
            if (topoResult.IsSuccess) m.Set(MeshIOKeys.Topology, topoResult.Value);
        });

        metadata = metadata.WithCommand(new TextEmbossCommand(decal, _outlineSource))
                           .WithTextDecal(decal);

        var finalMesh = newMesh.WithMetadata(metadata);
        var updateResult = Workspace.UpdateMesh(finalMesh);
        if (updateResult.IsSuccess)
        {
            Workspace = updateResult.Value;
            _targetMesh = finalMesh;
            _sceneManager.UpdateMesh(_targetMesh);
            _sceneManager.ClearPreviewVisuals();
            IsApplied = true;
            OnPropertyChanged(nameof(StatusWord));
            OnPropertyChanged(nameof(StatusColor));
            _messenger.Send(new WorkspaceChangedMessage(Workspace));
        }
    }

    [RelayCommand]
    private void Reset()
    {
        Rotation = 0;
        if (_targetMesh != null)
        {
            Anchor = _meshCenter;
            AnchorNormal = Vector3.UnitZ;
        }
        Invalidate();
    }

    public async Task ActivateAsync(Workspace workspace)
    {
        _isActivating = true;
        try
        {
            await Task.Yield();
            Workspace = workspace;

            var activeResult = Workspace.GetActiveMesh();
            if (activeResult.IsFailure) return;

            _targetMesh = activeResult.Value;
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

            var savedDecal = _targetMesh.Metadata.TextDecal();
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
                Mirror = d.Mirror;
                Anchor = d.Anchor;
                AnchorNormal = d.AnchorNormal;
                IsApplied = true;
            }
            else
            {
                Anchor = _meshCenter;
                AnchorNormal = Vector3.UnitZ;
                IsApplied = false;
            }

            _metrics = _outlineSource.MeasureText(LabelText, Font, CapHeight, Tracking);
            UpdateUVReadout();
            ExecutePreviewUpdate();
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
