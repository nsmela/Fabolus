using System.Numerics;
using Fabolus.Core.Common;

namespace Fabolus.Core.Geometry;

/// <summary>
/// Provides mould-parting-line operations: finding where a mesh's silhouette (relative to a
/// pull direction) crosses itself, and building the solid tool geometry used to split a mould
/// along that silhouette. Implementations work directly against the underlying engine's native
/// mesh type to avoid round-tripping through IMesh's flat vertex/triangle arrays.
/// </summary>
public interface IPartingTools
{
    /// <summary>
    /// Computes the parting line of <paramref name="mesh"/> for the given pull direction: the
    /// set of closed loops where the surface normal is perpendicular to the pull direction
    /// (i.e. the silhouette seen looking along that direction). A mesh with an internal hole
    /// along the pull direction (e.g. a tunnel) produces more than one loop - everything past
    /// the largest (outer) loop is an internal hole that needs its own shut-off surface.
    /// </summary>
    /// <param name="mesh">The mesh to analyse - should be the base/body mesh, not a mould shell.</param>
    /// <param name="pullDirection">The direction the two halves will be pulled apart along.</param>
    /// <param name="noiseThreshold">
    /// Loops shorter than this fraction of the mesh's largest dimension are discarded as noise.
    /// </param>
    /// <param name="neutralBand">
    /// The draft-neutral tolerance band. The isoline is traced at its midpoint, so an asymmetric band
    /// biases the parting line toward the side the user opened up. Pass
    /// <see cref="PartingNeutralBand.None"/> for the plain zero-crossing silhouette.
    /// </param>
    Result<PartingLine> GeneratePartingLine(
        IMesh mesh, Vector3 pullDirection, float noiseThreshold = 0.1f, PartingNeutralBand neutralBand = default);

    /// <summary>
    /// Smooths a parting line while holding every point on <paramref name="surface"/> - the mesh the
    /// line was traced from. This is <see cref="PartingLineSmoother"/> with the engine supplying the
    /// closest-point projection it needs, and it is the form callers should use whenever they still
    /// have the traced mesh: the smoother on its own has no way to tell where the surface went, so it
    /// leaves the line a few tenths of a millimetre off it, and the flange is built by offsetting
    /// outward from exactly these points.
    /// </summary>
    /// <param name="pullDirection">
    /// The axis the halves separate along; the plane perpendicular to it is the footprint the
    /// smoothing and de-looping are biased toward.
    /// </param>
    Result<PartingLine> SmoothPartingLineOnSurface(
        IMesh surface,
        PartingLine line,
        Vector3 pullDirection,
        PartingLineSmoothingOptions options);

    /// <summary>
    /// Measures the curves where <paramref name="cutter"/> crosses <paramref name="mould"/> - the
    /// thing a boolean actually requires to be well formed. See <see cref="CutContourReport"/>.
    /// </summary>
    /// <param name="shiftCutter">
    /// The same virtual shift the cut itself will use, so the contours reported are the contours the
    /// boolean will see rather than ones from a position it is never evaluated at.
    /// </param>
    Result<CutContourReport> InspectCutContours(IMesh mould, IMesh cutter, Vector3 shiftCutter = default);

    /// <summary>
    /// The surface normal of <paramref name="mesh"/> at each of <paramref name="points"/>, averaged
    /// from the vertices around it rather than taken from the single face it lands on.
    ///
    /// <para>
    /// Averaging is the point. A per-face normal is piecewise constant, so consecutive points of a
    /// parting line that happen to straddle a triangle edge come back tens of degrees apart on a body
    /// this coarse - fine for a one-off query, useless for anything read along the line, which is
    /// what this exists for. Taking the vertex normals of the face and averaging them gives the
    /// smooth-shaded normal, which varies continuously as the point moves across the surface.
    /// </para>
    /// </summary>
    Result<IReadOnlyList<Vector3>> SampleSurfaceNormals(IMesh mesh, IReadOnlyList<Vector3> points);

    /// <summary>
    /// A closest-point projector onto <paramref name="mesh"/>, for callers that need to hold a curve
    /// on the surface it was traced from. The returned projector keeps a spatial index over the mesh
    /// and must be disposed; see <see cref="ISurfaceProjector"/> for why it is a handle rather than a
    /// function.
    /// </summary>
    Result<ISurfaceProjector> CreateSurfaceProjector(IMesh mesh);

    /// <summary>
    /// A shortest-path finder across <paramref name="mesh"/>, for callers that need to join two points
    /// on a surface by the route the surface itself allows. The returned handle keeps a spatial index
    /// and the native mesh and must be disposed; see <see cref="ISurfaceGeodesic"/>.
    /// </summary>
    Result<ISurfaceGeodesic> CreateSurfaceGeodesic(IMesh mesh);

    /// <summary>
    /// The rectangle, offset <paramref name="offset"/> mm beyond the mesh's extent, that a flange
    /// sweeps out to. Returned as world-space points on the footprint plane for
    /// <paramref name="pullDirection"/> (see <see cref="PartingFrame"/>) - axis-aligned within that
    /// plane, not in world axes, so it tracks the pull direction instead of assuming +Y.
    /// </summary>
    Result<IReadOnlyList<Vector3>> GenerateOuterBoxContour(
        IMesh referenceMesh, Vector3 pullDirection, float offset = 10.0f);

    Result<IReadOnlyList<Vector3>> GenerateInnerConcaveContour(IMesh referenceMesh, PartingLine partingLine, float offset = 0);

    /// <param name="launchSurface">
    /// The body the parting line was traced on. Supplying it makes the flange leave the line along
    /// that body's surface normal before relaxing to level, instead of flattening straight away -
    /// <see cref="PartingMeshSweep.TangentLaunch"/>. Null builds the flange exactly as it always was.
    /// </param>
    /// <param name="launchHoldMm">
    /// How far out from the parting line, in mm, the body's normal direction is held before the height
    /// relaxation is allowed to take over. Ignored without a <paramref name="launchSurface"/>.
    ///
    /// <para>
    /// This is the dial that decides whether following the normals is visible at all. At the 15mm it
    /// was fixed at, the launch moved the flange a measured 0.12mm on average against a rim that swings
    /// 35mm, so the result was indistinguishable from the plain planar sweep - same triangle count,
    /// same halves to within a tenth of a percent. Widening it carries the body's direction further out
    /// before the flange levels off.
    /// </para>
    /// </param>
    Result<IMesh> GenerateWavefrontFlangeMesh(
        IReadOnlyList<Vector3> inner3DLoop,
        IReadOnlyList<Vector2> outerPlanarBox,
        Vector3 planeNormal,
        float stepDistanceMm = 3.0f,
        int maxRibbonRings = 200,
        float concaveBandWidthMm = 3.0f,
        float overhangTargetSlopeDeg = 40f,
        float innerSealHold = 0.5f,
        float innerBleedMm = 2.5f,
        IMesh? sealAgainst = null,
        float sealMarginMm = 0.5f,
        IMesh? launchSurface = null,
        float launchHoldMm = 15.0f,
        bool rawFlange = false,
        int launchSmoothingPasses = 0);

    /// <summary>
    /// Builds the flange by marching outward in 3D rather than in a projected plane: every point of
    /// the parting line steps along its own outward direction, seeded from
    /// <paramref name="body"/>'s surface normal there and turned gradually toward the plane
    /// perpendicular to <paramref name="planeNormal"/> so the outer rim flattens and reaches past the
    /// mould. See <see cref="PartingMeshSweep.SurfaceSweep"/>.
    /// </summary>
    /// <param name="outerPlanarBox">
    /// Used only to know when to stop marching - the ring that lies entirely outside it is the last.
    /// Nothing is clipped against it.
    /// </param>
    Result<IMesh> GenerateSurfaceSweepFlangeMesh(
        IReadOnlyList<Vector3> inner3DLoop,
        IReadOnlyList<Vector2> outerPlanarBox,
        Vector3 planeNormal,
        IMesh body,
        float stepDistanceMm = 3.0f,
        int maxRings = 200,
        float innerBleedMm = 2.5f,
        float boundsMarginMm = 10f);

    Result<IMesh> GenerateSurfaceSweepFlangeMesh3D(
        IReadOnlyList<Vector3> inner3DLoop,
        Vector3 planeNormal,
        IMesh body,
        float stepDistanceMm = 3.0f,
        int maxRings = 200,
        float innerBleedMm = 2.5f,
        float boundsMarginMm = 10f);

    /// <summary>
    /// Reports where <paramref name="flange"/>'s inner rim sits relative to <paramref name="body"/> -
    /// the read-only counterpart to the seal that <see cref="GenerateWavefrontFlangeMesh"/> applies
    /// when handed a <c>sealAgainst</c> mesh. Any point coming back with a positive signed distance is
    /// outside the body and will bridge the cut.
    /// </summary>
    /// <param name="partingLoop">
    /// The parting line, used to tell the inner rim from the outer one: the inner rim is the boundary
    /// that falls inside this loop's footprint, and it is the only one that has to seal.
    /// </param>
    Result<IReadOnlyList<FlangeSealPoint>> InspectFlangeSeal(
        IMesh flange,
        IMesh body,
        Vector3 pullDirection,
        IReadOnlyList<Vector3> partingLoop);

    /// <summary>
    /// Thickens an open flange surface (from <see cref="GenerateWavefrontFlangeMesh"/>) into a closed
    /// solid slab <paramref name="depth"/> mm thick, offset symmetrically along <paramref name="direction"/>
    /// (the pull axis) and walled around every boundary edge. Cheap enough to re-run on a depth-slider drag.
    /// </summary>
    /// <summary>
    /// <para>
    /// Do not remesh the result. It is the obvious move against a self-intersecting cutter and it has
    /// been measured twice, both times harmful. The slab is <c>depth</c> thick - a tenth of a
    /// millimetre by default - while any useful remesh target is one to four millimetres, ten to
    /// forty times that, so relocating a vertex by anything near the target drives the two sheets
    /// through each other. Measured on the finished solid: chin 0 self-intersections becoming 74 at a
    /// 2mm target and 194 at 4mm, scalp 0 becoming 251, larynx 67 becoming 1,321 - and two splits that
    /// worked stopped working. Not one case improved at any target.
    /// </para>
    /// </summary>
    Result<IMesh> ExtrudeFlange(IMesh surface, Vector3 direction, float depth);

    /// <summary>
    /// Thickens the flange surface into a solid by offsetting it, rather than by copying it to two
    /// sheets and walling the gap.
    ///
    /// <para>
    /// The reason to prefer it is that it cannot produce a self-intersecting result. The offset is
    /// taken from a distance field sampled on a voxel grid and re-extracted as a surface, and a
    /// distance field has no memory of the input having crossed itself - so whatever state the flange
    /// is in, what comes back is a clean manifold solid. Extruding preserves the input's crossings
    /// exactly, and doubles them.
    /// </para>
    ///
    /// <para>
    /// What it costs is resolution: the grid has to resolve the thickness, so a very thin cutter
    /// needs a very fine grid. That is affordable only because the gap between the halves no longer
    /// comes from the cutter's thickness - it comes from shifting the cutter in the boolean - which
    /// leaves the thickness free to be chosen for the grid rather than for the fit.
    /// </para>
    /// </summary>
    /// <param name="voxelSizeMm">
    /// Grid resolution. The cost is cubic in the body size over this, so it is the setting that
    /// decides whether the operation is affordable at all - and it is only affordable because the gap
    /// between the halves no longer comes from the cutter's thickness, which leaves the thickness
    /// free to be chosen coarse enough for the grid.
    /// </param>
    Result<IMesh> ThickenFlange(IMesh surface, Vector3 direction, float thicknessMm, float voxelSizeMm);

    /// <summary>
    /// Thickens the flange surface into a closed solid filling everything from the surface out to
    /// <paramref name="distance"/> along <paramref name="direction"/> - one side of the parting,
    /// rather than a slab straddling it.
    ///
    /// <para>
    /// This is the tool the mould is divided with. Given a solid covering one side, the two halves
    /// are simply the mould's intersection with it and the mould's difference from it, with no
    /// severing step to fail and no need to work out afterwards which piece was which. Making the
    /// tool a thin slab instead cannot do that: the difference returns both halves joined and the
    /// intersection returns the slab.
    /// </para>
    ///
    /// <para>
    /// <paramref name="distance"/> has to carry the solid clear of the mould, so it is measured from
    /// the mould rather than chosen - anything short leaves part of the mould on neither side.
    /// </para>
    /// </summary>
    /// <param name="roundingMm">
    /// Grow-then-shrink applied to the finished solid. Each pass samples a distance field onto a grid
    /// and re-extracts it, which discards self-intersections; out and back leaves the shape put, less
    /// any detail finer than this - which is the detail that was crossing. Zero skips it.
    /// </param>
    /// <param name="topAlongAxis">
    /// Where the wall's top plane sits, measured along <paramref name="direction"/>. Has to clear the
    /// mould: everything between the parting surface and this plane becomes the solid.
    /// </param>
    Result<IMesh> ExtrudeFlangeToSolid(
        IMesh surface, Vector3 direction, float topAlongAxis, float roundingMm, float voxelSizeMm);

    Result<IMesh> GenerateHolePatch(
        IReadOnlyList<Vector3> loop,
        Vector3 planeNormal);
}
