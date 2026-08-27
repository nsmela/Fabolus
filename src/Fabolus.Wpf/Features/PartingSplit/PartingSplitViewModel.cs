using System.Linq;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.Main;
using Fabolus.Wpf.Features.Viewport;
using PartingLine = Fabolus.Core.Geometry.PartingLine;

namespace Fabolus.Wpf.Features.PartingSplit;

/// <summary>
/// Lets the user pick a pull direction, generate the parting line for the active mould along
/// it (surfacing any internal holes that need their own shut-off surface), and commit the
/// split into two new workspace meshes. Only meaningful for moulds - see <see cref="IsMould"/>.
///
/// The wizard walks <see cref="PartingSplitState"/> in order, and each stage isolates one artefact
/// of the split so it can be inspected before committing. Everything the later stages display is
/// produced by <see cref="GeneratePartingGeometryAsync"/> and <see cref="GenerateSplitAsync"/>.
/// </summary>
public partial class PartingSplitViewModel : ObservableObject, IViewState
{
    private readonly IAlertDialog _alert;
    private readonly IMessenger _messenger;
    private readonly PartingSplitSceneManager _sceneManager;

    /// <summary>Kept for the wall measurement the strategy assessment needs; every other geometry
    /// call goes through <see cref="_partingMeshFeature"/>.</summary>
    private readonly IGeometryEngine _engine;

    /// <summary>Every geometry call this view makes goes through here.</summary>
    private readonly PartingMeshFeature _partingMeshFeature;

    /// <summary>
    /// Committing the split is a workspace operation, so it goes to the feature that owns workspace
    /// mutation rather than through a pass-through on the geometry one.
    /// </summary>
    private readonly SplitMouldFeature _splitMouldFeature;

    /// <summary>Pull direction the view opens on. The scene is seeded from it.</summary>
    private static readonly Vector3 InitialDirection = Vector3.UnitY;

    /// <summary>Shown when a command is reached with a non-mould active mesh. The view hides those
    /// commands entirely, so this is a guard against a state the UI should not be able to produce.</summary>
    private const string MouldOnlyError = "Parting split only applies to moulds.";

    /// <summary>
    /// The two parameter sets that describe the split. Everything the user adjusts feeds into these,
    /// and they are what gets recorded on each half so the split can be replayed on import - so the
    /// halves are always reproducible from what the view was showing.
    /// </summary>
    public PartingLineParameters LineParameters => new()
    {
        Source = LineSource,
        PullDirection = Vector3.Normalize(Direction),
        NeutralBand = NeutralBand,
    };

    /// <summary>
    /// The parting mesh as a recipe: which axis it is built on, and how thick the cutter is. Axis
    /// carries the pull direction whichever mode is selected - under
    /// <see cref="PartingMeshAxisSource.PartingLine"/> it is ignored but for fixing which half comes
    /// back as Positive.
    ///
    /// <para>
    /// Deliberately unresolved. This is what gets recorded on the committed halves, so replay
    /// re-derives the axis from the line it re-traces rather than being handed one baked at commit
    /// time - which is what keeps an upstream change flowing through to the split. The interactive
    /// stages use <see cref="_resolvedMeshParameters"/> instead; see
    /// <see cref="PartingMeshFeature.ResolveAxis"/>.
    /// </para>
    /// </summary>
    public PartingMeshParameters MeshParameters => PartingMeshParameters.Default with
    {
        AxisSource = MeshAxisSource,
        Sweep = MeshSweep,
        SplitMethod = SplitMethod,
        Thickening = Thickening,
        Depth = CutterDepthMm,
        FlangeMaxSlopeDeg = FlangeMaxSlopeDeg,
        NormalFollowMm = NormalFollowMm,
        RawFlange = RawFlange,
        NormalSmoothingPasses = NormalSmoothingPasses,
        Axis = Vector3.Normalize(Direction),
    };

    // ---------------------------------------------------------------- the settled recipe
    //
    // These five were radio groups in step one and a slider in step two, kept side by side while the
    // alternatives were being compared on real bodies. They are settled now, so they are stated here
    // as one recipe rather than left switchable.
    //
    // Fixed here and not in PartingMeshParameters, whose own defaults are what an older save file
    // replays with and must keep meaning what they meant when it was committed. This is what new work
    // gets. Each still names the enum member rather than relying on a default, so the recipe reads in
    // one place and a change to any record default cannot silently move it.

    /// <summary>
    /// The body's own extruded border, which needs no pull direction - see
    /// <see cref="PartingLineSource.ExtrusionBorder"/>. The silhouette tracer is what the alternative
    /// was, and it only has an answer for a body that is not an extruded shell.
    /// </summary>
    public PartingLineSource LineSource => PartingLineSource.ExtrusionBorder;

    /// <summary>
    /// The parting line's own best-fit plane. Pairs with <see cref="LineSource"/> to settle the whole
    /// parting from the body's geometry - see <see cref="PartingMeshAxisSource.PartingLine"/>. This is
    /// what leaves the pull direction with nothing to decide but which half is called Positive.
    /// </summary>
    public PartingMeshAxisSource MeshAxisSource => PartingMeshAxisSource.PartingLine;

    /// <summary>
    /// The planar wavefront's footprint, but leaving the parting line along the body's own surface
    /// normal - <see cref="PartingMeshSweep.TangentLaunch"/>.
    ///
    /// <para>
    /// The footprint still comes from Clipper, so it provably cannot cross itself and the rim still
    /// reaches past the mould; only the height the flange leaves at changes. Measured against the
    /// normals the view draws, the flange's departure slope goes from 6 degrees to 43 on scalp where
    /// the normals ask for 48, and the mean disagreement across the line falls from 35 degrees to 11.
    /// Chin and nose move the same way, and all three still break their mould into halves within a
    /// couple of percent of even.
    /// </para>
    ///
    /// <para>
    /// Not <see cref="PartingMeshSweep.SurfaceSweep"/>, which marches in 3D along the normals. It is
    /// the more literal reading, but it does not reach the outer contour on its own and the far-field
    /// turn written to carry it there produced the flange that was rejected on sight.
    /// </para>
    /// </summary>
    public PartingMeshSweep MeshSweep => PartingMeshSweep.SurfaceSweep;

    /// <summary>
    /// How steep, in degrees from level, the flange is allowed to be.
    ///
    /// <para>
    /// Raised well past the 40 degrees the overhang relaxation defaults to, because that default is
    /// also the ceiling on how steeply the flange may leave the line, and the body's normals are
    /// routinely steeper: scalp's ask for 48. At 40 the launch is capped straight back and half the
    /// gain is lost - the disagreement with the normals sits at 18 degrees instead of 11.
    /// </para>
    ///
    /// <para>
    /// This is a real trade, not a free win. The 40-degree default was chosen to keep the flange under
    /// the support-free limit for FDM printing, so a mould parted on this surface will have faces that
    /// need support where the anatomy is steep. Lowering it back toward 45 keeps most of the
    /// normal-following and all of the printability.
    /// </para>
    /// </summary>
    public const float FlangeMaxSlopeDeg = 80f;

    /// <summary>
    /// How far out from the parting line the body's normal direction is held, in mm. Large enough to
    /// cover the whole flange, which is the point: the normal is not a launch angle to be shed once
    /// the flange is clear of the rim, it is the direction the surface is meant to keep.
    ///
    /// <para>
    /// At the 15mm this defaults to, the flange tracks the normals for about a centimetre and then
    /// peels off - measured on scalp, the disagreement holds near 11 degrees out to 10mm, reaches 16
    /// by 20mm and 30 by 40mm. Holding it the whole way keeps it near 10 throughout. It saturates
    /// past about 40mm on the bodies measured, since by then the flange has reached the outer contour,
    /// so this is comfortably "all of it" rather than a tuned distance.
    /// </para>
    ///
    /// <para>
    /// It costs nothing in the footprint: the 2D rings are unchanged, only the height they are lifted
    /// to, so triangle count and the halves that come out are identical at every value.
    /// </para>
    /// </summary>
    public const float NormalFollowMm = 100f;

    /// <summary>
    /// Show the sweep's own surface, with the height relaxation, the overhang relaxation and the
    /// inner-rim seal all switched off - see <see cref="PartingMeshParameters.RawFlange"/>.
    ///
    /// <para>
    /// Off. It was turned on to find out whether the flange's warping came from the sweep or from a
    /// pass running over it afterwards, and the answer was neither-of-those: raw and processed
    /// measured identical to a tenth of a degree at every distance out, because holding the normals
    /// across the whole flange pins every vertex and leaves those passes nothing to act on. The
    /// warping was the normal field's own scatter, and it is dealt with now by
    /// <see cref="NormalSmoothingPasses"/>.
    /// </para>
    ///
    /// <para>
    /// It must stay off in anything that cuts. The seal goes with the rest, and without it 77 rim
    /// points on scalp sit outside the body - each one a bridge of mould material the cut leaves
    /// behind.
    /// </para>
    /// </summary>
    public const bool RawFlange = false;

    /// <summary>
    /// How many times the flange's heights are averaged along each contour before it is returned.
    ///
    /// <para>
    /// This is what stops the surface corrugating. Each point of the parting line launches at the
    /// angle its own normal implies, and on a rim that is a crease those disagree sharply between
    /// neighbours - 8.9 degrees on average and 40 at worst on scalp, over points a millimetre apart.
    /// Averaging along the contour takes that out while leaving the slope across contours alone,
    /// which is the direction that follows the normals.
    /// </para>
    ///
    /// <para>
    /// Five, because more is worse in both directions at once. Measured worst-case ripple falls from
    /// 28.8 to 6.1 degrees on nose, 21.5 to 8.9 on scalp and 68.4 to 23.1 on chin, for about two
    /// degrees of adherence - and chin's adherence actually improves. By 20 passes scalp's ripple is
    /// climbing again as the surface starts to drift off the normals, and by 150 adherence has gone
    /// from 9 degrees to 29.
    /// </para>
    /// </summary>
    public const int NormalSmoothingPasses = 5;

    /// <summary>
    /// Subtract, then separate the pieces.
    ///
    /// <para>
    /// Pairs with <see cref="MeshSweep"/>: the half-space split builds its own tool from the flange
    /// surface rather than cutting with the slab on screen, so pointing this at the planar flange
    /// while step two shows the slab would have the user approving one solid and receiving another.
    /// The two move together or not at all.
    /// </para>
    /// </summary>
    public PartingSplitMethod SplitMethod => PartingSplitMethod.SeveredComponents;

    /// <summary>
    /// Copy the flange to two sheets and wall the gap - <see cref="PartingMeshThickening.Extrude"/>.
    /// Exact, instant, and it puts the cutter exactly where the flange is rather than wherever a grid
    /// could resolve it.
    ///
    /// <para>
    /// Not the offset. Offsetting sidestepped the crossings extrusion creates by reading the shape off
    /// a distance field, which cannot represent a crossing - but it pays for that in resolution: the
    /// grid has to resolve the wall, so the cutter had to be millimetres thick rather than tenths, and
    /// the gap between the halves had to be that thick with it. Repairing the crossings by cutting
    /// them out reaches a clean cutter without the grid, so the wall is free to be thin again - see
    /// <see cref="PartingMeshFeature.ExtrudeFlange"/>.
    /// </para>
    /// </summary>
    public PartingMeshThickening Thickening => PartingMeshThickening.Extrude;

    /// <summary>
    /// Wall thickness of the cutter, in mm, and so the gap the two halves come apart on.
    ///
    /// <para>
    /// A tenth of a millimetre: enough to sever, little enough that the two halves still meet along a
    /// seam rather than standing a visible gap apart. Nothing has to resolve it any more - the
    /// extrusion places both sheets exactly, and the repair that follows works on the faces that
    /// cross rather than on a sampled grid - so the thickness is chosen for the mould instead of for
    /// what a voxel budget could afford.
    /// </para>
    /// </summary>
    [ObservableProperty]
    private float _cutterDepthMm = PartingMeshParameters.DefaultDepth;

    partial void OnCutterDepthMmChanged(float value)
    {
        if (CurrentState == PartingSplitState.PartingMeshPreview)
        {
            UpdatePartingMesh();
        }
    }
    /// <summary>
    /// The draft-neutral band, in degrees either side of the silhouette. Only shades the model now -
    /// it fed the silhouette tracer, and the border line does not consult it - so it is held at the
    /// range the slider used to open on rather than exposed.
    /// </summary>
    private const float LowerRefAngle = -5f;
    private const float UpperRefAngle = 5f;

    /// <summary>
    /// Trailing debounce, in ms, on the live parting-line preview: coalesces a direction change into one
    /// recompute when it settles rather than one per tick.
    ///
    /// Only the first trace on a body pays it now. The extrusion border does not move with the pull
    /// direction, so every drag after that redraws the line already in hand - which matters, because
    /// measuring a body's wall thickness costs far more than the isoline pass it replaced.
    /// </summary>
    private const int LivePreviewDebounceMs = 30;

    private Workspace Workspace { get; set; }
    private bool _isUpdatingFromScene;
    private CancellationTokenSource? _livePreviewCts;
    private CancellationTokenSource? _partingMeshCts;

    /// <summary>
    /// The extrusion-border line for the current body, kept so dragging the direction redraws it
    /// rather than re-measuring the whole body's wall thickness. Cleared whenever the body changes,
    /// since it describes that body and nothing else.
    /// </summary>
    private PartingLine? _borderLine;

    /// <summary>The active mesh as a validated mould, set on activation only when it really is one.
    /// Every feature call that needs the mould goes through this rather than the raw active mesh.</summary>
    private MouldMesh? _mould;
    private BodyMesh _body;

    [ObservableProperty] private bool _isMould;
    [ObservableProperty] private IMesh? _activeMesh;
    [ObservableProperty] private IMesh? _baseTransformMesh;

    /// <summary>The extruded parting solid shown from <see cref="PartingSplitState.PartingMeshPreview"/> on.</summary>
    [ObservableProperty] private IMesh? _partingMesh;

    /// <summary>
    /// The two mould halves. Either both are set or neither is: the split reports a failure rather
    /// than returning a single piece, so stage five never has half a result to show.
    /// </summary>
    [ObservableProperty] private IMesh? _positiveRegionMesh;
    [ObservableProperty] private IMesh? _negativeRegionMesh;

    [ObservableProperty] private PartingSplitState _currentState = PartingSplitState.DirectionSelection;

    /// <summary>
    /// What the body's own shape says about how it should be parted, evaluated once when the view
    /// opens and shown on step one.
    ///
    /// <para>
    /// The source is settled - see <see cref="LineSource"/> - so this is not a control. It is there
    /// because the settled choice is only right for a body that has a rim dividing it, and whether a
    /// given body does is not something the user can see by looking. When the two disagree, the split
    /// will still run and will still produce something; what it produces is a line traced from a border
    /// the body does not really have, and the failure looks like a bad result rather than a wrong
    /// approach. Saying so before the user spends three steps on it is the point.
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStrategy))]
    [NotifyPropertyChangedFor(nameof(StrategyShape))]
    [NotifyPropertyChangedFor(nameof(StrategyContours))]
    [NotifyPropertyChangedFor(nameof(StrategyRims))]
    [NotifyPropertyChangedFor(nameof(StrategySummary))]
    [NotifyPropertyChangedFor(nameof(StrategyApproach))]
    [NotifyPropertyChangedFor(nameof(StrategyDisagrees))]
    private PartingStrategyReport? _strategy;

    public bool HasStrategy => Strategy is not null;

    public string StrategyShape => Strategy is null
        ? ""
        : Strategy.Shape switch
        {
            PartingBodyShape.Open => "Open mesh - not watertight",
            PartingBodyShape.Torus => $"Torus (χ {Strategy.EulerCharacteristic}, one hole through the body)",
            PartingBodyShape.MultipleHoles =>
                $"{Strategy.Genus} holes through the body (χ {Strategy.EulerCharacteristic})",
            _ => $"Shell (χ {Strategy.EulerCharacteristic})",
        };

    /// <summary>
    /// Reported as the rims together, not one at a time. A body with a hole is not parted by any single
    /// rim - the two sides still meet by going round through the hole - so a per-rim line reads as a
    /// failure on a body whose rims are exactly right.
    /// </summary>
    public string StrategyContours => Strategy is null
        ? ""
        : $"{Strategy.ClosedContours} closed rim contours, needs {Strategy.CutsNeeded} to come apart; "
            + (Strategy.Combined.Separates
                ? $"together they part it, {Strategy.Combined.LargestShare:P0} and "
                    + $"{Strategy.Combined.SecondShare:P0} of the surface either side of the rim wall"
                : "together they still do not part it");

    /// <summary>
    /// Which rims are walls and which are knife edges. Worth saying because the two want different
    /// treatment: a wall rim is bounded by two contours and the line runs between them, while a single
    /// ridge has no wall to bound and the contour is the line.
    /// </summary>
    public string StrategyRims => Strategy is null || Strategy.Rims.Count == 0
        ? ""
        : $"{Strategy.Rims.Count} rim(s): " + string.Join("; ", Strategy.Rims.Select(r =>
            $"{r.ContourIndices.Count} contour(s), {r.Kind.ToString().ToLowerInvariant()}"))
            + (Strategy.MergedRims > 0
                ? " - merged rims cannot be told apart, so a mesh per rim is not yet decidable"
                : Strategy.SingleRidgeRims > 0
                    ? " - a single ridge has no band to bound, so the contour is the line"
                    : "");

    public string StrategySummary => Strategy?.Summary ?? "";

    /// <summary>The approach actually being taken, which is <see cref="LineSource"/> whatever the body says.</summary>
    public string StrategyApproach => Strategy is null ? ""
        : Strategy.Recommended != LineSource
            ? $"{LineSource} - this body wants {Strategy.Recommended?.ToString() ?? "neither source"}"
        : Strategy.NeedsHybrid
            ? $"{LineSource}, one parting mesh per rim - this body takes {Strategy.CutsNeeded} cuts, " +
              "and the split currently builds one"
            : $"{LineSource} - matches this body";

    /// <summary>
    /// Amber when the settled approach will not do for this body - either the wrong source, or the
    /// right source applied once to a body that needs it applied per rim.
    /// </summary>
    public bool StrategyDisagrees => Strategy is not null
        && (Strategy.Recommended != LineSource || Strategy.NeedsHybrid);

    /// <summary>Chosen on the final step: how the single result mesh is written on export.</summary>
    [ObservableProperty] private PartingResultMode _resultMode = PartingResultMode.Separated;

    /// <summary>
    /// The pull direction. With the border line and the line-plane axis
    /// both settled, no geometry depends on it any more - it shades the model, and its sign decides
    /// which half comes back as Positive.
    /// </summary>
    [ObservableProperty] private float _directionX;
    [ObservableProperty] private float _directionY;
    [ObservableProperty] private float _directionZ;

    [ObservableProperty] private PartingLine? _partingLine;

    /// <summary>
    /// True once <see cref="GeneratePartingGeometryAsync"/> has committed a line. Gates the apply command, and
    /// gates the invalidation that a direction change performs.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplySplitCommand))]
    private bool _hasPartingLine;

    [ObservableProperty] private int _internalHoleCount;

    /// <summary>
    /// How many inner-rim points fall outside the body. Non-zero means the parting mesh will not
    /// sever the mould - each one is a bridge of material the cut leaves behind - so it is worth
    /// stating as a number rather than relying on the user spotting a red point in the viewport.
    /// </summary>
    [ObservableProperty] private int _breachedSealPointCount;

    /// <summary>The flange as a zero-thickness surface, cached so the cutter can be rebuilt without
    /// re-tracing the line behind it.</summary>
    private IMesh? _flangeSurface;

    /// <summary>
    /// <see cref="MeshParameters"/> with the axis settled, as of the line currently on screen. Set
    /// alongside <see cref="_flangeSurface"/> and cleared alongside it, because the two belong
    /// together: the flange was swept in the plane perpendicular to this axis, so re-extruding or
    /// cutting with any other one thickens and severs it in a direction it was never shaped for.
    /// Every stage after the line is traced reads this rather than <see cref="MeshParameters"/>.
    /// </summary>
    private PartingMeshParameters? _resolvedMeshParameters;

    /// <summary>
    /// Builds the cutter from the cached flange surface and shows the resulting solid. The halves are
    /// cut with this solid, so rebuilding it invalidates them; they are recomputed when the user steps
    /// forward to <see cref="PartingSplitState.SplitResult"/>.
    /// </summary>
    private async void UpdatePartingMesh()
    {
        if (_flangeSurface is null || _resolvedMeshParameters is null)
            return;

        // Off the UI thread, which the offset thickening made mandatory: it resolves the cutter on a
        // voxel grid and runs in hundreds of milliseconds to seconds, where the extrusion it replaced
        // was effectively instant. Still cancellable, so leaving the view mid-build abandons it rather
        // than drawing into a scene that has been released.
        _partingMeshCts?.Cancel();
        _partingMeshCts?.Dispose();
        var cts = new CancellationTokenSource();
        _partingMeshCts = cts;
        var token = cts.Token;

        // The halves are cut from this solid, so they are stale the moment it is rebuilt - dropped
        // now rather than when the rebuild lands, so the viewport never shows halves that belong to a
        // cutter that is no longer on screen.
        PositiveRegionMesh = null;
        NegativeRegionMesh = null;
        _sceneManager.SetRegions(null, null);

        // Thickness and thickening both restated rather than taken from the resolved set, so the
        // cutter the user is looking at is the one this view's recipe describes and not whatever the
        // axis resolution happened to carry through.
        var surface = _flangeSurface;
        var parameters = _resolvedMeshParameters with
        {
            Depth = CutterDepthMm,
            Thickening = Thickening,
        };

        try
        {
            var solid = await Task.Run(
                () => _partingMeshFeature.ExtrudeFlange(surface, parameters), token);

            if (token.IsCancellationRequested) return;

            if (solid.IsFailure)
            {
                _alert.ShowError($"Failed to build the parting mesh: {solid.Error.Description}");
                return;
            }

            PartingMesh = solid.Value;
            _sceneManager.UpdatePartingMesh(PartingMesh);
        }
        catch (OperationCanceledException)
        {
            // The view was left, or a newer line superseded this one.
        }
    }

    partial void OnDirectionXChanged(float value) => OnDirectionChanged();
    partial void OnDirectionYChanged(float value) => OnDirectionChanged();
    partial void OnDirectionZChanged(float value) => OnDirectionChanged();

    /// <summary>
    /// The reference angles are given in degrees off the silhouette, but the shader compares them
    /// against a normal-vs-direction dot product, so a face at angle a off the silhouette has dot sin(a).
    /// </summary>
    private float LowerDot => NeutralBand.Lower;
    private float UpperDot => NeutralBand.Upper;

    /// <summary>
    /// The draft-neutral band. Shades the model, and would bias a silhouette trace - which the settled
    /// <see cref="LineSource"/> does not perform.
    /// </summary>
    private static PartingNeutralBand NeutralBand => PartingNeutralBand.FromDegrees(LowerRefAngle, UpperRefAngle);

    private Vector3 Direction => new(DirectionX, DirectionY, DirectionZ);

    public PartingSplitViewModel(IMessenger messenger, IAlertDialog alert, IGeometryEngine engine)
    {
        _messenger = messenger;
        _alert = alert;

        _partingMeshFeature = new PartingMeshFeature(engine);
        _engine = engine;
        _splitMouldFeature = new SplitMouldFeature(engine);
        _sceneManager = new PartingSplitSceneManager(engine, new ComputePartingDirectionColors());
        _sceneManager.EditSelectionChanged += OnEditSelectionChanged;
        _sceneManager.HandleMoved += OnHandleMoved;
        _sceneManager.HandleRequested += OnHandleRequested;

        Workspace = Workspace.CreateEmpty();
    }

    public PartingSplitViewModel() : this(
        WeakReferenceMessenger.Default,
        new AlertDialog(),
        new GeometryMeshLib.GeometryEngine(new FileSystem()))
    { }

    public ISceneManager SceneManager => _sceneManager;

    /// <summary>
    /// Asks the body which source it wants, and asks the border tracer whether it has an answer.
    ///
    /// <para>
    /// The trace is run here rather than assumed available because "the body has a rim" and "the border
    /// can be traced on it" are different claims and the second is the one that decides whether the
    /// settled approach will work. It is the same call step one already makes to draw the line, so the
    /// cost is a repeat of work rather than new work - and it happens off the UI thread, once, on
    /// opening.
    /// </para>
    /// </summary>
    private PartingStrategyReport Evaluate(BodyMesh body)
    {
        var traced = _partingMeshFeature.GeneratePartingLineFromBody(body, LineParameters);

        // The wall, only so the rims can be told apart from each other: two contours a wall apart are
        // the two sides of one rim, and one with nothing that close to it is a knife edge.
        var thickness = _engine.Evaluators.MeasureWallThickness(body.Mesh, WallThicknessOptions.Default);
        float wall = float.NaN;
        if (thickness.IsSuccess)
        {
            var measured = thickness.Value.PerFace
                .Where(t => !float.IsPositiveInfinity(t) && t > 0f)
                .OrderBy(t => t)
                .ToArray();
            if (measured.Length > 0) wall = measured[measured.Length / 2];
        }

        return PartingStrategy.Evaluate(
            body.Mesh,
            seamAvailable: traced.IsSuccess,
            seamError: traced.IsFailure ? traced.Error.Description : null,
            wallThickness: wall);
    }

    public async Task ActivateAsync(Workspace workspace)
    {
        Workspace = workspace;
        CurrentState = PartingSplitState.DirectionSelection;

        var activeMeshResult = Workspace.GetActiveMesh();
        if (activeMeshResult.IsSuccess)
        {
            ActiveMesh = activeMeshResult.Value;

            // MouldMesh.Create validates the mould metadata, so its success IS the mould test.
            var mouldResult = MouldMesh.Create(ActiveMesh);
            _mould = mouldResult.IsSuccess ? mouldResult.Value : null;
            IsMould = _mould is not null;

            // Fall back to the active mesh when it isn't a mould, so the scene still shows something.
            BaseTransformMesh = ActiveMesh;
            if (_mould is not null)
            {
                var bodyResult = await Task.Run(() => _partingMeshFeature.GetBodyMesh(_mould));
                if (bodyResult.IsSuccess)
                {
                    _body = bodyResult.Value;
                    BaseTransformMesh = _body.Mesh;
                    _borderLine = null;   // belongs to whichever body it was traced on
                    ReleaseGeodesic();    // as does the path finder over it

                    Strategy = await Task.Run(() => Evaluate(_body));
                }
            }

            await Task.Yield();
            // TODO: generate mould mesh from command to ensure it matches the original before saving
            // this is because importing a mesh centres it and the mould might not be centred originally
            _sceneManager.UpdateMeshes(ActiveMesh, BaseTransformMesh);

            SeedDirection();
            _sceneManager.UpdateState(CurrentState);
        }

        ClearGeneratedSplit();

        // SeedDirection only fires OnDirectionChanged for values that actually changed, so on a
        // re-activation (already at InitialDirection) nothing would queue. Kick it here so the line
        // is always present when the view opens.
        QueueLivePartingLinePreview();
    }

    public Task<Workspace> DeactivateAsync()
    {
        _livePreviewCts?.Cancel();
        _livePreviewCts?.Dispose();
        _livePreviewCts = null;

        _partingMeshCts?.Cancel();
        _partingMeshCts?.Dispose();
        _partingMeshCts = null;

        _sceneManager.ReleaseMeshes();
        ActiveMesh = null;
        BaseTransformMesh = null;
        _mould = null;
        ReleaseGeodesic();
        ClearGeneratedSplit();
        return Task.FromResult(Workspace);
    }

    /// <summary>
    /// Puts the view's direction and the scene on <see cref="InitialDirection"/> together.
    /// Without this the view sits at its default 0,0,0 while the scene shows the initial direction, so
    /// the first change appears to do nothing (it is really the first time the two agree). The guard
    /// stops the seeding from being echoed back out as a user-driven direction change.
    /// </summary>
    private void SeedDirection()
    {
        _isUpdatingFromScene = true;
        DirectionX = InitialDirection.X;
        DirectionY = InitialDirection.Y;
        DirectionZ = InitialDirection.Z;
        _isUpdatingFromScene = false;

        _sceneManager.UpdateDirection(InitialDirection, LowerDot, UpperDot);
    }

    /// <summary>
    /// Drops everything produced by <see cref="GenerateSplitAsync"/> and the visuals built from it,
    /// returning the view to the state it has before a line has ever been committed. Anything added
    /// to the generated set belongs here as well as in the generator.
    /// </summary>
    private void ClearGeneratedSplit()
    {
        PartingLine = null;
        HasPartingLine = false;
        InternalHoleCount = 0;
        PartingMesh = null;
        PositiveRegionMesh = null;
        NegativeRegionMesh = null;
        _flangeSurface = null;
        _resolvedMeshParameters = null;
        BreachedSealPointCount = 0;
        _sceneManager.ClearPartingPreview();
    }

    private void OnDirectionChanged()
    {
        var direction = Direction;
        if (direction == Vector3.Zero) return;

        if (!_isUpdatingFromScene)
        {
            _sceneManager.UpdateDirection(Vector3.Normalize(direction), LowerDot, UpperDot);
        }

        // A new direction invalidates any previously generated parting line/preview.
        if (HasPartingLine)
        {
            ClearGeneratedSplit();
        }

        QueueLivePartingLinePreview();
    }

    /// <summary>
    /// Traces the parting line and shows it during direction selection, so the user can see where the
    /// mould will part before committing. Debounced and latest-wins: each
    /// call cancels the one before it, so a drag produces one recompute when it settles rather than one
    /// per frame, and a result that has been superseded is discarded instead of drawn.
    ///
    /// This is preview only - it deliberately does not set <see cref="PartingLine"/> or
    /// <see cref="HasPartingLine"/>. Those stay the committed state that <see cref="GeneratePartingGeometryAsync"/>
    /// produces, so a live refresh cannot make the view look like the line has been generated when it
    /// has not, and cannot trip the invalidation branch above.
    /// </summary>
    private async void QueueLivePartingLinePreview()
    {
        if (!IsMould)
            return;

        if (Direction == Vector3.Zero)
            return;

        // Snapshot before going off-thread; the direction may move again while this runs.
        var lineParameters = LineParameters;

        // The extrusion border does not depend on the pull direction, so a drag cannot change it.
        // Redrawing the line we already have is instant; recomputing it is a wall-thickness probe of
        // every face, which runs to over a second on a dense body - once per drag frame would make
        // a direction change unusable for a result identical to the one already on screen.
        if (lineParameters.Source == PartingLineSource.ExtrusionBorder && _borderLine is not null)
        {
            ShowPartingLine();
            return;
        }

        _livePreviewCts?.Cancel();
        _livePreviewCts?.Dispose();
        var cts = new CancellationTokenSource();
        _livePreviewCts = cts;
        var token = cts.Token;

        try
        {
            await Task.Delay(LivePreviewDebounceMs, token);

            var result = await Task.Run(
                () => _partingMeshFeature.GeneratePartingLineFromBody(_body, lineParameters), token);

            // A newer direction landed while this was running - its result is the one to draw.
            if (token.IsCancellationRequested)
                return;

            // Cached so a subsequent drag redraws instead of recomputing; see the note above. Only
            // the border is worth keeping - a silhouette line is specific to the direction that
            // produced it and is stale the moment the direction moves.
            _borderLine = lineParameters.Source == PartingLineSource.ExtrusionBorder && result.IsSuccess
                ? result.Value
                : null;

            // A failure here is expected for plenty of directions (no silhouette loop long enough to
            // clear the noise threshold). Clear the stale line rather than alerting - the user is
            // still scrubbing, and a dialog per bad direction would be unusable.
            if (_borderLine is not null) ShowPartingLine();
            else _sceneManager.UpdatePartingLinePreview(result.IsSuccess ? result.Value : null);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer direction; nothing to draw.
        }
    }

    /// <summary>
    /// The parting-line normals overlay is no longer drawn. It put a five-segment arrow at every one
    /// of the line's points - about 250 of them, each roughly five times longer than the gap to the
    /// next - so the arrows overlapped into a solid mat along the rim and read as a line zigzagging
    /// violently. That appearance was the overlay, never the line, and it hid the thing it existed to
    /// show. Kept as a note rather than a method so the next person to want normals here knows what
    /// happened to the last attempt.
    /// </summary>
    /// <summary>
    /// Draws the parting line, as editable sections wherever the body has a rim wall to confine them
    /// to and as a plain curve where it has not.
    ///
    /// <para>
    /// The sections are the line, not an overlay on it: drawing both would put two curves a
    /// hair apart along the whole rim, which reads as one badly aliased line rather than as two. So the
    /// plain preview is the fallback for a body that cannot be sectioned at all, and nothing else.
    /// </para>
    /// </summary>
    private void ShowPartingLine()
    {
        // Sectioned only once the line is settled - see BuildEdit. Skipped while edits are in hand, so
        // a redraw cannot silently discard them.
        if (!_hasEdits) BuildEdit();

        _sceneManager.UpdatePartingLinePreview(CanEditLine ? null : _borderLine);
    }

    /// <summary>The line the later stages are built from, which is whichever one is in force.</summary>
    /// <remarks>
    /// A hand-edited line outranks both offered ones. Once the user has moved a handle, the line on
    /// screen is theirs and every stage after this has to be built from that rather than from the
    /// automatic answer it started as - otherwise the flange is swept along a curve nobody saw.
    /// </remarks>
    private PartingLine? SelectedLine =>
        _edit is { IsEmpty: false } && _hasEdits ? _edit.ToPartingLine() : _borderLine;

    // ---------------------------------------------------------------- editing the line by hand

    private PartingLineEdit? _edit;
    private bool _hasEdits;

    /// <summary>The wall's width, which the handles are sized against. Set when the edit is built.</summary>
    private float _handleSize = 1f;

    /// <summary>Which section or handle is selected, if any.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(CanRemoveHandle))]
    [NotifyPropertyChangedFor(nameof(SelectionSummary))]
    private PartingLinePick? _selection;

    /// <summary>
    /// The scene follows the selection rather than being told separately at each site that changes it.
    ///
    /// <para>
    /// Every path that dropped the selection - resetting the edits, removing the selected handle - set
    /// this and left the scene highlighting a control that was no longer selected, and on a reset the
    /// highlight then belonged to a section that no longer existed. Answering it here covers those and
    /// anything added later. The echo when the scene is what raised the change costs a material swap.
    /// </para>
    /// </summary>
    partial void OnSelectionChanged(PartingLinePick? value) =>
        _sceneManager.SetEditSelection(value, _handleSize);


    /// <summary>
    /// Whether a re-walked span takes the shortest path across the whole body rather than across the
    /// rim wall - see <see cref="PartingLineEditor.Move"/>.
    ///
    /// <para>
    /// Offered as a switch because which one is right turns out to depend on the body, and by more than
    /// a little. Measured across the set, the unconstrained path is 2 to 12 percent shorter and turns
    /// roughly a third less at every sample, which is exactly what it promises. What it costs is the
    /// wall: on <c>standard</c>, <c>scalp</c> and the larynx's second rim it strays under 1.7mm from a
    /// wall 5.7 to 11.5mm wide, which is to say it stays on the rim and simply takes a better line
    /// across it. On <c>nose</c> and <c>eye</c> it strays 7.5 and 8.6mm from walls 5.3mm wide - clean
    /// off the rim and onto a shell, for a line 9 and 12 percent shorter. That is not a smoother parting
    /// line, it is a parting line somewhere else.
    /// </para>
    ///
    /// <para>
    /// Off by default for that reason, and the band walk keeps the guarantee the editing model was built
    /// on. Nothing here decides between them automatically: what makes a body one case or the other is
    /// how tightly its rim turns against how wide its wall is, and a number that separates the two would
    /// need measuring on many more bodies than are in the set.
    /// </para>
    /// </summary>
    [ObservableProperty] private bool _useUnconstrainedGeodesic;

    partial void OnUseUnconstrainedGeodesicChanged(bool value)
    {
        if (_edit is null || _edit.IsEmpty) return;

        // Re-walked wholesale rather than left to take effect on the next drag, which would leave the
        // line on screen half walked one way and half the other - and so show neither.
        var geodesic = Geodesic;
        var edit = _edit;
        for (int rim = 0; rim < edit.Rims.Count; rim++)
            edit = edit.With(
                rim, PartingLineEditor.Retrace(edit.Rims[rim].Line, edit.Rims[rim].Graph, geodesic));

        _edit = edit;
        _hasEdits = true;

        _sceneManager.SetPartingLineEdit(_edit, _handleSize);
        OnPropertyChanged(nameof(SelectionSummary));
    }

    private ISurfaceGeodesic? _geodesic;
    private bool _geodesicRefused;

    /// <summary>
    /// The unconstrained path finder, or null when the band walk is in force. Built on first use rather
    /// than with the edit: it copies the body into the engine's own mesh and holds a spatial index over
    /// it, which is a real cost to impose on the users who never turn this on.
    /// </summary>
    private ISurfaceGeodesic? Geodesic
    {
        get
        {
            if (!UseUnconstrainedGeodesic || _body is null || _geodesicRefused) return null;
            if (_geodesic is not null) return _geodesic;

            var made = _engine.PartingTools.CreateSurfaceGeodesic(_body.Mesh);
            if (made.IsFailure)
            {
                // Latched, because this is read on every frame of a drag and a dialog per frame is
                // worse than the thing it is reporting.
                _geodesicRefused = true;
                _alert.ShowError($"Could not build the surface path finder: {made.Error.Description}");
                return null;
            }

            return _geodesic = made.Value;
        }
    }

    /// <summary>Drops the path finder, which belongs to whichever body it was built on.</summary>
    private void ReleaseGeodesic()
    {
        _geodesic?.Dispose();
        _geodesic = null;
        _geodesicRefused = false;
    }

    [ObservableProperty] private bool _canEditLine;

    public bool HasSelection => Selection is not null;

    /// <summary>
    /// A handle may only be removed while more than two are left: two is the fewest that can describe
    /// a closed line, and below that there is nothing left to walk between.
    /// </summary>
    public bool CanRemoveHandle =>
        Selection is { IsHandle: true } pick
        && _edit is not null
        && pick.Rim < _edit.Rims.Count
        && _edit.Rims[pick.Rim].Line.Anchors.Count > 2;

    public string SelectionSummary
    {
        get
        {
            if (_edit is null || Selection is not { } pick) return "Nothing selected";
            if (pick.Rim >= _edit.Rims.Count) return "Nothing selected";

            var line = _edit.Rims[pick.Rim].Line;
            if (pick.IsHandle)
                return pick.Index < line.Anchors.Count
                    ? $"Handle {pick.Index + 1} of {line.Anchors.Count}"
                    : "Nothing selected";

            if (pick.Index >= line.Spans.Count) return "Nothing selected";

            var span = line.Spans[pick.Index];
            return $"Section {pick.Index + 1} of {line.Spans.Count} - " +
                   $"{span.Condition}, {span.Length:F1} mm";
        }
    }


    /// <summary>
    /// Cuts the line on screen into editable sections. Deliberately not done as part of tracing: there
    /// is a choice of two lines to settle first, and sectioning the one not settled on would put
    /// handles on a curve about to be replaced.
    /// </summary>
    private void BuildEdit()
    {
        _edit = null;
        _hasEdits = false;
        Selection = null;
        CanEditLine = false;

        var line = _borderLine;

        if (_body is null || line is null)
        {
            _sceneManager.SetPartingLineEdit(null, _handleSize);
            return;
        }

        var built = _partingMeshFeature.BeginPartingLineEdit(_body, line);
        if (built.IsFailure)
        {
            // Not an error worth a dialog: a body with no wall rim is one this cannot offer editing
            // for, and the automatic line it already has is still perfectly usable.
            _sceneManager.SetPartingLineEdit(null, _handleSize);
            return;
        }

        _edit = built.Value;
        _handleSize = _edit.Rims.Count > 0 ? _edit.Rims[0].Graph.Band.Span : 1f;
        CanEditLine = true;

        _sceneManager.SetPartingLineEdit(_edit, _handleSize);
    }

    private void OnEditSelectionChanged(PartingLinePick? pick) => Selection = pick;

    /// <summary>
    /// Carries a handle to where the cursor is. Every drag frame re-walks the two spans that meet at
    /// the handle, which is cheap enough to do live: a span is a walk across a few hundred band faces,
    /// not a pass over the body.
    /// </summary>
    private void OnHandleMoved(PartingLinePick pick, Vector3 to)
    {
        if (_edit is null || pick.Rim >= _edit.Rims.Count) return;

        var rim = _edit.Rims[pick.Rim];
        _edit = _edit.With(
            pick.Rim, PartingLineEditor.Move(rim.Line, pick.Index, to, rim.Graph, Geodesic));
        _hasEdits = true;

        _sceneManager.SetPartingLineEdit(_edit, _handleSize);
        OnPropertyChanged(nameof(SelectionSummary));
    }

    /// <summary>
    /// Divides a section in two at the spot the user clicked, putting a handle there.
    ///
    /// <para>
    /// The spot is decided by the scene, not here, and that is on purpose: it is the same placement the
    /// marker under the cursor was drawn from, so the handle appears exactly where the user was shown it
    /// would. Working it out again here would be a second answer to the same question, and the two
    /// would disagree the moment either changed.
    /// </para>
    /// </summary>
    private void OnHandleRequested(PartingInsertion insertion)
    {
        if (_edit is null || insertion.Rim < 0 || insertion.Rim >= _edit.Rims.Count) return;

        var (line, anchor) = PartingLineEditor.Insert(_edit.Rims[insertion.Rim].Line, insertion);

        // A placement that no longer names a section divides nothing. It reads to the user as a click
        // that did not take, which is right - the marker it was aimed at is gone too.
        if (anchor < 0) return;

        _edit = _edit.With(insertion.Rim, line);
        _hasEdits = true;

        _sceneManager.SetPartingLineEdit(_edit, _handleSize);

        // Selected, so the handle just made is the one "Remove selected handle" acts on - which makes
        // an unwanted division one click to undo.
        Selection = new PartingLinePick(insertion.Rim, anchor, IsHandle: true);
        OnPropertyChanged(nameof(SelectionSummary));
    }

    /// <summary>Drops the selected handle and re-walks the merged stretch across the wall.</summary>
    [RelayCommand]
    private void RemoveHandle()
    {
        if (_edit is null || Selection is not { IsHandle: true } pick) return;
        if (pick.Rim >= _edit.Rims.Count) return;

        var rim = _edit.Rims[pick.Rim];
        _edit = _edit.With(
            pick.Rim, PartingLineEditor.Remove(rim.Line, pick.Index, rim.Graph, Geodesic));
        _hasEdits = true;

        Selection = null;
        _sceneManager.SetPartingLineEdit(_edit, _handleSize);
    }

    /// <summary>
    /// Eases the whole line along its own length, holding every point on the rim wall.
    ///
    /// <para>
    /// A command rather than something applied after every drag. Dragging a handle re-walks the two
    /// spans that meet at it as geodesics, which is already the straightest those spans can be while
    /// still passing through the handles - so smoothing after each drag would have nothing to do but
    /// work on the stretches the user had not asked about. As a button it is repeatable, and each press
    /// moves the line further toward the piecewise geodesic through the handles rather than further
    /// toward nothing in particular.
    /// </para>
    /// </summary>
    [RelayCommand]
    private void SmoothLine()
    {
        if (_edit is null || _edit.IsEmpty) return;

        var edit = _edit;
        for (int rim = 0; rim < edit.Rims.Count; rim++)
            edit = edit.With(rim, PartingLineEditor.Smooth(edit.Rims[rim].Line, edit.Rims[rim].Graph));

        _edit = edit;

        // Counts as an edit, so a redraw cannot silently throw it away and every stage below is built
        // from the smoothed line rather than from the trace it started as - see SelectedLine.
        _hasEdits = true;

        _sceneManager.SetPartingLineEdit(_edit, _handleSize);
        OnPropertyChanged(nameof(SelectionSummary));
    }

    /// <summary>Throws away every edit and goes back to the line as it was computed.</summary>
    [RelayCommand]
    private void ResetLineEdits() => BuildEdit();

    private static float DiagonalOf(IMesh mesh)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in mesh.Vertices)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }
        return (max - min).Length();
    }

    [RelayCommand]
    public async Task NextStateAsync()
    {
        // Each stage's data is built just before the stage that shows it, so a failure leaves the
        // user where they are rather than stepping them into a blank stage.
        var generated = CurrentState switch
        {
            PartingSplitState.DirectionSelection => await GeneratePartingGeometryAsync(),
            PartingSplitState.PartingMeshPreview => await GenerateSplitAsync(),
            _ => true
        };

        if (!generated)
            return;

        if (CurrentState < PartingSplitState.SplitResult)
        {
            CurrentState++;
            _sceneManager.UpdateState(CurrentState);
        }
    }

    /// <summary>
    /// Commits the parting line for the current direction and builds the parting mesh from it -
    /// what stage two displays. Returns false (having alerted) if
    /// any step fails, leaving the generated set cleared so no stage shows a half-built result.
    /// </summary>
    private async Task<bool> GeneratePartingGeometryAsync()
    {
        if (_mould is null || BaseTransformMesh is null) return false;
        if (!IsMould)
        {
            _alert.ShowError(MouldOnlyError);
            return false;
        }

        var lineParameters = LineParameters;

        _messenger.Send(new IsLoadingMessage(true));
        try
        {
            var lineResult = await Task.Run(() => _partingMeshFeature.GeneratePartingLineFromBody(_body, lineParameters));
            if (lineResult.IsFailure)
            {
                _alert.ShowError(lineResult.Error.Description);
                return false;
            }

            // Whichever line the user picked in step one. The traced result is recomputed above rather
            // than cached because a silhouette line does depend on the direction; for the border line
            // the two are the same object and the pick is what decides.
            var partingLine = SelectedLine ?? lineResult.Value;

            // Settled once, here, and used by every stage below. The line-aligned mesh derives its
            // axis from this line, so resolving per stage would let a later one recompute against a
            // different line and build the flange, the extrusion and the cut on axes that disagree.
            var resolved = PartingMeshFeature.ResolveAxis(partingLine, MeshParameters);
            if (resolved.IsFailure)
            {
                _alert.ShowError(resolved.Error.Description);
                return false;
            }

            var meshParameters = resolved.Value;

            var outerContour = _partingMeshFeature.GenerateOuterContour(_mould, meshParameters);
            if (outerContour.IsFailure)
            {
                _alert.ShowError($"Failed to generate the outer contour: {outerContour.Error.Description}");
                return false;
            }

            // bodyMesh is what the flange seals against. Omitting it leaves the inner rim placed by
            // footprint arithmetic alone, which leaves a few vertices fractionally outside the body -
            // each one a hairline bridge that survives the cut and keeps the mould in one piece. That
            // is what made scalp.3mf fail to separate at any cutter thickness.
            var flangeResult = await Task.Run(() => _partingMeshFeature.GenerateFlangeSurface(
                partingLine, outerContour.Value, meshParameters, _body));
            if (flangeResult.IsFailure)
            {
                _alert.ShowError($"Failed to generate the parting mesh: {flangeResult.Error.Description}");
                return false;
            }

            PartingLine = partingLine;
            InternalHoleCount = partingLine.InternalHoleCount;
            HasPartingLine = true;

            _sceneManager.SetPartingLine(partingLine);

            _flangeSurface = flangeResult.Value;
            _resolvedMeshParameters = meshParameters;

            // Whether this flange can actually sever the mould comes down to its inner rim sitting
            // inside the body. Showing that with the parting mesh means a breach is visible now,
            // rather than surfacing later as a boolean that declines to separate anything.
            var seal = _partingMeshFeature.InspectFlangeSeal(
                _flangeSurface, _body, partingLine, meshParameters);
            _sceneManager.SetFlangeSeal(seal.IsSuccess ? seal.Value : null);
            BreachedSealPointCount = seal.IsSuccess ? seal.Value.Count(p => !p.IsSealed) : 0;

            UpdatePartingMesh();
            return true;
        }
        finally
        {
            _messenger.Send(new IsLoadingMessage(false));
        }
    }

    /// <summary>
    /// Cuts the mould with the parting mesh as currently built, producing both halves. Skipped when
    /// they are already up to date - stepping back and forth over stage two should not re-run a
    /// boolean for nothing.
    /// </summary>
    private async Task<bool> GenerateSplitAsync()
    {
        if (_mould is null || PartingLine is null || PartingMesh is null) return false;
        if (_resolvedMeshParameters is null) return false;
        if (PositiveRegionMesh is not null) return true;

        var mould = _mould;
        var partingMesh = PartingMesh;
        var partingLine = PartingLine;
        var meshParameters = _resolvedMeshParameters;

        _messenger.Send(new IsLoadingMessage(true));
        try
        {
            // Subtract-only shows the raw cut instead of two halves, so it takes its own route: there
            // is nothing to separate and nothing to name.
            if (meshParameters.SplitMethod == PartingSplitMethod.SubtractOnly)
            {
                var cut = await Task.Run(() => _partingMeshFeature.CutMouldWith(mould, partingMesh));
                if (cut.IsFailure)
                {
                    _alert.ShowError($"Failed to cut the mould: {cut.Error.Description}");
                    return false;
                }

                PositiveRegionMesh = cut.Value;
                NegativeRegionMesh = null;
                _sceneManager.SetRegions(PositiveRegionMesh, null);
                return true;
            }

            // The half-space split builds its own tool from the flange surface, so it is handed that
            // rather than the extruded slab - the slab is only what stage two put on screen.
            var surface = _flangeSurface;
            var splitResult = await Task.Run(() =>
                meshParameters.SplitMethod == PartingSplitMethod.ShiftedHalfSpaces && surface is not null
                    ? _partingMeshFeature.SplitMouldByHalfSpaces(mould, surface, meshParameters)
                    : _partingMeshFeature.SplitMould(mould, partingMesh, partingLine, meshParameters.Axis));
            if (splitResult.IsFailure)
            {
                _alert.ShowError($"Failed to split the mould: {splitResult.Error.Description}");
                return false;
            }

            (PositiveRegionMesh, NegativeRegionMesh) = splitResult.Value;
            _sceneManager.SetRegions(PositiveRegionMesh, NegativeRegionMesh);
            return true;
        }
        finally
        {
            _messenger.Send(new IsLoadingMessage(false));
        }
    }

    [RelayCommand]
    public void PreviousState()
    {
        if (CurrentState > PartingSplitState.DirectionSelection)
        {
            CurrentState--;
            _sceneManager.UpdateState(CurrentState);
        }
    }

    [RelayCommand(CanExecute = nameof(HasPartingLine))]
    public async Task ApplySplitAsync()
    {
        if (ActiveMesh is null || PartingLine is null) return;
        if (!IsMould)
        {
            _alert.ShowError(MouldOnlyError);
            return;
        }

        var meshId = ActiveMesh.Metadata.Id;
        var lineParameters = LineParameters;
        var meshParameters = MeshParameters;
        var resultMode = ResultMode;

        _messenger.Send(new IsLoadingMessage(true));
        try
        {
            var result = await Task.Run(() =>
                _splitMouldFeature.ExecuteCut(Workspace, meshId, lineParameters, meshParameters, resultMode));
            if (result.IsFailure)
            {
                _alert.ShowError(result.Error.Description);
                return;
            }

            Workspace = result.Value;
            _sceneManager.ReleaseMeshes();

            // The result is in the workspace now, so the mesh manager is where it can be acted on.
            _messenger.Send(new SwitchToMeshManagerMessage());
        }
        finally
        {
            _messenger.Send(new IsLoadingMessage(false));
        }
    }

    [RelayCommand]
    public void ResetDirection()
    {
        CurrentState = PartingSplitState.DirectionSelection;
        SeedDirection();
        ClearGeneratedSplit();
        _sceneManager.UpdateState(CurrentState);
        QueueLivePartingLinePreview();
    }
}
