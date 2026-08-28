using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using System.Numerics;
using System.Text.Json.Serialization;

namespace Fabolus.Core.Features.PartingSplit;

/// <summary>
/// The parting-split geometry, in the order it runs: find the body inside a mould, trace its parting
/// line for a pull direction, build the parting mesh that follows that line, and cut the mould with it.
///
/// Pure geometry: meshes and parameters in, meshes out. Nothing here touches the workspace or records
/// commands - committing a parting is <see cref="SplitMouldFeature"/>'s job, and it depends on this
/// class rather than the other way round. Keeping the arrow pointing one way is what lets the geometry
/// be exercised without ever building a Workspace.
///
/// Each stage is a separate call because the view shows each one to the user before moving on, and
/// because the later stages are re-run on their own - the parting mesh is re-extruded on every depth
/// change, and the halves re-cut from it.
/// </summary>
public sealed class PartingMeshFeature
{
    private readonly IGeometryEngine _engine;

    public PartingMeshFeature(IGeometryEngine engine) => _engine = engine;

    // ---------------------------------------------------------------- parting line

    /// <summary>
    /// The body a mould was generated around, as it stood right before the Mould command ran (after
    /// Rotate/Translate/Smoothing). The parting line is always traced on this rather than on the
    /// mould shell, whose outer offset surface doesn't reliably carry the same silhouette crossings.
    /// </summary>
    public Result<BodyMesh> GetBodyMesh(MouldMesh mould) => BodyMesh.Create(_engine, mould);

    /// <summary>
    /// Traces the parting line of the body inside <paramref name="mould"/> for the given pull
    /// direction. Cheap enough to run live while the user drags the direction gizmo.
    /// </summary>
    public Result<PartingLine> GeneratePartingLine(MouldMesh mould, Vector3 pullDirection)
    {
        if (pullDirection == Vector3.Zero) return MeshErrors.InvalidPullDirection;

        return GeneratePartingLine(
            mould, new PartingLineParameters { PullDirection = Vector3.Normalize(pullDirection) });
    }

    /// <summary>
    /// As above, but carrying the caller's full parameter set rather than just a direction.
    ///
    /// The replay paths must use this one. Rebuilding the parameters from the pull direction alone
    /// silently drops the neutral band, noise threshold, smoothing and pinch settings back to their
    /// defaults, so the committed geometry stops matching the line the user reviewed - which is
    /// exactly what <see cref="GeneratePartingLineFromBody"/> promises cannot happen.
    /// </summary>
    public Result<PartingLine> GeneratePartingLine(MouldMesh mould, PartingLineParameters parameters)
    {
        if (parameters.PullDirection == Vector3.Zero) return MeshErrors.InvalidPullDirection;

        var bodyResult = GetBodyMesh(mould);
        if (bodyResult.IsFailure) return bodyResult.Error;

        return GeneratePartingLineFromBody(bodyResult.Value, parameters);
    }

    /// <summary>
    /// Traces the parting line of a body mesh directly, for callers that already have it (the view
    /// keeps the body on hand to draw it, so it does not pay for the command replay on every drag).
    /// The body is any surface to trace, not a mould, so it stays a bare <see cref="IMesh"/> - there
    /// is nothing mould-shaped to validate here.
    ///
    /// The raw isoline is amputated of pinches and smoothed before being returned: the same line is
    /// used for the live preview and for the committed split, so the user cannot review one shape
    /// and get another.
    ///
    /// Smoothing runs against <paramref name="bodyMesh"/> rather than on the loop alone, so every
    /// point of the returned line lies exactly on the body it was traced from - the flange is offset
    /// outward from these points, and a line floating off the surface seats the mould halves against
    /// nothing.
    /// </summary>
    public Result<PartingLine> GeneratePartingLineFromBody(BodyMesh body, PartingLineParameters parameters)
    {
        if (body is null) return MeshErrors.NullSource;

        // Dispatched here, on the recipe, rather than chosen by the caller - this is the one place
        // both the interactive path and SplitCommand's replay come through, so routing it anywhere
        // else would let a saved split rebuild itself from a different line than the one approved.
        if (parameters.Source == PartingLineSource.ExtrusionBorder)
            return GeneratePartingLineFromThickness(body, parameters.ThicknessOptions);

        if (parameters.PullDirection == Vector3.Zero) return MeshErrors.InvalidPullDirection;

        var bodyMesh = body.Mesh;

        var lineResult = _engine.PartingTools.GeneratePartingLine(
            bodyMesh, parameters.PullDirection, parameters.NoiseThreshold, parameters.NeutralBand);
        if (lineResult.IsFailure) return lineResult.Error;

        var line = PartingLinePinchFilter.AmputatePinches(lineResult.Value, parameters.PinchOptions);
        return _engine.PartingTools.SmoothPartingLineOnSurface(
            bodyMesh, line, parameters.PullDirection, parameters.SmoothingOptions);
    }

    /// <summary>
    /// Traces the parting line from the body's own wall thickness instead of from a pull direction -
    /// see <see cref="ThicknessParting"/> for how and why.
    ///
    /// <para>
    /// An alternative to <see cref="GeneratePartingLineFromBody"/> rather than a replacement, because
    /// the two answer different questions. The silhouette tracer asks where the mould comes apart
    /// along a chosen direction; this asks where the body's own extrusion border runs. The border is
    /// usually where the user wants the line, and finding it needs no direction at all - but nothing
    /// here checks the answer is mouldable, so a caller that has a pull direction in mind still has
    /// to check the result against it.
    /// </para>
    ///
    /// <para>
    /// Returned unsmoothed by the loop filters the silhouette path applies. Those exist to repair a
    /// traced isoline - pinches, needles, footprint self-crossings - and this line has none of them
    /// to repair: it is a region boundary, so it is closed and simple by construction.
    /// </para>
    ///
    /// <para>
    /// It does share the silhouette path's one guarantee about position: every point of the returned
    /// line lies on the body. The trace is on the surface to begin with, and the projector passed
    /// below is what holds it there through the relaxation that follows.
    /// </para>
    ///
    /// <para>
    /// The traced line is then centred in the rim wall it runs along - see
    /// <see cref="PartingLineCentring"/>. The trace leaves it off centre in stretches: on
    /// <c>standard</c> the bias runs to 0.80 of the way across the band and 0.86 at worst, and on the
    /// STL bodies it sits a tenth of the band off centre along its whole length. Centring takes the
    /// worst of that to 0.61 and clears the beyond-0.75 tail entirely.
    /// </para>
    ///
    /// <para>
    /// It does cost two bodies a self-intersecting parting solid, which was reason enough to leave it
    /// off until the cause was known. It now is, and it is not this: at the default 0.1mm cutter depth
    /// <c>RepairSelfIntersections</c> turns 8 crossings into 215, the repair is correctly discarded and
    /// the unrepaired cutter is returned - so the count that changes is noise on a repair that cannot
    /// work at that thickness, on a body that already fails the same way with no centring at all. At
    /// 1mm every body and both lines repair to zero. Fixing that belongs in the thickening, and
    /// withholding a measurably better line to keep a broken gate quiet would be the wrong trade.
    /// </para>
    /// </summary>
    /// <param name="centring">
    /// Null uses <see cref="PartingLineCentringOptions.Default"/>. There is no way to ask for no
    /// centring at all, deliberately - a caller that wants the raw trace wants
    /// <see cref="ThicknessParting.Trace"/>, which is public and is what this is built on.
    /// </param>
    public Result<PartingLine> GeneratePartingLineFromThickness(
        BodyMesh body, ThicknessPartingOptions? options = null,
        PartingLineCentringOptions? centring = null)
    {
        if (body is null) return MeshErrors.NullSource;

        var thickness = _engine.Evaluators.MeasureWallThickness(body.Mesh, WallThicknessOptions.Default);
        if (thickness.IsFailure) return thickness.Error;

        // A projector is wanted but not required: it only holds the line on the surface, so failing
        // to build one is worth continuing without rather than failing the whole trace. The line is
        // then smoothed free of the body and can drift off it, which is a degraded result, not a
        // wrong one.
        var projector = _engine.PartingTools.CreateSurfaceProjector(body.Mesh);
        using var held = projector.IsSuccess ? projector.Value : null;

        var tracing = options ?? ThicknessPartingOptions.Default;
        var traced = ThicknessParting.Trace(body.Mesh, thickness.Value, tracing, held);
        if (traced.IsFailure) return traced;

        // Three sources, in the order of how much of the answer each takes from the body rather than
        // from a solve over it. The crease offset takes its shape from a crease, which is a curve that
        // is genuinely on the body; the medial line solves a level set through the band, which needs
        // the band wide and evenly meshed enough to carry one; the corrected trace pushes an existing
        // curve about, which is the least of the three and the only one that always answers.
        var walls = RimWalls(body.Mesh, thickness.Value.Statistics.Median);

        // How much relaxation the caller asked for, carried across to the source that actually
        // produces the line. Without this the setting is quietly dead on every body the offset can
        // answer for - it belongs to the trace, and the trace is now only the fallback - so a caller
        // asking for an unsmoothed line would silently get a smoothed one. Scaled rather than copied
        // because the two stages start from different curves and their defaults are not comparable;
        // what has to survive is the ratio, and above all that zero means zero.
        float relaxation = ThicknessPartingOptions.Default.SmoothingPasses <= 0
            ? 1f
            : (float)tracing.SmoothingPasses / ThicknessPartingOptions.Default.SmoothingPasses;

        var offset = Offset(body.Mesh, walls, held, relaxation);
        var medial = offset is null
            ? Medial(body.Mesh, thickness.Value.Statistics.Median, held)
            : null;

        var placed = offset ?? medial ?? PartingLineCentring.Centre(
            traced.Value, walls, centring ?? PartingLineCentringOptions.Default, held);

        // Straightening is deliberately not applied here - see PartingLineStraightening for what it
        // does and why it is not wired in. It improves every measure of the curve itself and breaks
        // three downstream tests doing it, which settles a question worth having settled: the line
        // being central is not an aesthetic preference that could be traded for smoothness. The flange
        // rim is offset from the line and has to seat inside the body, and it stops sealing well before
        // the line reaches a crease.

        // The repair, by contrast, is applied - and to whichever line came back, not to the offset
        // alone. All three sources take their shape from the rim in some measure, so a step in the rim
        // is a step any of them can inherit, and a repair gated on which source produced the line would
        // stop working the moment the source changed. It also only ever moves the line towards the
        // middle, which is why it does not run into the objection above.
        return PartingLineTreatment.Apply(
            placed, walls,
            PartingLineTreatmentOptions.Default with
            {
                PolishPasses = Scale(PartingLineTreatmentOptions.Default.PolishPasses, relaxation),
            },
            held);
    }

    /// <summary>
    /// A pass count scaled by the caller's relaxation, never rounding a wanted pass away to none.
    /// Zero in is zero out, which is the one value that has to be exact - a caller asking for a raw
    /// line is asking to see what the stage before it produced.
    /// </summary>
    private static int Scale(int passes, float factor) =>
        factor <= 0f ? 0 : Math.Max((int)MathF.Round(passes * factor), 1);

    /// <summary>
    /// Puts a parting line into the form a user can edit: one rim per wall, each cut into sections with
    /// a handle at every join and confined to the wall it runs in.
    ///
    /// <para>
    /// Takes a line rather than tracing one, so what the user edits is exactly what they were shown.
    /// Re-tracing here would be a second answer to a question already settled, and the two would differ
    /// wherever anything upstream had been re-run in between.
    /// </para>
    /// </summary>
    /// <param name="line">
    /// The line as computed, normally from <see cref="GeneratePartingLineFromThickness"/>. Loops are
    /// matched to rims by which wall each runs nearest, so the order they arrive in does not matter.
    /// </param>
    public Result<PartingLineEdit> BeginPartingLineEdit(BodyMesh body, PartingLine line)
    {
        if (body is null || line is null || !line.IsValid) return MeshErrors.InvalidPartingLine;

        var thickness = _engine.Evaluators.MeasureWallThickness(body.Mesh, WallThicknessOptions.Default);
        if (thickness.IsFailure) return thickness.Error;

        var ridge = RidgeDetection.FindRidge(body.Mesh, RidgeDetectionOptions.Default);
        if (ridge.Band.Length != ridge.Faces.Length)
            return new Error("Geometry.NoRimBand",
                "This body's rim did not resolve into a band, so there is no wall to confine an " +
                "edited line to.");

        var contours = ridge.Contours.Where(c => c.IsClosed).ToList();
        var walls = PartingStrategy.Rims(contours, thickness.Value.Statistics.Median)
            .Where(r => r.Kind == PartingRimKind.Wall)
            .ToList();

        if (walls.Count == 0)
            return new Error("Geometry.NoWallRim",
                "This body has no rim with two sides, so there is no wall a line could be edited within.");

        var mask = ridge.Band;
        var rims = new List<PartingRimEdit>(walls.Count);
        var taken = new bool[line.Loops.Count];

        foreach (var wall in walls)
        {
            var band = new PartingBand(
                contours[wall.ContourIndices[0]], contours[wall.ContourIndices[1]]);

            var graph = PartingBandGraph.Build(body.Mesh, mask, ridge.FaceRims, wall.Id, band);
            if (graph is null) continue;

            // Matched by which loop runs nearest this wall, and claimed once, so two rims that converge
            // cannot both take the same loop and leave the other with none.
            int best = -1;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < line.Loops.Count; i++)
            {
                if (taken[i]) continue;

                var loop = line.Loops[i];
                float total = 0f;
                int sampled = 0;

                for (int p = 0; p < loop.Count; p += Math.Max(loop.Count / 16, 1))
                {
                    total += MathF.Min(
                        PartingBand.Closest(loop[p], band.First).Distance,
                        PartingBand.Closest(loop[p], band.Second).Distance);
                    sampled++;
                }

                float mean = sampled == 0 ? float.MaxValue : total / sampled;
                if (mean >= bestDistance) continue;

                bestDistance = mean;
                best = i;
            }

            if (best < 0) continue;

            taken[best] = true;
            var sectioned = PartingLineEditor.Seed(line.Loops[best], band);
            if (sectioned.Anchors.Count == 0) continue;

            rims.Add(new PartingRimEdit(sectioned, graph));
        }

        return rims.Count == 0
            ? new Error("Geometry.NoEditableRim",
                "No rim of this body could be put into an editable form.")
            : Result.Success(new PartingLineEdit(rims));
    }

    /// <summary>
    /// One crease offset per wall rim, or null if any of them fails to come back.
    ///
    /// <para>
    /// All or nothing, for the same reason <see cref="Medial"/> is: a body parts along every one of its
    /// rims at once, so a result that had one curve for one rim and a different kind for another would
    /// be two answers to the same question, and the difference between them would show up as a step
    /// where they met.
    /// </para>
    /// </summary>
    private static PartingLine? Offset(
        IMesh body, IReadOnlyList<PartingBand> walls, ISurfaceProjector? projector, float relaxation)
    {
        if (walls.Count == 0) return null;

        var options = CreaseOffsetOptions.Default with
        {
            SmoothingPasses = Scale(CreaseOffsetOptions.Default.SmoothingPasses, relaxation),
        };

        var loops = new List<IReadOnlyList<Vector3>>(walls.Count);
        foreach (var wall in walls)
        {
            var loop = CreaseOffsetLine.Trace(body, wall, projector, options);
            if (loop is null) return null;

            loops.Add(loop);
        }

        return new PartingLine(loops);
    }

    /// <summary>
    /// One medial line per wall rim, or null if any of them fails to come back closed.
    ///
    /// <para>
    /// All or nothing on purpose. A body parts along every one of its rims at once, so a result that
    /// had the medial line for one and the corrected trace for another would be two different curves
    /// answering the same question - and the difference between them would show up as a step where they
    /// met. Falling back wholesale keeps the line one thing.
    /// </para>
    /// </summary>
    private static PartingLine? Medial(IMesh body, float wallThickness, ISurfaceProjector? projector)
    {
        var ridge = RidgeDetection.FindRidge(body, RidgeDetectionOptions.Default);
        if (ridge.Band.Length != ridge.Faces.Length) return null;

        var closed = ridge.Contours.Where(c => c.IsClosed).ToList();
        var walls = PartingStrategy.Rims(closed, wallThickness)
            .Where(rim => rim.Kind == PartingRimKind.Wall)
            .ToList();

        if (walls.Count == 0) return null;

        var loops = new List<IReadOnlyList<Vector3>>(walls.Count);
        foreach (var rim in walls)
        {
            var band = new PartingBand(closed[rim.ContourIndices[0]], closed[rim.ContourIndices[1]]);
            var loop = BandMedialLine.Trace(
                body, ridge.Band, ridge.FaceRims, rim.Id, band, BandMedialOptions.Default, projector);

            if (loop is null) return null;
            loops.Add(loop);
        }

        return new PartingLine(loops);
    }

    /// <summary>
    /// The same line made as straight as the rim wall allows - see
    /// <see cref="PartingLineStraightening"/> for what that costs and why it is offered rather than
    /// applied.
    ///
    /// <para>
    /// Takes a line already traced rather than tracing one, so a caller can put the two side by side
    /// without paying for the wall-thickness probe twice.
    /// </para>
    /// </summary>
    public Result<PartingLine> StraightenPartingLine(
        BodyMesh body, PartingLine line, PartingLineStraighteningOptions? options = null)
    {
        if (body is null || line is null) return MeshErrors.NullSource;

        var thickness = _engine.Evaluators.MeasureWallThickness(body.Mesh, WallThicknessOptions.Default);
        if (thickness.IsFailure) return thickness.Error;

        var projector = _engine.PartingTools.CreateSurfaceProjector(body.Mesh);
        using var held = projector.IsSuccess ? projector.Value : null;

        return PartingLineStraightening.Straighten(
            line, RimWalls(body.Mesh, thickness.Value.Statistics.Median),
            options ?? PartingLineStraighteningOptions.Default, held);
    }

    /// <summary>
    /// The bands the line can be centred within: one per rim that is a wall with two sides.
    ///
    /// <para>
    /// Only wall rims. A rim that has tapered to a knife edge has no band - its single contour is the
    /// parting line rather than one boundary of it - and one whose contours could not be told apart
    /// cannot say which of them bounds which side, so neither has a middle to aim at.
    /// </para>
    ///
    /// <para>
    /// The grouping comes from <see cref="PartingStrategy.Rims"/> rather than being worked out here,
    /// for the same reason the scene manager takes it from there: deriving it twice is how the report,
    /// the picture and the line come to disagree about what the rims are.
    /// </para>
    /// </summary>
    private static IReadOnlyList<PartingBand> RimWalls(IMesh body, float wallThickness)
    {
        var contours = RidgeDetection.FindRidgeContours(body, RidgeDetectionOptions.Default)
            .Where(c => c.IsClosed).ToList();

        return PartingStrategy.Rims(contours, wallThickness)
            .Where(rim => rim.Kind == PartingRimKind.Wall)
            .Select(rim => new PartingBand(
                contours[rim.ContourIndices[0]], contours[rim.ContourIndices[1]]))
            .ToList();
    }

    /// <summary>
    /// The body's surface normal at each of <paramref name="points"/>, averaged from the vertices
    /// around it - see <see cref="IPartingTools.SampleSurfaceNormals"/>.
    ///
    /// <para>
    /// Here so the view can draw the normals along the parting line without reaching past the
    /// feature into the engine, which is the only route it has to geometry.
    /// </para>
    /// </summary>
    public Result<IReadOnlyList<Vector3>> SampleSurfaceNormals(BodyMesh body, IReadOnlyList<Vector3> points) =>
        body is null ? MeshErrors.NullSource : _engine.PartingTools.SampleSurfaceNormals(body.Mesh, points);

    // ---------------------------------------------------------------- parting mesh axis

    /// <summary>
    /// Settles the axis the parting mesh is built on, which is the one decision that separates the
    /// two kinds of parting mesh. Returns the parameters with <see cref="PartingMeshParameters.Axis"/>
    /// filled in and <see cref="PartingMeshParameters.AxisSource"/> normalized, so everything
    /// downstream reads a plain axis and no stage has to resolve anything a second time.
    ///
    /// <para>
    /// Callers that drive the build stage by stage - the view does, so it can show each artefact -
    /// must resolve once, when they first have the line, and use the returned parameters for every
    /// call after that. Passing the unresolved set to one stage and the resolved set to another
    /// builds the flange in one plane and extrudes or splits it in another. Resolving twice is
    /// harmless: the second call sees <see cref="PartingMeshAxisSource.PullDirection"/> and an axis
    /// already set, and hands back what it was given.
    /// </para>
    /// </summary>
    public static Result<PartingMeshParameters> ResolveAxis(
        PartingLine partingLine, PartingMeshParameters parameters)
    {
        if (parameters is null) return MeshErrors.NullSource;

        if (parameters.AxisSource == PartingMeshAxisSource.PullDirection)
        {
            if (parameters.Axis == Vector3.Zero) return MeshErrors.InvalidPullDirection;
            return parameters with { Axis = Vector3.Normalize(parameters.Axis) };
        }

        if (partingLine is null || !partingLine.IsValid) return MeshErrors.InvalidPartingLine;

        var fitted = BestFitNormal(partingLine.Loops[0]);
        if (fitted == Vector3.Zero)
            return new Error("Geometry.PartingLineHasNoPlane",
                "This parting line encloses no area from any direction, so it cannot supply an axis " +
                "of its own. Build the parting mesh on the pull direction instead.");

        // The sign is a labelling choice, not a geometric one: the flange and its footprint are the
        // same either way, and all that turns over is which half comes back as Positive. Aligning it
        // with the direction the user is looking along keeps those two names meaning what they see in
        // the viewport, without the shape depending on the direction at all.
        if (parameters.Axis != Vector3.Zero && Vector3.Dot(fitted, parameters.Axis) < 0f)
            fitted = -fitted;

        return parameters with { Axis = fitted, AxisSource = PartingMeshAxisSource.PullDirection };
    }

    /// <summary>
    /// Newell's method: the area-weighted normal of the polygon's best-fit plane. Preferred over a
    /// cross product of any three points because a parting line is never flat - Newell weighs the
    /// whole loop, so a local wobble cannot tip the answer the way a badly chosen triple would.
    /// Returns zero for a loop that encloses no area in any plane (a line doubled back on itself).
    /// </summary>
    private static Vector3 BestFitNormal(IReadOnlyList<Vector3> loop)
    {
        var normal = Vector3.Zero;
        for (int i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];
            normal += new Vector3(
                (a.Y - b.Y) * (a.Z + b.Z),
                (a.Z - b.Z) * (a.X + b.X),
                (a.X - b.X) * (a.Y + b.Y));
        }

        return normal.LengthSquared() < 1e-12f ? Vector3.Zero : Vector3.Normalize(normal);
    }

    /// <summary>
    /// The rectangle, offset beyond the mould, that the flange sweeps out to. Built in the plane
    /// perpendicular to <see cref="PartingMeshParameters.Axis"/>, so it tracks the pull direction.
    /// </summary>
    public Result<IReadOnlyList<Vector3>> GenerateOuterContour(MouldMesh mould, PartingMeshParameters parameters) =>
        _engine.PartingTools.GenerateOuterBoxContour(mould.Mesh, parameters.Axis, parameters.OuterContourMargin);

    /// <summary>
    /// Builds the flange as a zero-thickness surface running from the parting line out to
    /// <paramref name="outerContour"/>. Kept separate from <see cref="ExtrudeFlange"/> so a depth
    /// change only re-runs the cheap extrusion, not this.
    /// </summary>
    /// <param name="body">
    /// The body the parting line was traced on. Optional, but supplying it is what lets the flange
    /// guarantee its seal: the inner rim is placed by footprint arithmetic, which leaves a few
    /// vertices sitting fractionally outside the body, and each one is a hairline bridge that
    /// survives the cut and keeps the mould in one piece. With the body in hand those vertices are
    /// pushed inside it. Without it the flange is built as before and the cut relies on
    /// <see cref="PartingMeshParameters.Depth"/> being thick enough to swallow the leak.
    /// </param>
    public Result<IMesh> GenerateFlangeSurface(
        PartingLine partingLine, IReadOnlyList<Vector3> outerContour, PartingMeshParameters parameters,
        BodyMesh body)
    {
        if (!partingLine.IsValid) return MeshErrors.InvalidPartingLine;
        if (parameters.Axis == Vector3.Zero) return MeshErrors.InvalidPullDirection;

        var flatContour = outerContour
            .Select(v => PartingFrame.ToPlane(v, parameters.Axis))
            .ToList();

        var meshes = new List<IMesh>();

        Result<IMesh> outerMeshResult;
        if (parameters.Sweep == PartingMeshSweep.MouldLoft)
        {
            // The only sweep that asks the mould where the flange should come out, so it is the only
            // one that needs it. A body handed over without one is a caller that never had a mould
            // rather than a caller in error, so it falls back to the marching sweep instead of failing.
            outerMeshResult = body.ParentMould.HasValue
                ? _engine.PartingTools.GenerateMouldLoftFlangeMesh(
                    partingLine.Loops[0],
                    parameters.Axis,
                    body.Mesh,
                    body.ParentMould.Value.Mesh)
                : _engine.PartingTools.GenerateSurfaceSweepFlangeMesh3D(
                    partingLine.Loops[0],
                    parameters.Axis,
                    body.Mesh,
                    parameters.StepDistanceMm,
                    boundsMarginMm: parameters.OuterContourMargin);
        }
        else if (parameters.Sweep == PartingMeshSweep.SurfaceSweep)
        {
            outerMeshResult = _engine.PartingTools.GenerateSurfaceSweepFlangeMesh3D(
                partingLine.Loops[0],
                parameters.Axis,
                body.Mesh,
                parameters.StepDistanceMm,
                boundsMarginMm: parameters.OuterContourMargin);
        }
        else
        {
            outerMeshResult = _engine.PartingTools.GenerateWavefrontFlangeMesh(
                partingLine.Loops[0],
                flatContour,
                parameters.Axis,
                parameters.StepDistanceMm,
                overhangTargetSlopeDeg: parameters.FlangeMaxSlopeDeg,
                sealAgainst: body.Mesh,
                launchSurface: parameters.Sweep == PartingMeshSweep.TangentLaunch ? body.Mesh : null,
                launchHoldMm: parameters.NormalFollowMm,
                rawFlange: parameters.RawFlange,
                launchSmoothingPasses: parameters.NormalSmoothingPasses);
        }

        if (outerMeshResult.IsFailure) return outerMeshResult;
        meshes.Add(outerMeshResult.Value);

        // Generate shut-off surfaces for any internal holes (e.g. tunnels in the body)
        for (int i = 1; i < partingLine.Loops.Count; i++)
        {
            var patchResult = _engine.PartingTools.GenerateHolePatch(partingLine.Loops[i], parameters.Axis);
            if (patchResult.IsSuccess)
                meshes.Add(patchResult.Value);
        }

        return Combine(meshes);
    }

    /// <summary>
    /// Reports where the flange's inner rim sits relative to the body it has to seal against. Any
    /// point outside the body is a bridge the cut will leave behind, so this is what tells a caller
    /// whether the parting mesh can sever the mould before it spends a boolean finding out.
    /// </summary>
    public Result<IReadOnlyList<FlangeSealPoint>> InspectFlangeSeal(
        IMesh flangeSurface, BodyMesh body, PartingLine partingLine, PartingMeshParameters parameters)
    {
        if (!partingLine.IsValid) return MeshErrors.InvalidPartingLine;

        return _engine.PartingTools.InspectFlangeSeal(
            flangeSurface, body.Mesh, parameters.Axis, partingLine.Loops[0]);
    }

    /// <summary>
    /// Thickens a flange surface into the closed solid the mould is actually cut with. Cheap enough
    /// to re-run whenever <see cref="PartingMeshParameters.Depth"/> changes.
    /// </summary>
    public Result<IMesh> ExtrudeFlange(IMesh surface, PartingMeshParameters parameters)
    {
        if (parameters.Thickening == PartingMeshThickening.Offset)
        {
            return _engine.PartingTools.ThickenFlange(
                surface, parameters.Axis, parameters.Depth, parameters.OffsetVoxelSizeMm);
        }

        // Repaired after thickening, not before. Extruding is what creates the crossings here: it
        // copies the surface to two sheets offset along one axis, so anywhere the surface is steeper
        // than the slab is thick the two sheets pass through each other. Measured on chin, a flange
        // with no self-intersections at all becomes a cutter with 544.
        //
        // That is why the offset route existed - a distance field cannot represent a crossing, so it
        // sidestepped them. Repairing by cutting reaches the same place without the voxel grid: the
        // same chin cutter comes back with 26, still watertight, and with fewer triangles than it
        // started with. Relaxing, which is what this used to get by default, took it to 1,107.
        var extruded = _engine.PartingTools.ExtrudeFlange(surface, parameters.Axis, parameters.Depth);
        return RepairIfSelfIntersecting(extruded);
    }

    /// <summary>
    /// Runs the whole parting-mesh build for a committed line, in one call. Resolves the axis first,
    /// so the caller may pass either kind of <see cref="PartingMeshParameters"/>.
    /// </summary>
    public Result<IMesh> GeneratePartingMesh(
        MouldMesh mould, PartingLine partingLine, PartingMeshParameters parameters)
    {
        var resolved = ResolveAxis(partingLine, parameters);
        if (resolved.IsFailure) return resolved.Error;
        parameters = resolved.Value;

        var contourResult = GenerateOuterContour(mould, parameters);
        if (contourResult.IsFailure) return contourResult.Error;

        // The body is what the flange has to seal against. A replay failure isn't fatal - the flange
        // is still built, just without the guarantee - so this deliberately does not propagate.
        var bodyResult = GetBodyMesh(mould);
        if (bodyResult.IsFailure) return bodyResult.Error;

        var surfaceResult = GenerateFlangeSurface(partingLine, contourResult.Value, parameters, bodyResult.Value);
        if (surfaceResult.IsFailure) return surfaceResult.Error;

        return ExtrudeFlange(surfaceResult.Value, parameters);
    }

    /// <summary>
    /// Splits <paramref name="mould"/> with an already-built parting solid, returning both halves.
    /// </summary>
    /// <param name="partingMesh">
    /// A closed solid that fully spans the mould's cross-section along the parting surface. It is a
    /// produced artifact, not a mould, so it stays a bare <see cref="IMesh"/>. It must be the extruded
    /// solid rather than the flange surface it came from - a zero-thickness cutter subtracts to
    /// nothing and leaves the mould in one piece, reported as
    /// <see cref="MeshErrors.SplitProducedSinglePiece"/>.
    /// </param>
    /// <param name="separationAxis">
    /// The axis the two halves come apart along, which is the axis <paramref name="partingMesh"/> was
    /// built perpendicular to. This is not always the pull direction: a flange built on a fixed axis
    /// separates along that axis no matter which pull direction produced the parting line.
    /// </param>
    public Result<(IMesh Positive, IMesh Negative)> SplitMould(
        MouldMesh mould, IMesh partingMesh, PartingLine partingLine, Vector3 separationAxis)
    {
        if (partingMesh is null) return MeshErrors.NullSource;
        if (!partingLine.IsValid) return MeshErrors.InvalidPartingLine;
        if (separationAxis == Vector3.Zero) return MeshErrors.InvalidPullDirection;

        var axis = Vector3.Normalize(separationAxis);

        var cutResult = _engine.Booleans.Subtract(mould.Mesh, partingMesh);
        if (cutResult.IsFailure) return cutResult.Error;

        var componentsResult = _engine.Evaluators.SeparateComponents(cutResult.Value);
        if (componentsResult.IsFailure) return componentsResult.Error;

        // Biggest first, then the first piece is one half and everything else is the other.
        //
        // No vote and no severed check. Both used to live here and both were doing harm: the check
        // reported SplitProducedSinglePiece whenever the subtraction was judged not to have severed,
        // and the side test was a majority sample of each piece's vertices against the nearest
        // parting point, which could put both pieces on the same side and report the same error.
        // Measured on scalp, the raw subtraction gives two pieces at 56% and 43% of the mould at
        // every cutter depth and both thickenings - unambiguous by size, and still failing here.
        //
        // Ordering by volume is what makes "the first one" mean something: the pieces come back in
        // whatever order the component walk found them, so without it which half is Positive would
        // vary between runs of the same recipe.
        var components = componentsResult.Value
            .OrderByDescending(SignedVolume)
            .ToList();

        if (components.Count == 0) return MeshErrors.SplitProducedSinglePiece;

        var positiveResult = Combine([components[0]]);
        if (positiveResult.IsFailure) return positiveResult.Error;

        if (components.Count == 1)
            return (positiveResult.Value, components[0]);

        var negativeResult = Combine(components.Skip(1).ToList());
        if (negativeResult.IsFailure) return negativeResult.Error;

        return (positiveResult.Value, negativeResult.Value);
    }

    /// <summary>
    /// Runs MeshLib's self-intersection repair over a flange or cutter, but only when it has some.
    ///
    /// <para>
    /// This is <see cref="IGeometryModifiers.RepairSelfIntersections"/>, which resolves the crossings
    /// themselves. It is emphatically not a uniform remesh: one used to stand at the end of the
    /// flange build and it was the cause of the very failure it looks like it would fix - on every
    /// body measured it returned a mesh whose vertices largely coincided, thousands of zero-area
    /// faces, which is what the mould boolean was refusing to cut with.
    /// </para>
    ///
    /// <para>
    /// Cutting, not relaxing. Relaxing is what this got for as long as it existed, because the
    /// settings were left bare and that is MeshLib's default, and on every surface measured that has
    /// crossings it made them worse: larynx's flange 663 to 945, its cutter 3,649 to 11,555, chin's
    /// cutter 544 to 1,107, each while multiplying the triangle count. Relaxation can only slide
    /// vertices along the surface it already has, and no arrangement of them separates two sheets
    /// that have passed through each other. Cutting the region out and re-filling it does: the same
    /// three come back at 13, 536 and 26. It is destructive where it acts, so the worry was that it
    /// would perforate a cutter a tenth of a millimetre thick - measured, chin's stays watertight and
    /// loses a third of its triangles.
    /// </para>
    ///
    /// <para>
    /// Gated on there being something to repair so a mesh that is already clean is never rebuilt,
    /// and falling back to the original on failure, since a cutter with crossings still splits most
    /// moulds and is a better outcome than none at all.
    /// </para>
    /// </summary>
    private Result<IMesh> RepairIfSelfIntersecting(Result<IMesh> flange)
    {
        if (flange.IsFailure) return flange;

        var topology = _engine.Evaluators.ValidateTopology(flange.Value);
        if (topology.IsFailure || topology.Value.SelfIntersectionCount == 0) return flange;

        var repaired = _engine.Modifiers.RepairSelfIntersections(
            flange.Value, SelfIntersectionRepair.CutAndFill);
        if (repaired.IsFailure) return flange;

        // A repair that opened the cutter up is refused whatever it did to the crossing count: the
        // boolean needs a closed solid, and cutting is the one method that can take a hole out of a
        // sheet this thin. Only meaningful once the mesh is closed to begin with - a flange surface
        // is open by construction and stays that way.
        if (topology.Value.IsWatertight)
        {
            var closed = _engine.Evaluators.ValidateTopology(repaired.Value);
            if (closed.IsFailure || !closed.Value.IsWatertight) return flange;
        }

        // Only kept if it actually helped. The repair subdivides and relaxes around each crossing, so
        // on a badly folded surface it can leave more than it found.
        var after = _engine.Evaluators.ValidateTopology(repaired.Value);
        if (after.IsFailure || after.Value.SelfIntersectionCount >= topology.Value.SelfIntersectionCount)
            return flange;

        return repaired;
    }

    /// <summary>
    /// The flange surface for a resolved recipe, contour and body included. The stage-by-stage
    /// callers build these separately because they show each one; the recipe-driven paths just need
    /// the surface.
    /// </summary>
    private Result<IMesh> GenerateFlangeSurfaceFor(
        MouldMesh mould, PartingLine partingLine, PartingMeshParameters parameters)
    {
        var contour = GenerateOuterContour(mould, parameters);
        if (contour.IsFailure) return contour.Error;

        var body = GetBodyMesh(mould);
        if (body.IsFailure) return body.Error;

        return GenerateFlangeSurface(partingLine, contour.Value, parameters, body.Value);
    }

    /// <summary>
    /// How far past the mould, as a fraction of its own extent, the half-space tool is carried. It
    /// only has to clear the mould; the excess costs nothing because none of it meets anything.
    /// </summary>
    private const float HalfSpaceOvershoot = 0.25f;

    /// <summary>
    /// Divides <paramref name="mould"/> by taking each half straight from a boolean, using a solid
    /// that covers one side of <paramref name="flangeSurface"/> - see
    /// <see cref="PartingSplitMethod.ShiftedHalfSpaces"/>.
    ///
    /// <para>
    /// Takes the flange <em>surface</em>, not the extruded cutter: the tool it needs reaches all the
    /// way to one side rather than straddling the parting, and the gap comes from shifting it.
    /// </para>
    /// </summary>
    public Result<(IMesh Positive, IMesh Negative)> SplitMouldByHalfSpaces(
        MouldMesh mould, IMesh flangeSurface, PartingMeshParameters parameters)
    {
        if (flangeSurface is null) return MeshErrors.NullSource;
        if (parameters.Axis == Vector3.Zero) return MeshErrors.InvalidPullDirection;

        var axis = Vector3.Normalize(parameters.Axis);

        // The plane the wall is carried up to, past everything the mould reaches along the axis, so
        // the only part of the tool the mould can meet is the parting surface itself.
        float extent = ExtentAlong(mould.Mesh, axis);
        if (extent <= 0f) return MeshErrors.NullSource;

        float top = float.MinValue;
        foreach (var vertex in mould.Mesh.Vertices)
            top = MathF.Max(top, Vector3.Dot(vertex, axis));
        top += extent * HalfSpaceOvershoot;

        // Repaired as well as the surface it came from. Sweeping the flange along the axis is only
        // fold-free while the flange is a height field over the axis plane, and a sweep that follows
        // the body's normals deliberately is not one - so the tool can pick up crossings the surface
        // did not have.
        var toolResult = RepairIfSelfIntersecting(
            _engine.PartingTools.ExtrudeFlangeToSolid(
                flangeSurface, axis, top,
                roundingMm: 0f,
                voxelSizeMm: parameters.OffsetVoxelSizeMm));
        if (toolResult.IsFailure) return toolResult.Error;

        // Half a gap each way, so the halves end up a full gap apart and neither operation sees the
        // tool where the other one did.
        var step = axis * (parameters.Depth * 0.5f);

        // Positive is what the tool takes away; negative is what it overlaps.
        //
        // Each shift moves the tool so that its own half loses the gap, which means they go the
        // opposite way from the obvious one: shifting the tool along the axis makes the subtraction
        // remove less and the intersection keep more, so shifting each toward its own result is what
        // grows both and leaves them overlapping - measured at 103% to 109% of the mould between them.
        var positive = _engine.Booleans.Subtract(mould.Mesh, toolResult.Value, shiftB: -step);
        if (positive.IsFailure) return positive.Error;

        var negative = _engine.Booleans.Intersect(mould.Mesh, toolResult.Value, shiftB: step);
        if (negative.IsFailure) return negative.Error;

        // A half that came back empty means the tool did not cover the side it was meant to, which
        // this approach cannot report for itself - it hands back one intact mould and one nothing,
        // both of them valid meshes. The contour report says which way it went wrong.
        if (positive.Value.IsEmpty || negative.Value.IsEmpty)
        {
            var contours = _engine.PartingTools.InspectCutContours(mould.Mesh, toolResult.Value);
            string detail = contours.IsSuccess ? contours.Value.Describe() : "contours could not be measured";
            return new Error("Geometry.PartingMeshDidNotDivide",
                $"The parting mesh left one half empty, so it does not divide the mould - {detail}.");
        }

        return (positive.Value, negative.Value);
    }

    /// <summary>The mesh's extent along <paramref name="axis"/>, which world bounds cannot give for
    /// an axis that is not one of theirs.</summary>
    private static float ExtentAlong(IMesh mesh, Vector3 axis)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (var vertex in mesh.Vertices)
        {
            float along = Vector3.Dot(vertex, axis);
            min = MathF.Min(min, along);
            max = MathF.Max(max, along);
        }

        return max > min ? max - min : 0f;
    }

    /// <summary>
    /// Subtracts <paramref name="cutter"/> from the mould and returns the result as it comes - no
    /// separation, no side test, no check that anything was severed. See
    /// <see cref="PartingSplitMethod.SubtractOnly"/>.
    /// </summary>
    public Result<IMesh> CutMouldWith(MouldMesh mould, IMesh cutter)
    {
        if (mould is null || cutter is null) return MeshErrors.NullSource;

        return _engine.Booleans.Subtract(mould.Mesh, cutter);
    }

    // Cut subtracts the parting mesh and leaves the result as one mesh; SplitMould separates the two halves.
    public Result<IMesh> CutMould(MouldMesh mould, PartingLineParameters lineParameters, PartingMeshParameters meshParameters)
    {
        var lineResult = GeneratePartingLine(mould, lineParameters);
        if (lineResult.IsFailure)
            return lineResult.Error;

        if (meshParameters.SplitMethod == PartingSplitMethod.ShiftedHalfSpaces)
        {
            var resolvedCut = ResolveAxis(lineResult.Value, meshParameters);
            if (resolvedCut.IsFailure) return resolvedCut.Error;

            var surface = GenerateFlangeSurfaceFor(mould, lineResult.Value, resolvedCut.Value);
            if (surface.IsFailure) return surface.Error;

            var halves = SplitMouldByHalfSpaces(mould, surface.Value, resolvedCut.Value);
            if (halves.IsFailure) return halves.Error;

            // The joined result is just the two halves side by side. They are disjoint - that is what
            // the gap is - so combining them is a concatenation and not another boolean to go wrong.
            return Combine([halves.Value.Positive, halves.Value.Negative]);
        }

        var partingMeshResult = GeneratePartingMesh(mould, lineResult.Value, meshParameters);
        if (partingMeshResult.IsFailure)
            return partingMeshResult.Error;

        var cutResult = _engine.Booleans.Subtract(mould.Mesh, partingMeshResult.Value);
        if (cutResult.IsFailure)
            return cutResult.Error;

        // Subtract succeeds even when the parting mesh removes nothing (too thin, doesn't span the
        // cross-section, degenerate flange), handing back an intact mould. A cut that actually
        // severed leaves the result in two or more disconnected components, so a single component
        // is the signal that it didn't - surface it rather than return an unsevered mould as success.
        if (meshParameters.SplitMethod == PartingSplitMethod.SubtractOnly)
            return cutResult;

        var severedResult = _engine.Evaluators.HasMultipleComponents(cutResult.Value);
        if (severedResult.IsFailure)
            return severedResult.Error;
        if (!severedResult.Value)
            return MeshErrors.SplitProducedSinglePiece;

        return Result.Success(cutResult.Value);
    }

    /// <summary>
    /// Runs the entire pipeline from parameters alone: trace the line, build the parting mesh, cut.
    /// This is the replay path - <see cref="SplitCommand"/> stores only these two parameter sets, so
    /// it has to be able to reproduce the whole split from them.
    ///
    /// Interactive callers that have already shown the user a parting mesh should pass that mesh to
    /// the <see cref="SplitMould(MouldMesh, IMesh, PartingLine, Vector3)"/> overload instead, so the
    /// halves come from the exact solid that was reviewed.
    /// </summary>
    public Result<(IMesh Positive, IMesh Negative)> SplitMould(
        MouldMesh mould, PartingLineParameters lineParameters, PartingMeshParameters meshParameters)
    {
        var lineResult = GeneratePartingLine(mould, lineParameters);
        if (lineResult.IsFailure) return lineResult.Error;

        // The halves have to be told apart along the axis the parting mesh was actually built on, not
        // along whatever the caller's parameters happened to carry - on a line-aligned mesh those are
        // two different directions, and sorting the pieces by the wrong one puts both on one side.
        var resolved = ResolveAxis(lineResult.Value, meshParameters);
        if (resolved.IsFailure) return resolved.Error;

        if (resolved.Value.SplitMethod == PartingSplitMethod.ShiftedHalfSpaces)
        {
            var surface = GenerateFlangeSurfaceFor(mould, lineResult.Value, resolved.Value);
            if (surface.IsFailure) return surface.Error;

            return SplitMouldByHalfSpaces(mould, surface.Value, resolved.Value);
        }

        var partingMeshResult = GeneratePartingMesh(mould, lineResult.Value, resolved.Value);
        if (partingMeshResult.IsFailure) return partingMeshResult.Error;

        return SplitMould(mould, partingMeshResult.Value, lineResult.Value, resolved.Value.Axis);
    }


    // --- helpers --- //

    /// <summary>Enclosed volume by the divergence theorem, for ranking the cut pieces by size.</summary>
    private static double SignedVolume(IMesh mesh)
    {
        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;

        double total = 0;
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            total += Vector3.Dot(
                vertices[triangles[i]],
                Vector3.Cross(vertices[triangles[i + 1]], vertices[triangles[i + 2]])) / 6.0;
        }

        return Math.Abs(total);
    }

    private Result<IMesh> Combine(List<IMesh> pieces) =>
        pieces.Count == 1 ? Result.Success(pieces[0]) : _engine.CombineMeshes(pieces);

    /// <summary>
    /// How many of a component's vertices are sampled for the side vote. The vote is statistical, so
    /// a few thousand spread evenly over the piece settle it as firmly as all of them would, and it
    /// keeps the nearest-parting-point scan off the critical path on a dense mould.
    /// </summary>
    private const int SideVoteSampleTarget = 2000;

    /// <summary>
    /// Decides which half of the split <paramref name="component"/> belongs to, by asking - for each
    /// of a sample of its vertices - whether that vertex sits above or below the parting line
    /// <em>where the parting line runs beneath it</em>, and taking the majority.
    ///
    /// <para>
    /// The comparison has to be local. Comparing a component's centroid against the parting line's
    /// centroid (which is what this used to do) assumes the two halves straddle the line's mean
    /// height, and they routinely don't: on chin.3mf the cut severs correctly into two pieces whose
    /// centroids are at -6.84 and -1.53 along the axis, while the parting line's centroid is -7.70,
    /// so both pieces read as "positive", one side comes back empty, and a perfectly good cut is
    /// reported as <see cref="MeshErrors.SplitProducedSinglePiece"/>. Matching each vertex to the
    /// nearest parting point in the footprint removes that assumption - a tall thin piece and a
    /// short wide one are still classified correctly, because neither is compared against a height
    /// drawn from the far side of the mould.
    /// </para>
    /// </summary>
    private static bool IsPositiveSide(IMesh component, PartingLine partingLine, Vector3 axis)
    {
        var loopPoints = partingLine.Loops.SelectMany(l => l).ToArray();
        if (loopPoints.Length == 0) return true;

        // Footprint coordinates: strip the axis component, so "nearest" means nearest as seen
        // looking along the pull axis rather than nearest in space (a point directly above a
        // parting point is what we want to compare against, however far above it sits).
        var flatLoop = new Vector3[loopPoints.Length];
        var loopHeight = new float[loopPoints.Length];
        for (int i = 0; i < loopPoints.Length; i++)
        {
            loopHeight[i] = Vector3.Dot(loopPoints[i], axis);
            flatLoop[i] = loopPoints[i] - (axis * loopHeight[i]);
        }

        var vertices = component.Vertices;
        int stride = Math.Max(1, vertices.Length / SideVoteSampleTarget);

        int above = 0;
        int below = 0;
        for (int v = 0; v < vertices.Length; v += stride)
        {
            float height = Vector3.Dot(vertices[v], axis);
            var flat = vertices[v] - (axis * height);

            float bestSq = float.MaxValue;
            int nearest = 0;
            for (int i = 0; i < flatLoop.Length; i++)
            {
                float dSq = Vector3.DistanceSquared(flat, flatLoop[i]);
                if (dSq >= bestSq) continue;
                bestSq = dSq;
                nearest = i;
            }

            if (height >= loopHeight[nearest]) above++;
            else below++;
        }

        return above >= below;
    }
}

/// <summary>Which of the two ways of finding a parting line produced it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PartingLineSource
{
    /// <summary>
    /// The border of the extrusion the body was made by, found from its wall thickness and needing
    /// no pull direction at all. Answers "where does this body's own edge run". See
    /// <see cref="ThicknessParting"/>.
    ///
    /// <para>
    /// Deliberately the zero value as well as the declared default, so a recipe that arrives without
    /// a usable source - absent from an older save file, or defaulted by anything that builds the
    /// record without setting it - lands here rather than somewhere it was never asked to go.
    /// </para>
    /// </summary>
    ExtrusionBorder,

    /// <summary>
    /// The silhouette of the body for <see cref="PartingLineParameters.PullDirection"/> - where the
    /// surface turns away from the pull. Answers "where can this be pulled apart along that axis".
    /// </summary>
    Silhouette,
}

/// <summary>
/// Everything needed to rebuild a parting line from the mesh it was traced on. Held as parameters
/// rather than as a baked <see cref="PartingLine"/> so a split can be replayed from the recipe.
/// </summary>
public sealed record PartingLineParameters
{
    /// <summary>
    /// How the line is found.
    ///
    /// <para>
    /// The <em>record</em> default is the silhouette, while the view opens on the border. That split
    /// is deliberate. This default is what an unspecified recipe gets - an older save file with no
    /// source recorded, or any caller that does not set one - and those want the behaviour that
    /// existed before the border did, reproduced exactly. The border also cannot serve as a blanket
    /// default: it refuses a body that is not a surface given thickness, which is correct but leaves
    /// a sphere or a torus with no line at all.
    /// </para>
    ///
    /// <para>
    /// New work still gets the border, because the view sets it explicitly and records it in the
    /// recipe. It is paired there with <see cref="PartingMeshAxisSource.PartingLine"/>, which is what
    /// carries "no pull direction" through the rest of the build.
    /// </para>
    /// </summary>
    public PartingLineSource Source { get; init; } = PartingLineSource.Silhouette;

    /// <summary>Settings for the border trace; ignored unless <see cref="Source"/> selects it.</summary>
    public ThicknessPartingOptions ThicknessOptions { get; init; } = ThicknessPartingOptions.Default;

    /// <summary>
    /// The direction the two halves are pulled apart along. Still meaningful when
    /// <see cref="Source"/> is <see cref="PartingLineSource.ExtrusionBorder"/> - that ignores it for
    /// tracing, but the flange is still built and extruded along it.
    /// </summary>
    public Vector3 PullDirection { get; init; } = Vector3.UnitY;

    public PartingLineSmoothingOptions SmoothingOptions { get; init; } = PartingLineSmoothingOptions.Default;

    public PartingLineFilterOptions FilterOptions { get; init; } = PartingLineFilterOptions.Default;

    public PartingLinePinchOptions PinchOptions { get; init; } = PartingLinePinchOptions.Default;

    public float NoiseThreshold { get; init; } = 0.1f;

    /// <summary>
    /// The draft-neutral band, as the range slider in the parting view sets it. See
    /// <see cref="PartingNeutralBand"/> - this is the same band the view shades on the model, and
    /// until now it was only ever used for that shading.
    /// </summary>
    public PartingNeutralBand NeutralBand { get; init; } = PartingNeutralBand.Default;

    public static PartingLineParameters Default { get; } = new();
}

/// <summary>How the flange surface is turned into the solid the mould is cut with.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PartingMeshThickening
{
    /// <summary>
    /// Copy the surface to two sheets and wall the gap between them.
    ///
    /// <para>
    /// Exact and instant, and it preserves whatever state the surface is in - including, precisely,
    /// its self-intersections, which it then doubles. The zero value and the record default so an
    /// older recipe rebuilds the cutter it was committed with.
    /// </para>
    /// </summary>
    Extrude,

    /// <summary>
    /// Offset the surface: sample a distance field around it on a voxel grid and re-extract the
    /// surface at the offset distance.
    ///
    /// <para>
    /// A distance field has no memory of the input having crossed itself, so what comes back is a
    /// clean watertight solid whatever went in - measured across chin, scalp and larynx, zero
    /// self-intersections and watertight in every case, where extruding the same surfaces gave
    /// hundreds. That is what makes the cut work: chin and scalp fall into two near-equal halves.
    /// </para>
    ///
    /// <para>
    /// It costs resolution and triangles. The grid has to resolve the thickness, and the cost is
    /// cubic in body size over voxel size, so the cutter has to be millimetres thick rather than
    /// tenths - which is affordable only because the thickness is the gap, and a gap of a millimetre
    /// or two is what a mould wants anyway.
    /// </para>
    /// </summary>
    Offset,
}

/// <summary>Which of the two ways of dividing the mould with the parting mesh is used.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PartingSplitMethod
{
    /// <summary>
    /// Subtract a thin cutter, then find the pieces: separate the result into connected components
    /// and decide which half each belongs to by sampling its vertices against the parting line.
    ///
    /// <para>
    /// Two things can go wrong and both are common. The subtraction may not sever - a cutter that is
    /// thin, or that does not span the whole cross-section, leaves the mould in one piece - and the
    /// side test is a majority vote, which has been seen to put both pieces on the same side. Every
    /// route out of either reports <see cref="MeshErrors.SplitProducedSinglePiece"/>.
    /// </para>
    ///
    /// <para>The zero value and the default, so an older recipe replays as it was committed.</para>
    /// </summary>
    SeveredComponents,

    /// <summary>
    /// Take each half directly: the mould's intersection with a solid covering one side of the
    /// parting surface, and the mould's difference from it. Nothing has to sever and nothing has to
    /// be identified afterwards - the operation that produced a half is what makes it that half.
    ///
    /// <para>
    /// The gap between the halves comes from shifting the tool half a gap each way rather than from
    /// the tool's own thickness. That is not a detail: giving the two operations the same tool
    /// position makes their results share a surface, and the boolean resolves coincident input by
    /// virtually displacing vertices, which returns a valid mesh of zero volume. See MeshLib
    /// discussion 4933.
    /// </para>
    /// </summary>
    ShiftedHalfSpaces,

    /// <summary>
    /// Subtract the cutter and hand back whatever that produced, as one mesh, without checking it
    /// came apart or working out which piece is which.
    ///
    /// <para>
    /// A diagnostic rather than a way to finish a mould. Every other method reports a failure when
    /// the result is not two pieces, which tells you the split did not work but not what it did
    /// instead; this shows the geometry so the cut can be looked at. Expect one mesh, possibly still
    /// in one piece.
    /// </para>
    /// </summary>
    SubtractOnly,
}

/// <summary>
/// How the flange is swept outward from the parting line. Three builders, kept side by side so they
/// can be compared on real bodies before one is settled on.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PartingMeshSweep
{
    /// <summary>
    /// The original: the parting line is flattened into the plane perpendicular to the axis, offset
    /// outward there as 2D rings, triangulated, and lifted by giving each ring a height.
    ///
    /// <para>
    /// Every point therefore travels outward inside one global plane, which is what makes it twist on
    /// a rim that is far from planar. On scalp_bolus the rim swings 35mm out of the flattest plane
    /// there is, so where the plane happens to line up with the direction the rim faces the flange
    /// looks right, and where it does not the flange has to spiral to reconcile the two.
    /// </para>
    ///
    /// <para>The zero value and the default: a recipe without one rebuilds the way it was committed.</para>
    /// </summary>
    PlanarWavefront,

    /// <summary>
    /// As <see cref="PlanarWavefront"/>, but the flange <em>leaves</em> the parting line along the
    /// body's surface normal there before it relaxes to level. The footprint and triangulation are
    /// unchanged; only the height the first ring is given changes, from "whatever the relaxation
    /// wants" to "keep going the way the body was going".
    ///
    /// <para>
    /// Aimed squarely at the twist being visible right at the rim, which is where it reads worst. It
    /// cannot remove the twist further out, because further out the flange is still travelling in the
    /// one global plane.
    /// </para>
    /// </summary>
    TangentLaunch,

    /// <summary>
    /// No global plane at all: the flange is marched outward in 3D, each point of each ring stepping
    /// along its own outward direction, seeded from the body's surface normal at the parting line and
    /// turned gradually toward the axis plane so the outer rim still flattens and reaches past the
    /// mould.
    ///
    /// <para>
    /// This is the one that answers "go directly out" literally. It gives up what the planar sweep
    /// gets for free - a footprint that provably does not cross itself, from 2D offsetting - so it
    /// can fold where the parting line is sharply concave.
    /// </para>
    /// </summary>
    SurfaceSweep,

    /// <summary>
    /// Lofts the parting line out to a ring on the mould, the ring taking its shape from the mould's
    /// outline and its height from the line at the same bearing.
    ///
    /// <para>
    /// The other two build the flange from the body alone and never consult the mould, so the body's
    /// undulation reaches the outer wall and the mating face carries it. This asks the mould where the
    /// surface should come out, which is the one part of the answer the body has no claim on. Matching
    /// the ring's height to the line's leaves every radial of the loft level, so the climb that made
    /// the steep faces is not there to be made.
    /// </para>
    /// </summary>
    MouldLoft,
}

/// <summary>Which of the two ways of choosing the parting mesh's axis is in force.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PartingMeshAxisSource
{
    /// <summary>
    /// The direction the user picked, taken straight from <see cref="PartingMeshParameters.Axis"/>.
    /// Pairs with <see cref="PartingLineSource.Silhouette"/>, which traces the line for that same
    /// direction: the line marks where the mould comes apart along it, and the flange sweeps in the
    /// plane perpendicular to it, so the two describe one pull.
    ///
    /// <para>
    /// The zero value and the declared default, so a recipe that arrives without one - an older save
    /// file, a caller that never set it - keeps the behaviour that existed before the alternative did.
    /// </para>
    /// </summary>
    PullDirection,

    /// <summary>
    /// The parting line's own best-fit plane normal, so the pull direction is not consulted at all.
    /// Pairs with <see cref="PartingLineSource.ExtrusionBorder"/> and completes it: the border line
    /// is traced from the body's wall thickness with no direction in it, and this builds the mesh
    /// around that line on the axis the line itself implies. Together they make a parting that asks
    /// the user for no direction anywhere.
    ///
    /// <para>
    /// This is also what makes a border line usable at all on a body whose border wraps well away
    /// from the gizmo direction. The flange is swept in the plane perpendicular to the axis, so an
    /// axis the line runs edge-on to leaves the footprint folded over itself and the flange sweeping
    /// a shape the line was never traced in. Fitting the axis to the line is what rules that out.
    /// </para>
    /// </summary>
    PartingLine,
}

/// <summary>
/// Everything needed to rebuild the parting mesh from a parting line. Paired with
/// <see cref="PartingLineParameters"/> these fully describe a split, which is what
/// <see cref="SplitCommand"/> stores so it can be replayed on import.
/// </summary>
public sealed record PartingMeshParameters
{
    /// <summary>
    /// Where <see cref="Axis"/> comes from - see <see cref="PartingMeshAxisSource"/>. Run the pair
    /// through <see cref="PartingMeshFeature.ResolveAxis"/> to turn it into a plain axis; every stage
    /// of the build reads <see cref="Axis"/> and none of them resolves this for itself.
    /// </summary>
    public PartingMeshAxisSource AxisSource { get; init; } = PartingMeshAxisSource.PullDirection;

    /// <summary>How the flange is swept outward from the line - see <see cref="PartingMeshSweep"/>.</summary>
    public PartingMeshSweep Sweep { get; init; } = PartingMeshSweep.PlanarWavefront;

    /// <summary>How the mould is divided with it - see <see cref="PartingSplitMethod"/>.</summary>
    public PartingSplitMethod SplitMethod { get; init; } = PartingSplitMethod.SeveredComponents;

    /// <summary>How the flange is made solid - see <see cref="PartingMeshThickening"/>.</summary>
    public PartingMeshThickening Thickening { get; init; } = PartingMeshThickening.Extrude;

    /// <summary>
    /// Grid resolution for the offset passes, in mm.
    ///
    /// <para>
    /// An absolute size, not a fraction of <see cref="Depth"/>. Tying it to the depth coupled two
    /// things that have no reason to move together: how thick the cutter is, and how finely the
    /// rounding needs to resolve it. A 2mm offset does not need sub-millimetre cells, and scaling
    /// them down with the depth is what made a tenth-of-a-millimetre cutter ask for nine billion of
    /// them and a 2mm one on scalp still exceed its budget.
    /// </para>
    ///
    /// <para>
    /// 1mm keeps every body measured comfortably inside the budget - a head-sized mould is a couple
    /// of million cells - and is fine enough for a cutter measured in millimetres.
    /// </para>
    /// </summary>
    public float OffsetVoxelSizeMm { get; init; } = 1.0f;

    /// <summary>
    /// Axis the flange is built and extruded along, and the axis the halves separate along. The
    /// flange offsets its wavefront ribbons outward in the plane perpendicular to this, and the lift
    /// treats the component along it as height, so this is the direction the whole parting mesh is
    /// shaped around.
    ///
    /// <para>
    /// It has to agree with the parting line, or the ribbons sweep a footprint the line was never
    /// traced in. What "agree" means depends on <see cref="AxisSource"/>: under
    /// <see cref="PartingMeshAxisSource.PullDirection"/> this must be the same direction as
    /// <see cref="PartingLineParameters.PullDirection"/>, since the line is that direction's
    /// silhouette; under <see cref="PartingMeshAxisSource.PartingLine"/> the agreement is arranged
    /// by fitting the axis to the line, and whatever is set here is ignored but for its sign.
    /// </para>
    ///
    /// <para>Defaults to world up only because that is where the direction gizmo starts.</para>
    /// </summary>
    public Vector3 Axis { get; init; } = Vector3.UnitY;

    /// <summary>Margin, in mm, between the mould bounds and the outer contour the flange sweeps to.</summary>
    public float OuterContourMargin { get; init; } = 10f;

    /// <summary>How far past the mould the flange surface is swept per ring, in mm.</summary>
    public float StepDistanceMm { get; init; } = 7.5f;

    /// <summary>
    /// Builds the flange with every pass that runs over it afterwards switched off: the height
    /// relaxation, the overhang relaxation, and the inner-rim seal. What comes back is the shape the
    /// sweep itself produced.
    ///
    /// <para>
    /// A diagnostic, and not safe to cut with. The seal goes with the rest, so the inner rim is placed
    /// by footprint arithmetic alone and may sit outside the body - which is a bridge of mould material
    /// the cut leaves behind. Use it to tell whether the flange's shape came from the sweep or from
    /// something repairing it, then turn it back off.
    /// </para>
    /// </summary>
    public bool RawFlange { get; init; }

    /// <summary>
    /// How many times the launch slopes are averaged around the parting line before the flange is
    /// lifted onto them - see the note in <c>LiftWavefrontToWorldSpace</c>.
    ///
    /// <para>
    /// Zero is the record default, which is the behaviour every saved recipe was committed with. It is
    /// also what corrugates: each point's slope comes from its own normal, neighbours disagree by 9
    /// degrees on average and 40 at worst, and holding the normals across the whole flange pins that
    /// difference into the surface. Averaging around the loop takes it out without touching the
    /// radial slope, which is the direction that actually follows the normals.
    /// </para>
    /// </summary>
    public int NormalSmoothingPasses { get; init; }

    /// <summary>
    /// Surface slope, in degrees from level, that the overhang relaxation eases the flange back down
    /// to. Set a few degrees under the 45-degree support-free print limit by default.
    ///
    /// <para>
    /// It is also the ceiling on how steeply the flange may leave the parting line, so it fights the
    /// launch wherever the body's normals are steeper than it - and they routinely are: scalp's want
    /// 48 degrees against this 40. Raising it lets the flange follow those normals at the cost of
    /// faces the printer will need support under, which is a real trade and why it is a parameter
    /// rather than a raised constant.
    /// </para>
    /// </summary>
    public float FlangeMaxSlopeDeg { get; init; } = 40f;

    /// <summary>
    /// How far out from the parting line, in mm, the flange keeps going the way the body's surface
    /// normal was going before it levels off. Only consulted under
    /// <see cref="PartingMeshSweep.TangentLaunch"/>.
    ///
    /// <para>
    /// 15mm is what this was fixed at, and at 15mm it does effectively nothing: measured, the launch
    /// shifted the flange 0.12mm on average where the rim itself swings 35mm, so tangent-launch and
    /// the plain planar sweep produced the same triangle count and the same halves to a tenth of a
    /// percent. It is the record default because that is the behaviour any saved recipe was committed
    /// with; the view asks for more.
    /// </para>
    /// </summary>
    public float NormalFollowMm { get; init; } = 15.0f;

    /// <summary>The default wall thickness, in mm. Exposed so a UI can seed its control from it.</summary>
    public const float DefaultDepth = 0.1f;

    /// <summary>
    /// Wall thickness of the extruded parting mesh, in mm. Also the width of the gap left between the
    /// two halves, since the mesh is subtracted from the mould.
    /// </summary>
    public float Depth { get; init; } = DefaultDepth;

    public static PartingMeshParameters Default { get; } = new();
}
