using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common.Mesh;
using Fabolus.Wpf.Features.Viewport;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
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
    // The parting mesh is drawn green on its front faces only; back faces are culled, so looking at
    // the flange from underneath shows nothing rather than a second colour.
    // The scene has no lights, so unlit DiffuseMaterials are used (a PhongMaterial would render black).
    private readonly Material _partingMeshSkin = DiffuseMaterials.Green;

    private readonly Material _heatMapSkin = new VertColorMaterial();

    // See-through skin for the mould, so the parting mesh inside it stays readable. Paired with
    // IsTransparent on the model, which is what puts it in the blended render pass.
    private readonly Material _mouldGhostSkin = new DiffuseMaterial
    {
        DiffuseColor = new Color4(0.75f, 0.75f, 0.78f, 0.25f)
    };

    private readonly Element3D _grid;
    private MeshGeometryModel3D? _baseMeshModel;
    private MeshGeometryModel3D? _mouldMeshModel;
    private MeshGeometryModel3D? _positiveRegionModel;
    private MeshGeometryModel3D? _negativeRegionModel;
    private MeshGeometryModel3D? _partingMeshModel;
    private LineGeometryModel3D? _partingLineModel;

    // The body's own rim, shown both ways: the facets it covers, and the curve that outlines them.
    // The curve is what sits where the rim actually is - a rim is often narrower than a triangle, so
    // shading alone can only ever produce a staircase of whole faces. The shaded region is what makes
    // the rim readable as a band with width rather than as a line floating over unshaded surface, and
    // it is the one part of the body where the draft colouring means nothing anyway: a rim's facets
    // face every which way, so red, green and grey scatter across it at random.
    //
    // One model per rim rather than one for the whole ridge. A body with a hole through it has two of
    // them and parts differently because of it, and drawing both in one colour renders a torus exactly
    // as it renders a shell - which is the one thing about it a reader most needs to see is not true.
    private readonly List<LineGeometryModel3D> _ridgeContourModels = new();

    // Per triangle. Cached because the rim is a property of the shape, not of the pull direction, so
    // it survives every change of direction.
    private bool[]? _ridgeFaces;

    // Which rim each face is on, parallel to _ridgeFaces, so the shading can separate them too.
    private int[]? _faceRims;

    // What each rim turned out to be, keyed by rim id, so the region and the curve over it are coloured
    // from one classification rather than two.
    private readonly Dictionary<int, PartingRimKind> _rimKinds = new();

    // Rim colours, muted on purpose and deliberately not the colours of the contours drawn on top of
    // them. A saturated fill swallows the curve at line width, which is how a region and its own
    // outline stop reading as two things.
    private static readonly Color4[] _ridgeRegionColours =
    {
        new(0.37f, 0.43f, 0.67f, 1.0f),   // rim 0 - the blue the single-rim case has always been
        new(0.33f, 0.60f, 0.51f, 1.0f),   // rim 1 - green, for the second rim of a body with a hole
        new(0.55f, 0.45f, 0.62f, 1.0f),   // rim 2
    };

    // A rim whose contours could not be told apart, because two rims' walls touch and share a band
    // group. Amber rather than another rim colour: the shading is honest about the faces it covers, but
    // what it cannot say is which rim they are on, and that is worth looking different.
    private static readonly Color4 _mergedRimColour = new(0.75f, 0.55f, 0.28f, 1.0f);

    // Faces on the ridge that belong to no band group: the facets a crease touches where the crease
    // bounds no wall. Given their own muted grey rather than the first rim's colour, which would have
    // them claiming to be part of a rim they were never attributed to - and on a body with two rims
    // that claim is a wrong one, not merely an unproven one.
    private static readonly Color4 _unattributedRimColour = new(0.46f, 0.48f, 0.53f, 1.0f);

    private static readonly System.Windows.Media.Color[] _ridgeContourColours =
    {
        System.Windows.Media.Color.FromRgb(198, 76, 255),
        System.Windows.Media.Color.FromRgb(96, 220, 170),
        System.Windows.Media.Color.FromRgb(180, 140, 255),
    };

    private static readonly System.Windows.Media.Color _mergedContourColour =
        System.Windows.Media.Color.FromRgb(240, 170, 70);

    // The flange's inner rim, split into the points that seal and the points that don't. Two models
    // rather than one with per-point colours: PointGeometryModel3D carries a single Color.
    private PointGeometryModel3D? _sealedRimModel;
    private PointGeometryModel3D? _breachedRimModel;
    private PartingSplitState _currentState;

    private IMesh? _activeMouldMesh;
    private IMesh? _baseTransformMesh;
    private CoreVector3 _direction = CoreVector3.UnitY;
    private float _lowerDot = -0.01f;
    private float _upperDot = 0.01f;

    public event Action<Element3D>? VisualAddedOrUpdated;
    public event Action<Guid>? VisualRemovedById;
    public event Action? VisualsCleared;

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

        if (_baseMeshModel != null)
            VisualRemovedById?.Invoke(_baseMeshModel.GUID);
        if (_mouldMeshModel != null)
            VisualRemovedById?.Invoke(_mouldMeshModel.GUID);
        foreach (var model in _ridgeContourModels) VisualRemovedById?.Invoke(model.GUID);
        _ridgeContourModels.Clear();

        ClearEditVisuals();

        // Flat-shaded (un-welded) so the direction classification can colour each triangle on its own
        // face normal. RecomputeDirectionColors below then only has to rewrite the colour channel,
        // since triangle t owns vertices 3t..3t+2 and that mapping never changes.
        var baseGeo = _baseTransformMesh.ToFlatShadedHelixMesh(_engine);
        if (baseGeo.IsSuccess)
        {
            _baseMeshModel = new MeshGeometryModel3D { Geometry = baseGeo.Value, Material = _meshSkin };
        }

        var mouldGeo = _activeMouldMesh.ToHelixMesh(_engine);
        if (mouldGeo.IsSuccess)
        {
            _mouldMeshModel = new MeshGeometryModel3D { Geometry = mouldGeo.Value, Material = _meshSkin };
        }

        // Found once here rather than in RecomputeDirectionColors: the rim is a property of the shape,
        // so it does not change as the pull direction is dragged, and re-deriving it every frame would
        // be the most expensive thing on that path by far. One call for both forms, so the shaded
        // region and the curve over it can never describe different ridges.
        var ridge = RidgeDetection.FindRidge(_baseTransformMesh, RidgeDetectionOptions.Default);

        // The band, not RidgeSurfaces.Faces. Faces is the band padded by one face either side of every
        // crease, which measures 1.3 to 1.7 times the distance between the creases and pads the two
        // sides unequally, because the faces on the inner and outer surfaces are not the same size. It
        // was shaded here before, and the effect was to make a parting line running correctly down the
        // middle of the wall look as though it hugged one edge - on the standard bolus the shading ran
        // 17.2mm against an 11.5mm wall. What is drawn as the rim has to be the rim.
        _ridgeFaces = ridge.Band.Length == ridge.Faces.Length ? ridge.Band : ridge.Faces;
        _faceRims = ridge.FaceRims.Length == ridge.Faces.Length ? ridge.FaceRims : null;

        // Classified by the same grouping the strategy report uses, so the picture and the report cannot
        // say different things about the same body. No wall thickness here - measuring one costs a ray
        // cast the scene has no other use for, and it is only needed to tell a wall from a knife edge.
        // The cases that change the drawing, one contour and more than two, need no thickness at all.
        _rimKinds.Clear();
        foreach (var rim in PartingStrategy.Rims(ridge.Contours.Where(c => c.IsClosed).ToList()))
            _rimKinds[rim.Id] = rim.Kind;

        BuildRidgeContourModels(ridge.Contours);

        RecomputeDirectionColors();
    }

    /// <summary>
    /// One line model per rim, because a model carries a single colour and the rims have to be told
    /// apart. Contours the trace could not attribute to a rim are drawn together in the first colour -
    /// they are still the ridge, and dropping them would hide real geometry to keep a legend tidy.
    /// </summary>
    private void BuildRidgeContourModels(IReadOnlyList<RidgeContour> contours)
    {
        foreach (var byRim in contours.GroupBy(c => c.Rim).OrderBy(g => g.Key))
        {
            var lineBuilder = new LineBuilder();
            bool any = false;

            foreach (var contour in byRim)
            {
                var points = contour.Points;
                if (points.Count < 2) continue;

                // Open contours stop where they stop - joining the last point back to the first would
                // draw a chord straight across the model.
                int segments = contour.IsClosed ? points.Count : points.Count - 1;
                for (int i = 0; i < segments; i++)
                {
                    var a = points[i];
                    var b = points[(i + 1) % points.Count];
                    lineBuilder.AddLine(new Vector3(a.X, a.Y, a.Z), new Vector3(b.X, b.Y, b.Z));
                    any = true;
                }
            }

            if (!any) continue;

            _ridgeContourModels.Add(new LineGeometryModel3D
            {
                Geometry = lineBuilder.ToLineGeometry3D(),
                Color = ContourColour(byRim.Key),
                Thickness = 2.0,
                IsHitTestVisible = false
            });
        }
    }

    private System.Windows.Media.Color ContourColour(int rim) =>
        _rimKinds.TryGetValue(rim, out var kind) && kind == PartingRimKind.Merged
            ? _mergedContourColour
            : _ridgeContourColours[RimSlot(rim) % _ridgeContourColours.Length];

    private Color4 RegionColour(int rim) =>
        rim < 0 ? _unattributedRimColour
        : _rimKinds.TryGetValue(rim, out var kind) && kind == PartingRimKind.Merged
            ? _mergedRimColour
            : _ridgeRegionColours[RimSlot(rim) % _ridgeRegionColours.Length];

    /// <summary>
    /// A rim's place in the colour order. Rim ids are region indices, so they are arbitrary and far
    /// apart - taking them modulo the palette would colour two rims the same as readily as not. Their
    /// rank does not.
    /// </summary>
    private int RimSlot(int rim)
    {
        if (rim < 0) return 0;

        int slot = 0;
        foreach (int id in _rimKinds.Keys.Where(k => k >= 0).OrderBy(k => k))
        {
            if (id == rim) return slot;
            slot++;
        }
        return 0;
    }

    /// <summary>
    /// Drops the editing controls and everything that pointed into them. The picks are indices into an
    /// edit, so a selection or a live drag outliving the edit it indexes is how a later click ends up
    /// moving a handle on a line that is no longer on screen.
    /// </summary>
    private void ClearEditVisuals()
    {
        foreach (var id in _editVisuals.Ids) VisualRemovedById?.Invoke(id);
        _editVisuals.Clear();
        _editVisuals.IsHitTestable = true;

        _edit = null;
        _selected = null;
        _hovered = null;
        _dragging = null;
        _lastDragPoint = null;
        _previewAt = null;
        _planned = null;
    }

    public void ReleaseMeshes()
    {
        _activeMouldMesh = null;
        _baseTransformMesh = null;
        _ridgeFaces = null;

        ClearEditVisuals();

        if (_baseMeshModel != null)
            VisualRemovedById?.Invoke(_baseMeshModel.GUID);
        if (_mouldMeshModel != null)
            VisualRemovedById?.Invoke(_mouldMeshModel.GUID);
        foreach (var model in _ridgeContourModels) VisualRemovedById?.Invoke(model.GUID);

        ClearPartingPreview();

        _baseMeshModel = null;
        _mouldMeshModel = null;
        _ridgeContourModels.Clear();
        _rimKinds.Clear();
        _faceRims = null;
    }

    public void UpdateDirection(CoreVector3 direction, float lowerDot, float upperDot)
    {
        _lowerDot = lowerDot;
        _upperDot = upperDot;

        if (direction == CoreVector3.Zero)
            return;
        _direction = CoreVector3.Normalize(direction);

        if (_currentState == PartingSplitState.DirectionSelection)
        {
            RecomputeDirectionColors();
        }
    }

    private void RecomputeDirectionColors()
    {
        if (_baseTransformMesh is null || _baseMeshModel?.Geometry is null)
            return;

        // _lowerDot/_upperDot are already normal-dot-pull values, i.e. the sine of the angle off the
        // silhouette - the caller converted from degrees. They were being multiplied by a
        // degrees-to-radians factor again here, which shrank the shaded band by ~57x and is why it
        // read as a hairline rather than the +/-5 degrees the slider asks for.
        var parameters = new PartingLineParameters
        {
            PullDirection = _direction,
            NeutralBand = new PartingNeutralBand(_lowerDot, _upperDot),
        };

        var colorsResult = _colorsFeature.Execute(_baseTransformMesh, parameters);
        if (colorsResult.IsSuccess && _baseMeshModel.Geometry is MeshGeometry3D geo)
        {
            // One colour per triangle, written to all three of that triangle's corners - the geometry
            // is un-welded, so this gives a hard edge at every face boundary rather than a gradient.
            var colors = colorsResult.Value;
            int triangleCount = colors.Length / 3;
            var colorCollection = new Color4Collection(triangleCount * 3);

            bool[]? ridge = _ridgeFaces?.Length == triangleCount ? _ridgeFaces : null;

            for (int t = 0; t < triangleCount; t++)
            {
                // The rim replaces the draft colour rather than blending with it. On a rim the draft
                // classification carries no information - the facets face every which way, so it comes
                // out as scattered red and green - and leaving that showing through would only make
                // the one part of the body with a definite answer look like the noisiest.
                var colour = ridge is not null && ridge[t]
                    ? RegionColour(_faceRims is not null ? _faceRims[t] : -1)
                    : new Color4(
                        (float)colors[t * 3], (float)colors[(t * 3) + 1], (float)colors[(t * 3) + 2], 1.0f);

                colorCollection.Add(colour);
                colorCollection.Add(colour);
                colorCollection.Add(colour);
            }

            geo.Colors = colorCollection;
        }
    }

    public void ClearPartingPreview()
    {
        if (_partingLineModel != null)
            VisualRemovedById?.Invoke(_partingLineModel.GUID);
        if (_sealedRimModel != null)
            VisualRemovedById?.Invoke(_sealedRimModel.GUID);
        if (_breachedRimModel != null)
            VisualRemovedById?.Invoke(_breachedRimModel.GUID);
        if (_partingMeshModel != null)
            VisualRemovedById?.Invoke(_partingMeshModel.GUID);
        if (_positiveRegionModel != null)
            VisualRemovedById?.Invoke(_positiveRegionModel.GUID);
        if (_negativeRegionModel != null)
            VisualRemovedById?.Invoke(_negativeRegionModel.GUID);

        _partingLineModel = null;
        _sealedRimModel = null;
        _breachedRimModel = null;
        _partingMeshModel = null;
        _positiveRegionModel = null;
        _negativeRegionModel = null;
    }

    /// <summary>
    /// Replaces just the parting-line visual, leaving the tool/region/contour previews alone. This is
    /// the live path used while the user is still choosing a pull direction, where
    /// <see cref="SetPreviewData"/> would be wrong twice over: it clears every other preview, and it
    /// is meant for a committed line rather than one that is about to be superseded.
    /// </summary>
    public void UpdatePartingLinePreview(PartingLine? partingLine)
    {
        if (_partingLineModel is not null)
            VisualRemovedById?.Invoke(_partingLineModel.GUID);

        _partingLineModel = BuildPartingLineModel(partingLine);
        if (_partingLineModel is null)
            return;

        VisualAddedOrUpdated?.Invoke(_partingLineModel);

        // UpdateState is not re-run on a live refresh, so apply this state's visibility here.
        SetVisibility(_partingLineModel, _currentState is PartingSplitState.DirectionSelection);
    }

    /// <summary>
    /// Shows the flange's inner rim: blue where it sits inside the body and seals, red where it does
    /// not and will bridge the cut. A single red point is enough to leave the mould in one piece, so
    /// this is shown alongside the parting mesh rather than left for the boolean to discover.
    /// </summary>
    public void SetFlangeSeal(IReadOnlyList<FlangeSealPoint>? sealPoints)
    {
        if (_sealedRimModel is not null)
            VisualRemovedById?.Invoke(_sealedRimModel.GUID);
        if (_breachedRimModel is not null)
            VisualRemovedById?.Invoke(_breachedRimModel.GUID);

        _sealedRimModel = null;
        _breachedRimModel = null;

        if (sealPoints is null || sealPoints.Count == 0)
            return;

        _sealedRimModel = BuildRimPointModel(
            sealPoints.Where(p => p.IsSealed), Colors.DeepSkyBlue, size: 4.0);

        // Deliberately larger: a breach is usually a handful of points among hundreds, and at the
        // same size as the sealing ones it is easy to miss entirely.
        _breachedRimModel = BuildRimPointModel(
            sealPoints.Where(p => !p.IsSealed), Colors.Red, size: 9.0);

        if (_sealedRimModel is not null)
            VisualAddedOrUpdated?.Invoke(_sealedRimModel);
        if (_breachedRimModel is not null)
            VisualAddedOrUpdated?.Invoke(_breachedRimModel);

        bool visible = _currentState is PartingSplitState.PartingMeshPreview;
        SetVisibility(_sealedRimModel, visible);
        SetVisibility(_breachedRimModel, visible);
    }

    /// <summary>
    /// The parting-line normals overlay used to live here and has been removed. It drew a five-segment
    /// arrow at every point of the line - about 250 of them, each roughly five times longer than the
    /// gap to the next - so the arrows overlapped into a solid magenta mat along the rim, which read as
    /// a line zigzagging violently. That appearance was entirely the overlay's: it looked the same
    /// whatever shape the line underneath was, and it hid the one thing it existed to show. Anything
    /// replacing it has to space the arrows by arc length against their own drawn length.
    /// </summary>

    // ---------------------------------------------------------------- editing the line by hand

    private readonly PartingLineEditVisuals _editVisuals = new();
    private PartingLineEdit? _edit;
    private PartingLinePick? _selected;

    /// <summary>The handle the cursor is over, which is drawn slightly larger to say the click lands there.</summary>
    private PartingLinePick? _hovered;

    /// <summary>The handle being dragged. While one is set, the controls are out of the hit test entirely.</summary>
    private PartingLinePick? _dragging;

    /// <summary>Where the last drag frame put the handle, so a still cursor asks for no work.</summary>
    private CoreVector3? _lastDragPoint;

    /// <summary>
    /// How far, in mm, the cursor has to move on the body before the handle is asked to follow.
    ///
    /// <para>
    /// Small enough to be invisible - a tenth of the thinnest wall in the set is around 0.1mm - and it
    /// exists only to drop the repeats. WPF reports a mouse-move whenever the pointer is polled, not
    /// only when it has moved, and every one of those costs two Dijkstra walks across the band in the
    /// editor upstream.
    /// </para>
    /// </summary>
    private const float DragDeadZoneMm = 0.02f;

    /// <summary>A section or a handle was clicked. Null when the click cleared the selection.</summary>
    public event Action<PartingLinePick?>? EditSelectionChanged;

    /// <summary>A handle was dragged to a new place on the body.</summary>
    public event Action<PartingLinePick, CoreVector3>? HandleMoved;

    /// <summary>
    /// A section was clicked, and this is where on it the new handle goes.
    ///
    /// <para>
    /// Every click on a section, rather than only those made in an adding mode. The mode is gone: it was
    /// a button that had to be found and turned on before a click on the line would divide it, and what
    /// replaces it is the marker under the cursor, which says the same thing without having to be
    /// discovered first. Selecting a section is what the click used to do and nothing depended on it -
    /// a section's condition and length are still reported for whichever one is selected, and dividing
    /// one selects the handle it produced.
    /// </para>
    /// </summary>
    public event Action<PartingInsertion>? HandleRequested;

    private float _handleSize = 1f;

    /// <summary>
    /// How many sections each rim had when the controls were last shown, which is the whole of what a
    /// held pick or plan depends on. Point counts within a section are deliberately not included: a
    /// drag re-walks the spans it touches and changes those on every frame, and clearing the drag's own
    /// selection each frame would end the drag it belongs to.
    /// </summary>
    private int[] _layout = Array.Empty<int>();

    private static int[] LayoutOf(PartingLineEdit? edit) =>
        edit is null ? Array.Empty<int>() : edit.Rims.Select(r => r.Line.Spans.Count).ToArray();

    private static bool SameLayout(int[] was, PartingLineEdit? edit)
    {
        var now = LayoutOf(edit);
        if (was.Length != now.Length) return false;

        for (int i = 0; i < now.Length; i++)
            if (was[i] != now[i]) return false;

        return true;
    }

    /// <summary>
    /// Shows the edit as it now stands, patching the controls already on screen where it can and
    /// replacing them where it cannot - see <see cref="PartingLineEditVisuals.TryUpdate"/>. Every drag
    /// frame comes through here, so the patching path is the one that matters.
    /// </summary>
    public void SetPartingLineEdit(PartingLineEdit? edit, float handleSize)
    {
        _edit = edit;
        _handleSize = handleSize;

        // A selection or a hover that pointed into the old sections cannot be trusted to mean the same
        // thing after an edit changed how many there are. Measured against the section layout rather
        // than against emptiness: a Remove or an Insert leaves the edit perfectly non-empty while
        // renumbering everything after it, and a plan held over from before then names a section that
        // no longer exists - PartingLineEditor.Insert indexes Spans with it directly.
        if (!SameLayout(_layout, edit))
        {
            _selected = null;
            _hovered = null;
            _previewAt = null;
            _planned = null;
        }

        _layout = LayoutOf(edit);

        if (_editVisuals.TryUpdate(edit, handleSize, _selected, _hovered)) return;

        // Removed only on the path that really does replace them. Doing it unconditionally, as this
        // did, is what made every drag frame a full teardown: new models carry new ids, so each one is
        // a removal and an addition across the dispatcher whether anything changed or not.
        foreach (var id in _editVisuals.Ids) VisualRemovedById?.Invoke(id);

        _editVisuals.Build(edit, handleSize, _selected, _hovered);
        RefreshEditVisuals();
    }

    private void RefreshEditVisuals()
    {
        foreach (var model in _editVisuals.Models)
        {
            VisualAddedOrUpdated?.Invoke(model);
            SetVisibility(model, _currentState is PartingSplitState.DirectionSelection);
        }

        // The marker is one of those models but its visibility is not this state's to decide - it is
        // shown only while the cursor is on a section.
        _editVisuals.ApplyPreview();
    }

    /// <summary>Selects a section or handle from outside the scene - from a list, say.</summary>
    public void SetEditSelection(PartingLinePick? pick, float handleSize)
    {
        _selected = pick;
        _handleSize = handleSize;
        _editVisuals.ApplyState(_selected, _hovered);
    }

    /// <summary>
    /// Notes what the cursor is over and reports whether that changed. A handle grows; a section
    /// brightens, and gets a marker where a click on it would put a handle.
    /// </summary>
    private bool SetHover(PartingLinePick? pick, CoreVector3? at = null)
    {
        // The marker moves along the section as the cursor does, so it is refreshed even when the pick
        // itself has not changed - that is the whole of what it is showing.
        var planned = Planned(pick, at);
        bool moved = planned?.At != _previewAt;

        _previewAt = planned?.At;
        _planned = planned;

        if (_hovered == pick && !moved) return false;

        _hovered = pick;
        _editVisuals.ApplyState(_selected, _hovered);
        _editVisuals.SetPreview(_previewAt);
        return true;
    }

    /// <summary>Where a click would put a handle, or null if it would not put one anywhere.</summary>
    private PartingInsertion? Planned(PartingLinePick? pick, CoreVector3? at) =>
        pick is { IsHandle: false } section && at is { } point
            && PartingLineEditor.TryPlan(_edit, section.Rim, section.Index, point, out var insertion)
                ? insertion
                : null;

    /// <summary>Where the marker is, kept so a cursor that has not moved along the line asks for no work.</summary>
    private CoreVector3? _previewAt;

    /// <summary>What the next click on the line would do, worked out while showing the marker for it.</summary>
    private PartingInsertion? _planned;

    private static PointGeometryModel3D? BuildRimPointModel(
        IEnumerable<FlangeSealPoint> points, System.Windows.Media.Color colour, double size)
    {
        var positions = new Vector3Collection();
        foreach (var p in points)
            positions.Add(new Vector3(p.Position.X, p.Position.Y, p.Position.Z));

        if (positions.Count == 0)
            return null;

        return new PointGeometryModel3D
        {
            Geometry = new PointGeometry3D { Positions = positions },
            Color = colour,
            Size = new System.Windows.Size(size, size),
            IsHitTestVisible = false,
        };
    }

    private static LineGeometryModel3D? BuildPartingLineModel(PartingLine? partingLine)
    {
        if (partingLine is null)
            return null;

        var lineBuilder = new LineBuilder();
        foreach (var loop in partingLine.Loops)
        {
            if (loop.Count < 2)
                continue;

            for (int i = 0; i < loop.Count; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % loop.Count];
                lineBuilder.AddLine(new Vector3(a.X, a.Y, a.Z), new Vector3(b.X, b.Y, b.Z));
            }
        }

        return new LineGeometryModel3D
        {
            Geometry = lineBuilder.ToLineGeometry3D(),
            Color = Colors.Yellow,
            Thickness = 2.5,
            IsHitTestVisible = false
        };
    }

    /// <summary>
    /// Replaces the committed parting line - the one the later stages are built from, as opposed to
    /// the live one <see cref="UpdatePartingLinePreview"/> draws while a direction is still being chosen.
    /// </summary>
    public void SetPartingLine(PartingLine? partingLine)
    {
        if (_partingLineModel != null)
            VisualRemovedById?.Invoke(_partingLineModel.GUID);

        _partingLineModel = BuildPartingLineModel(partingLine);
        if (_partingLineModel is null)
            return;

        VisualAddedOrUpdated?.Invoke(_partingLineModel);
        SetVisibility(_partingLineModel, _currentState is PartingSplitState.DirectionSelection);
    }

    /// <summary>
    /// Replaces the two mould halves. They are re-cut whenever the parting mesh depth changes, so
    /// this must leave the line and parting-mesh visuals alone.
    /// </summary>
    public void SetRegions(IMesh? positiveRegion, IMesh? negativeRegion)
    {
        if (_positiveRegionModel != null)
            VisualRemovedById?.Invoke(_positiveRegionModel.GUID);
        if (_negativeRegionModel != null)
            VisualRemovedById?.Invoke(_negativeRegionModel.GUID);

        _positiveRegionModel = BuildRegionModel(positiveRegion, _positiveSkin);
        _negativeRegionModel = BuildRegionModel(negativeRegion, _negativeSkin);

        // UpdateState is not re-run on a re-cut, so apply this state's visibility here.
        var isVisible = _currentState is PartingSplitState.SplitResult;
        foreach (var model in new[] { _positiveRegionModel, _negativeRegionModel })
        {
            if (model is null)
                continue;

            VisualAddedOrUpdated?.Invoke(model);
            SetVisibility(model, isVisible);
        }
    }

    private MeshGeometryModel3D? BuildRegionModel(IMesh? region, Material skin)
    {
        if (region is null)
            return null;

        var geo = region.ToHelixMesh(_engine);
        return geo.IsSuccess
            ? new MeshGeometryModel3D { Geometry = geo.Value, Material = skin, IsHitTestVisible = false }
            : null;
    }

    /// <summary>
    /// Replaces just the parting-mesh visual, leaving the line and region previews alone. Called on
    /// every depth change, so it must not disturb anything else the committed preview built.
    /// </summary>
    public void UpdatePartingMesh(IMesh? mesh)
    {
        if (_partingMeshModel != null)
            VisualRemovedById?.Invoke(_partingMeshModel.GUID);
        _partingMeshModel = null;

        if (mesh is null)
            return;

        var geometry = mesh.ToHelixMesh(_engine);
        if (!geometry.IsSuccess)
        {
            System.Diagnostics.Debug.WriteLine("FAILED TO CREATE HELIX MESH FOR PARTING MESH");
            return;
        }

        _partingMeshModel = new MeshGeometryModel3D
        {
            Geometry = geometry.Value,
            Material = _partingMeshSkin,
            CullMode = SharpDX.Direct3D11.CullMode.Back,
            RenderWireframe = true,
            WireframeColor = Colors.Black,
            IsHitTestVisible = false
        };


        VisualAddedOrUpdated?.Invoke(_partingMeshModel);

        // UpdateState is not re-run on a depth change, so apply this state's visibility here.
        var isVisible = _currentState is PartingSplitState.PartingMeshPreview;
        SetVisibility(_partingMeshModel, isVisible);
    }

    private void SetVisibility(Element3D? element, bool isVisible)
    {
        if (element != null)
        {
            element.Visibility = isVisible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            if (element is GeometryModel3D geom)
            {
                geom.IsRendering = isVisible;
            }
        }
    }

    public void UpdateState(PartingSplitState state)
    {
        _currentState = state;

        // The controls only exist on step one, so a drag or a hover cannot survive leaving it - and a
        // live drag that did would keep moving a handle the user can no longer see.
        if (state is not PartingSplitState.DirectionSelection)
        {
            EndDrag();
            SetHover(null);
        }

        // Ensure all available models are added to the scene (VisualAddedOrUpdated gracefully ignores duplicates).
        VisualAddedOrUpdated?.Invoke(_grid);
        if (_baseMeshModel != null)
            VisualAddedOrUpdated?.Invoke(_baseMeshModel);
        if (_mouldMeshModel != null)
            VisualAddedOrUpdated?.Invoke(_mouldMeshModel);
        if (_partingLineModel != null)
            VisualAddedOrUpdated?.Invoke(_partingLineModel);
        foreach (var model in _ridgeContourModels) VisualAddedOrUpdated?.Invoke(model);
        if (_sealedRimModel != null)
            VisualAddedOrUpdated?.Invoke(_sealedRimModel);
        if (_breachedRimModel != null)
            VisualAddedOrUpdated?.Invoke(_breachedRimModel);
        if (_partingMeshModel != null)
            VisualAddedOrUpdated?.Invoke(_partingMeshModel);
        if (_positiveRegionModel != null)
            VisualAddedOrUpdated?.Invoke(_positiveRegionModel);
        if (_negativeRegionModel != null)
            VisualAddedOrUpdated?.Invoke(_negativeRegionModel);
        foreach (var model in _editVisuals.Models) VisualAddedOrUpdated?.Invoke(model);

        // Hide everything initially
        SetVisibility(_baseMeshModel, false);
        SetVisibility(_mouldMeshModel, false);
        SetVisibility(_partingLineModel, false);
        foreach (var model in _ridgeContourModels) SetVisibility(model, false);
        SetVisibility(_sealedRimModel, false);
        SetVisibility(_breachedRimModel, false);
        SetVisibility(_partingMeshModel, false);
        SetVisibility(_positiveRegionModel, false);
        SetVisibility(_negativeRegionModel, false);

        // The editing controls were left out of this pass, so stepping forward carried the sections and
        // their handles into stages two and three - drawn over the parting mesh and over the halves, on
        // a line that is committed by then and that a click there cannot change.
        foreach (var model in _editVisuals.Models) SetVisibility(model, false);

        switch (state)
        {
            case PartingSplitState.DirectionSelection:
                ShowBaseMesh(_heatMapSkin);
                // The line is recomputed live as the direction changes, so it is shown here too -
                // the user sees where the mould will part before committing to a direction.
                SetVisibility(_partingLineModel, true);
                // Shown alongside it: the rim is where the parting line usually wants to sit, so the
                // two being visible together is what makes the direction choice readable.
                foreach (var model in _ridgeContourModels) SetVisibility(model, true);
                foreach (var model in _editVisuals.Models) SetVisibility(model, true);

                // Except the marker, which answers to the cursor rather than to the stage.
                _editVisuals.ApplyPreview();
                break;

            case PartingSplitState.PartingMeshPreview:
                ShowGhostMould();
                SetVisibility(_partingMeshModel, true);
                // The rim tells the user whether this parting mesh can actually sever the mould, so
                // it belongs with the parting mesh rather than with the result they'd otherwise wait
                // for the boolean to refuse.
                SetVisibility(_sealedRimModel, true);
                SetVisibility(_breachedRimModel, true);
                break;

            case PartingSplitState.SplitResult:
                SetVisibility(_positiveRegionModel, true);
                SetVisibility(_negativeRegionModel, true);
                break;
        }
    }

    /// <summary>Shows the base mesh under the given skin. Only stage one uses the heat map.</summary>
    private void ShowBaseMesh(Material skin)
    {
        if (_baseMeshModel is null)
            return;

        _baseMeshModel.Material = skin;
        if (ReferenceEquals(skin, _heatMapSkin))
            RecomputeDirectionColors();

        SetVisibility(_baseMeshModel, true);
    }

    /// <summary>
    /// Shows the mould see-through, so the parting mesh sitting inside it stays readable. Culling is
    /// off so the mould's far wall draws too - with a single-sided skin the ghost would look like a
    /// shell open towards the camera.
    /// </summary>
    private void ShowGhostMould()
    {
        if (_mouldMeshModel is null)
            return;

        _mouldMeshModel.Material = _mouldGhostSkin;
        _mouldMeshModel.IsTransparent = true;
        _mouldMeshModel.CullMode = SharpDX.Direct3D11.CullMode.None;
        SetVisibility(_mouldMeshModel, true);
    }

    public void OnActivated()
    {
        VisualsCleared?.Invoke();
        UpdateState(_currentState);
    }

    public void OnDeactivated() { }

    public bool OnKeyDown(Key key) => false;
    public bool OnKeyUp(Key key) => false;
    /// <summary>
    /// Selects a section or a handle of the parting line, or begins a drag. Only while a direction is
    /// being chosen: past that the line is committed and everything downstream is built from it, so
    /// letting a stray click change it would silently invalidate the flange already on screen.
    /// </summary>
    public bool OnMouseDown(MouseDown3DEventArgs eventArgs)
    {
        if (_currentState is not PartingSplitState.DirectionSelection) return false;

        // Left button only. Right and middle drive the camera, and a selection that changed every time
        // the user orbited would be unusable.
        if (eventArgs.OriginalInputEventArgs is not MouseButtonEventArgs
            { ChangedButton: MouseButton.Left })
            return false;

        var result = eventArgs.HitTestResult;
        var hit = result?.ModelHit as Element3D;
        if (hit is null)
        {
            // Missed everything: drop the selection rather than leave a stale one highlighted.
            if (_selected is null) return false;

            _selected = null;
            EditSelectionChanged?.Invoke(null);
            return true;
        }

        if (_editVisuals.Identify(hit) is { } pick)
        {
            if (!pick.IsHandle)
            {
                // Wherever the marker is, which is where the user was told the handle would go. Worked
                // out fresh from this click's own hit point rather than reused, because a click can
                // arrive without a move in front of it - a click straight after the view opens, or one
                // that lands a pixel off where the last move reported.
                var planned = Planned(pick, ToCore(result!.PointHit)) ?? _planned;
                if (planned is not { } insertion) return false;

                HandleRequested?.Invoke(insertion);
                return true;
            }

            _selected = pick;

            // A handle press begins a drag, and the whole control set drops out of the hit test for its
            // duration - see PartingLineEditVisuals.IsHitTestable. Set before the selection is raised,
            // because that call is what puts the new appearance on screen.
            BeginDrag(pick.IsHandle ? pick : null);

            EditSelectionChanged?.Invoke(pick);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Carries a handle drag, or notes which handle the cursor is over when there is no drag in hand.
    ///
    /// <para>
    /// The point reported by a drag is wherever on the body the cursor is, which is not necessarily on
    /// the rim wall - pinning it there is the editor's job, and doing it here instead would make the
    /// handle refuse to follow the cursor while the user was still moving it.
    /// </para>
    /// </summary>
    public bool OnMouseMove(IList<HelixToolkit.Wpf.SharpDX.HitTestResult> hits)
    {
        // Nearest hit only. The handles are what the cursor is aimed at and they sit in front of
        // the body, so the topmost visual is the one this editor means in both branches below.
        var hit = hits.Count > 0 ? hits[0] : null;

        if (_dragging is not { } pick)
        {
            // Hover is only meaningful where the controls are, and it never consumes the move - the
            // camera and everything else downstream still want it.
            if (_currentState is not PartingSplitState.DirectionSelection)
            {
                SetHover(null);
                return false;
            }

            var over = _editVisuals.Identify(hit?.ModelHit as Element3D);
            SetHover(over, over is { IsHandle: false } && hit is not null ? ToCore(hit.PointHit) : null);
            return false;
        }

        // The only thing that ends a drag is the button coming up. Not leaving the body, not losing the
        // hit - a drag that ended whenever the cursor slipped past the silhouette would drop the handle
        // exactly where the user was moving it fastest, and a release outside the viewport never raises
        // MouseUp3D at all.
        if (Mouse.LeftButton == MouseButtonState.Released)
        {
            EndDrag();
            return true;
        }

        // No hit means the cursor is off the body, and there is nowhere to move the handle to. The drag
        // stays live so it resumes when the cursor comes back.
        if (hit is null) return true;

        var point = ToCore(hit.PointHit);
        if (_lastDragPoint is { } last
            && CoreVector3.DistanceSquared(last, point) < DragDeadZoneMm * DragDeadZoneMm)
            return true;

        _lastDragPoint = point;
        HandleMoved?.Invoke(pick, point);
        return true;
    }

    public bool OnMouseUp(MouseUp3DEventArgs eventArgs)
    {
        if (_dragging is null) return false;

        if (eventArgs.OriginalInputEventArgs is not MouseButtonEventArgs
            { ChangedButton: MouseButton.Left })
            return false;

        EndDrag();
        return true;
    }

    /// <summary>
    /// Takes the controls out of the hit test so the ray reaches the body underneath them, which is the
    /// surface the user is actually pointing at.
    /// </summary>
    private void BeginDrag(PartingLinePick? pick)
    {
        _dragging = pick;
        _lastDragPoint = null;

        if (pick is null) return;

        // The cube is about to sit under the cursor for the whole drag, and it is already amber and
        // grown for being selected - leaving the hover on as well would only make the handle bigger
        // than it should be for as long as the drag lasts. The marker goes with it: a drag is not a
        // proposal to divide anything.
        _hovered = null;
        _previewAt = null;
        _planned = null;
        _editVisuals.SetPreview(null);
        _editVisuals.IsHitTestable = false;
    }

    /// <summary>Ends a drag and puts the controls back into the hit test.</summary>
    private void EndDrag()
    {
        if (_dragging is null) return;

        _dragging = null;
        _lastDragPoint = null;
        _editVisuals.IsHitTestable = true;
    }

    private static CoreVector3 ToCore(Vector3 v) => new(v.X, v.Y, v.Z);
}