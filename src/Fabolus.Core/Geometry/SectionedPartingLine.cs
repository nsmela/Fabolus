using System.Numerics;

namespace Fabolus.Core.Geometry;

/// <summary>Why an anchor is where it is, which decides how freely it may be moved or removed.</summary>
public enum PartingAnchorOrigin
{
    /// <summary>Placed by the analysis, at the boundary between two conditions.</summary>
    Section,

    /// <summary>Placed or moved by the user, and never overwritten by a re-analysis.</summary>
    User,
}

/// <summary>
/// One handle on the parting line: a point pinned to the rim wall that the spans either side of it run
/// between.
/// </summary>
public sealed record PartingAnchor(Vector3 Position, PartingAnchorOrigin Origin)
{
    public bool IsUserPlaced => Origin == PartingAnchorOrigin.User;
}

/// <summary>
/// One stretch of the line between two anchors, with what the analysis made of it.
/// </summary>
/// <param name="Condition">
/// What was wrong with this stretch when it was last read, which is what a view colours it by. Carried
/// on the span rather than recomputed at draw time so the picture and the report cannot disagree.
/// </param>
/// <param name="IsRetraced">
/// Whether this span was walked across the wall rather than carried over from the line as it arrived.
/// A retraced span follows the wall by construction; an original one is whatever the automatic pass
/// produced, and may be the thing the user is about to correct.
/// </param>
public sealed record PartingSpan(
    IReadOnlyList<Vector3> Points, PartingLineCondition Condition, bool IsRetraced)
{
    public float Length
    {
        get
        {
            float total = 0f;
            for (int i = 1; i < Points.Count; i++) total += Vector3.Distance(Points[i - 1], Points[i]);
            return total;
        }
    }
}

/// <summary>
/// A parting line as a ring of anchors with a traced stretch between each neighbouring pair - the form
/// the line has to be in before a user can edit it.
///
/// <para>
/// A parting line as computed is a few hundred points, and there is nothing in that a person can take
/// hold of. Cutting it at the boundaries the analysis already finds turns it into a handful of named
/// stretches with a handle at each join, and every edit the user wants becomes one of three things done
/// to that structure: move a handle, drop a handle, add a handle. Each of those is answered by
/// re-walking the one or two stretches it touches, so an edit is local and the rest of the line does
/// not move under it.
/// </para>
///
/// <para>
/// Every anchor is pinned to the rim wall and every span is walked across it, so no sequence of edits
/// can produce a line that is not on the wall. That is the guarantee worth having here: the automatic
/// passes can be argued with, but a hand-edited line that has wandered off the band is not a parting
/// line at all, and it would not be obvious on screen until the mould failed to seal.
/// </para>
/// </summary>
public sealed record SectionedPartingLine(
    IReadOnlyList<PartingAnchor> Anchors, IReadOnlyList<PartingSpan> Spans)
{
    public static SectionedPartingLine Empty { get; } =
        new(Array.Empty<PartingAnchor>(), Array.Empty<PartingSpan>());

    /// <summary>The whole line as one closed loop, which is what everything downstream still wants.</summary>
    public IReadOnlyList<Vector3> Flatten()
    {
        var points = new List<Vector3>();
        foreach (var span in Spans)
        {
            // The last point of each span is the first of the next, so it is dropped here rather than
            // repeated - a duplicated point is a zero-length segment, and those turn up later as a
            // division by zero in anything that normalizes a direction along the loop.
            for (int i = 0; i < span.Points.Count - 1; i++) points.Add(span.Points[i]);
        }

        return points;
    }

    public int SpanCount => Spans.Count;
}

/// <summary>One rim of an editable parting line: its sections, and the wall they are confined to.</summary>
public sealed record PartingRimEdit(SectionedPartingLine Line, PartingBandGraph Graph);

/// <summary>
/// A whole parting line in editable form - one entry per rim, because a body with a hole through it
/// parts along more than one and each has its own wall to be confined to.
/// </summary>
public sealed record PartingLineEdit(IReadOnlyList<PartingRimEdit> Rims)
{
    public static PartingLineEdit Empty { get; } = new(Array.Empty<PartingRimEdit>());

    public bool IsEmpty => Rims.Count == 0;

    /// <summary>The edited line in the form everything downstream still takes.</summary>
    public PartingLine ToPartingLine() =>
        new(Rims.Select(r => r.Line.Flatten()).Where(l => l.Count >= 3).ToList());

    /// <summary>The same with one rim replaced, which is what every edit produces.</summary>
    public PartingLineEdit With(int rim, SectionedPartingLine line)
    {
        if (rim < 0 || rim >= Rims.Count) return this;

        var rims = Rims.ToArray();
        rims[rim] = rims[rim] with { Line = line };
        return new PartingLineEdit(rims);
    }
}

/// <summary>
/// Where a handle would land if one were added at <paramref name="At"/>, and what it would divide.
///
/// <para>
/// A type of its own because the decision and the act are made in different places: the view works out
/// where a click would land in order to show the user a marker there, and hands this back when the
/// click comes so the handle appears exactly where the marker was. Passing the click point instead
/// would have the placement decided twice.
/// </para>
/// </summary>
/// <param name="Rim">Which rim, for a body that parts along more than one.</param>
/// <param name="Span">The section that would be divided in two.</param>
/// <param name="Point">Which of that section's samples would become the new handle.</param>
/// <param name="At">Where that sample is, which is where the handle appears.</param>
public readonly record struct PartingInsertion(int Rim, int Span, int Point, Vector3 At);

/// <summary>
/// Cuts a computed parting line into editable sections, and answers the edits a user makes to them.
///
/// <para>
/// The operations are deliberately few. A user working on a parting line wants to say three things -
/// "not there, here", "stop treating this as a separate stretch", and "let me take hold of this bit" -
/// and those are <see cref="Move"/>, <see cref="Remove"/> and <see cref="Insert"/>. Everything else a
/// mould needs is either automatic or is one of those repeated.
/// </para>
/// </summary>
public static class PartingLineEditor
{
    /// <summary>
    /// Cuts a line at the boundaries between conditions, so each stretch the analysis named becomes a
    /// section the user can take hold of.
    /// </summary>
    /// <param name="shortestSection">
    /// How short a stretch may be, in samples, before it is folded into its neighbour instead of
    /// getting a handle of its own. Without it a single kinked sample becomes its own section with two
    /// handles a millimetre apart, which is unusable as a control however correct it is as a diagnosis.
    /// </param>
    /// <param name="longestSection">
    /// How long a stretch may run, in millimetres, before it is divided again regardless of what the
    /// analysis made of it. Nothing to do with diagnosis and everything to do with control: on
    /// <c>standard</c> the analysis finds three sound stretches and one detour, and one of the sound
    /// ones is three quarters of the rim - correct as a reading, useless as a handle. Zero switches the
    /// extra divisions off and leaves only the boundaries the analysis found.
    /// </param>
    public static SectionedPartingLine Seed(
        IReadOnlyList<Vector3> loop, PartingBand band,
        PartingLineSectionOptions? options = null, int shortestSection = 6,
        float longestSection = 45f)
    {
        var report = PartingLineSections.Analyse(loop, band, options);
        if (report.Samples.Count == 0 || loop.Count < 8) return SectionedPartingLine.Empty;

        // Sections too short to hold a handle are absorbed by whichever neighbour follows them, so the
        // boundaries that survive are the ones a user could actually aim at.
        var kept = new List<PartingLineSection>();
        foreach (var section in report.Sections)
        {
            if (kept.Count > 0 && section.Count < shortestSection)
            {
                var last = kept[^1];
                kept[^1] = last with { Count = last.Count + section.Count };
                continue;
            }

            kept.Add(section);
        }

        if (kept.Count > 1 && kept[^1].Count < shortestSection)
        {
            var last = kept[^1];
            kept.RemoveAt(kept.Count - 1);
            kept[0] = kept[0] with { Start = last.Start, Count = kept[0].Count + last.Count };
        }

        if (kept.Count == 0) return SectionedPartingLine.Empty;

        var anchors = new List<PartingAnchor>(kept.Count);
        var spans = new List<PartingSpan>(kept.Count);

        foreach (var section in kept)
        {
            anchors.Add(new PartingAnchor(loop[section.Start], PartingAnchorOrigin.Section));

            var points = new List<Vector3>(section.Count + 1);
            for (int k = 0; k <= section.Count; k++) points.Add(loop[(section.Start + k) % loop.Count]);

            spans.Add(new PartingSpan(points, section.Condition, IsRetraced: false));
        }

        var seeded = new SectionedPartingLine(anchors, spans);
        return longestSection <= 0f ? seeded : Divide(seeded, longestSection);
    }

    /// <summary>
    /// Divides any span longer than <paramref name="longest"/> into equal parts, so no stretch of the
    /// line is too long to take hold of. Cut out of the existing points rather than re-walked - see
    /// <see cref="Insert"/> for why adding a handle must never move the line.
    /// </summary>
    private static SectionedPartingLine Divide(SectionedPartingLine line, float longest)
    {
        var anchors = new List<PartingAnchor>(line.Anchors.Count);
        var spans = new List<PartingSpan>(line.Spans.Count);

        for (int s = 0; s < line.Spans.Count; s++)
        {
            var span = line.Spans[s];
            anchors.Add(line.Anchors[s]);

            int parts = (int)MathF.Ceiling(span.Length / longest);
            if (parts <= 1 || span.Points.Count < parts * 2)
            {
                spans.Add(span);
                continue;
            }

            // Cut by point count rather than by arc length: the line is resampled to an even spacing
            // upstream, so the two agree, and counting points cannot land a handle between samples.
            int stride = span.Points.Count / parts;
            for (int p = 0; p < parts; p++)
            {
                int from = p * stride;
                int to = p == parts - 1 ? span.Points.Count - 1 : (p + 1) * stride;

                var points = new List<Vector3>(to - from + 1);
                for (int i = from; i <= to; i++) points.Add(span.Points[i]);

                if (p > 0)
                    anchors.Add(new PartingAnchor(span.Points[from], PartingAnchorOrigin.Section));

                spans.Add(span with { Points = points });
            }
        }

        return new SectionedPartingLine(anchors, spans);
    }

    /// <summary>
    /// Moves one handle and re-walks only the two stretches that meet at it.
    /// </summary>
    /// <param name="to">
    /// Where the user put it, in world space. Pinned to the wall before anything else happens - see
    /// <see cref="PartingBandGraph.Snap"/> for why a handle dragged off the wall is taken as meaning
    /// the nearest place on it rather than as an instruction to leave.
    /// </param>
    /// <param name="geodesic">
    /// Re-walks the two spans as shortest paths across the whole body rather than across the band.
    ///
    /// <para>
    /// A real choice rather than a better option, and it is worth being exact about what changes. The
    /// band walk is confined twice over: to the rim wall, and to the arc of the rim between the two
    /// handles. Both confinements exist because a parting line is not merely a curve on the body - it
    /// is the curve the two halves meet along, so a stretch that strays onto a shell is not a worse
    /// line, and a stretch traced the other way round the rim is not a longer line. They are lines that
    /// part the mould somewhere other than where it is drawn. An unconstrained geodesic is free to do
    /// both, and will wherever doing so is shorter: it cuts the corner over a crease, and between two
    /// handles well apart on a closed rim it takes the short way.
    /// </para>
    ///
    /// <para>
    /// What it buys is a path that owes nothing to the band's own triangulation. The band walk can only
    /// route through faces the band mask happens to include, so where the mask narrows to a face or two
    /// the corridor narrows with it and the taut path has nowhere to go. Null uses the band walk.
    /// </para>
    /// </param>
    public static SectionedPartingLine Move(
        SectionedPartingLine line, int anchor, Vector3 to, PartingBandGraph graph,
        ISurfaceGeodesic? geodesic = null)
    {
        if (line.Anchors.Count == 0) return line;

        int n = line.Anchors.Count;
        anchor = ((anchor % n) + n) % n;

        var anchors = line.Anchors.ToArray();
        anchors[anchor] = new PartingAnchor(graph.Snap(to), PartingAnchorOrigin.User);

        var spans = line.Spans.ToArray();

        // The span leaving this anchor, and the one arriving at it. Both, because a handle is the join
        // between two stretches and moving it changes where each of them ends.
        bool leaving = Retrace(spans, anchors, anchor, graph, geodesic);
        bool arriving = Retrace(spans, anchors, ((anchor - 1) % n + n) % n, graph, geodesic);

        // Neither span could be re-walked, so the handle has moved somewhere the wall cannot be crossed
        // to. Committing it anyway leaves both stretches still ending where the handle used to be - a
        // line with a step in it at the join, and a Flatten that no longer passes through the handle at
        // all. The drag is refused instead, which reads as the handle declining to follow the cursor
        // rather than as the line coming apart behind it.
        if (!leaving && !arriving) return line;

        return new SectionedPartingLine(anchors, spans);
    }

    /// <summary>
    /// Drops one handle, merging the two stretches that met at it into one and re-walking it.
    ///
    /// <para>
    /// This is how a user says the analysis over-divided the line: two sections that want to be one
    /// stretch become one, and the walk that replaces them takes the wall's own shortest route rather
    /// than keeping the detour through where the handle used to be.
    /// </para>
    /// </summary>
    /// <inheritdoc cref="Move" path="/param[@name='geodesic']"/>
    public static SectionedPartingLine Remove(
        SectionedPartingLine line, int anchor, PartingBandGraph graph,
        ISurfaceGeodesic? geodesic = null)
    {
        int n = line.Anchors.Count;

        // Two anchors is the fewest that can describe a closed line; below that there is nothing left
        // to walk between and the line would collapse to a point.
        if (n <= 2) return line;

        anchor = ((anchor % n) + n) % n;

        var anchors = line.Anchors.ToList();
        var spans = line.Spans.ToList();

        int previous = ((anchor - 1) % n + n) % n;

        anchors.RemoveAt(anchor);
        spans.RemoveAt(anchor);

        var kept = anchors.ToArray();
        var keptSpans = spans.ToArray();

        // Same refusal as Move makes, for the same reason. The merged stretch is the only thing joining
        // the two handles either side of the one being dropped, so if the wall cannot be crossed between
        // them the span left in its place still ends where the dropped handle was - a gap in the line
        // rather than a merge. Better to keep the handle than to break the ring.
        if (!Retrace(keptSpans, kept, anchor > previous ? previous : previous - 1, graph, geodesic))
            return line;

        return new SectionedPartingLine(kept, keptSpans);
    }

    /// <summary>
    /// Re-walks every span between the handles it already has, leaving the handles where they are.
    ///
    /// <para>
    /// What this is for is changing how spans are walked rather than where they run - switching between
    /// the band walk and an unconstrained geodesic, which otherwise only takes effect on the next span a
    /// user happens to drag, so the line on screen would be half one and half the other.
    /// </para>
    /// </summary>
    /// <inheritdoc cref="Move" path="/param[@name='geodesic']"/>
    public static SectionedPartingLine Retrace(
        SectionedPartingLine line, PartingBandGraph graph, ISurfaceGeodesic? geodesic = null)
    {
        if (line is null || line.Anchors.Count == 0 || line.Spans.Count != line.Anchors.Count)
            return line!;

        var anchors = line.Anchors.ToArray();
        var spans = line.Spans.ToArray();

        // In order, so each span's own direction round the rim is read from its existing points before
        // those are replaced.
        for (int i = 0; i < anchors.Length; i++) Retrace(spans, anchors, i, graph, geodesic);

        return new SectionedPartingLine(anchors, spans);
    }

    /// <summary>
    /// Adds a handle on the line, splitting the stretch it lands in.
    ///
    /// <para>
    /// The new stretches are cut out of the existing one rather than re-walked, so adding a handle never
    /// moves the line. That matters more than it sounds: the user is adding a handle in order to take
    /// hold of a stretch they are happy with the position of, and a line that shifted the moment they
    /// reached for it would be unusable.
    /// </para>
    /// </summary>
    public static SectionedPartingLine Insert(
        SectionedPartingLine line, Vector3 at, PartingBandGraph graph)
    {
        if (line is null || line.Spans.Count == 0) return line!;

        return TryPlace(line, graph.Snap(at), graph.Band.Span, onlySpan: -1, out int span, out int point)
            ? Insert(line, span, point)
            : line;
    }

    /// <summary>Applies a placement <see cref="TryPlan"/> already worked out.</summary>
    /// <returns>
    /// The line, and the index of the handle now sitting at that spot - or the line unchanged and -1
    /// where the placement no longer names anything.
    ///
    /// <para>
    /// Checked rather than trusted because a placement outlives the line it was worked out against: it
    /// is planned when the cursor moves and applied when the button goes down, and an edit in between
    /// renumbers the sections under it. A stale one used to index <see cref="SectionedPartingLine.Spans"/>
    /// straight out of range.
    /// </para>
    /// </returns>
    public static (SectionedPartingLine Line, int Anchor) Insert(
        SectionedPartingLine line, PartingInsertion insertion)
    {
        if (line is null || insertion.Span < 0 || insertion.Span >= line.Spans.Count)
            return (line!, -1);

        var span = line.Spans[insertion.Span];

        // The ends are the anchors either side, so a handle placed on one of them divides nothing and
        // leaves a span with no length - the same refusal TryPlace makes when it plans the placement.
        if (insertion.Point <= 0 || insertion.Point >= span.Points.Count - 1) return (line, -1);

        return (Insert(line, insertion.Span, insertion.Point), insertion.Span + 1);
    }

    private static SectionedPartingLine Insert(SectionedPartingLine line, int at, int point)
    {
        var span = line.Spans[at];

        var anchors = line.Anchors.ToList();
        var spans = line.Spans.ToList();

        var head = span.Points.Take(point + 1).ToList();
        var tail = span.Points.Skip(point).ToList();

        spans[at] = span with { Points = head };
        spans.Insert(at + 1, span with { Points = tail });
        anchors.Insert(at + 1, new PartingAnchor(span.Points[point], PartingAnchorOrigin.User));

        return new SectionedPartingLine(anchors, spans);
    }

    /// <summary>
    /// Works out where adding a handle at <paramref name="at"/> would put one, without adding it.
    ///
    /// <para>
    /// Split out from <see cref="Insert"/> so the view can show the user where their click will land
    /// before they make it. The two have to agree exactly or the preview is a lie, and the only way to
    /// be sure of that is for both to be this - a click that added a handle a few millimetres from the
    /// marker under the cursor would be worse than no marker.
    /// </para>
    ///
    /// <para>
    /// It answers false near a handle that already exists, which is the other half of what the preview
    /// is for: the refusal is not a failure, it is the editor saying there is nothing to divide there,
    /// and with a marker to watch that reads as the marker going away rather than as a click doing
    /// nothing.
    /// </para>
    /// </summary>
    /// <param name="rim">Which rim the section belongs to.</param>
    /// <param name="span">
    /// The section to divide - the one the user is pointing at, not whichever happens to be nearest.
    /// Restricting it to that one is what makes the answer predictable: it is the section under the
    /// cursor and highlighted on screen that divides, so the marker never jumps to a neighbour because
    /// the cursor drifted a millimetre past a handle.
    /// </param>
    public static bool TryPlan(
        PartingLineEdit? edit, int rim, int span, Vector3 at, out PartingInsertion insertion)
    {
        insertion = default;
        if (edit is null || rim < 0 || rim >= edit.Rims.Count) return false;

        var target = edit.Rims[rim];
        if (span < 0 || span >= target.Line.Spans.Count) return false;

        var pinned = target.Graph.Snap(at);
        if (!TryPlace(target.Line, pinned, target.Graph.Band.Span, span, out _, out int point))
            return false;

        insertion = new PartingInsertion(rim, span, point, target.Line.Spans[span].Points[point]);
        return true;
    }

    /// <summary>
    /// Which sample of which section a point lands on, or false if none of them can be divided there.
    /// </summary>
    /// <param name="wallSpan">
    /// How wide the rim wall is, which is what the room a new handle needs is measured against. A split
    /// at the very end of a section leaves a stretch with no length - a handle that cannot be grasped
    /// and a span that cannot be walked - and a split just inside one leaves two handles close enough to
    /// be a single target, which is unusable as a control however correct it is as a division. The wall
    /// is the right scale for that because it is also what the handles are drawn at: a cube is a fifth
    /// of the wall across, so a wall's width apart is five cube widths, which reads as two handles.
    /// </param>
    /// <param name="onlySpan">The section to divide, or -1 to take whichever is nearest.</param>
    private static bool TryPlace(
        SectionedPartingLine line, Vector3 pinned, float wallSpan, int onlySpan,
        out int span, out int point)
    {
        span = 0;
        point = 0;

        float best = float.MaxValue;
        float bestReach = 0f;
        bool found = false;

        for (int s = 0; s < line.Spans.Count; s++)
        {
            if (onlySpan >= 0 && s != onlySpan) continue;

            var points = line.Spans[s].Points;
            if (points.Count < 3) continue;

            // Never more than a sixth of the section's own length, so a section shorter than the wall is
            // wide still has a middle that can be divided. Without that, the guard would refuse a short
            // section outright - and a section is short exactly when a user has already divided it once
            // and wants to again.
            float room = MathF.Min(wallSpan, line.Spans[s].Length / 6f);

            for (int i = 1; i < points.Count - 1; i++)
            {
                float d = Vector3.DistanceSquared(points[i], pinned);
                if (d >= best) continue;

                if (Vector3.Distance(points[i], points[0]) < room) continue;
                if (Vector3.Distance(points[i], points[^1]) < room) continue;

                best = d;
                bestReach = room;
                span = s;
                point = i;
                found = true;
            }
        }

        // And no further from where the user pointed than two handles have to be apart anyway. Without
        // this the guard above only stops handles stacking, it does not stop the answer sliding: point
        // at a handle that already exists and the nearest spot with room for another is a wall's width
        // along the section, so the marker would leave the cursor and the click would divide somewhere
        // the user was not looking. The same number does both jobs, which is the point - it never binds
        // where there is room, because there the nearest sample is a fraction of a millimetre away.
        return found && best <= bestReach * bestReach;
    }

    /// <summary>
    /// Eases the line along its own length while holding every point on the wall.
    ///
    /// <para>
    /// Wanted because the geodesic retrace only reaches the spans an edit touched. Everything else is
    /// still the automatic trace - an isoline stepped across the band face by face, which arrives
    /// faceted at the scale of one triangle - and the flange is swept along it, so the faceting is
    /// carried outward into the parting surface. This is the pass that takes it out of the whole line at
    /// once rather than one drag at a time.
    /// </para>
    ///
    /// <para>
    /// Every point is put back on the wall after every pass, which is the part that makes it usable at
    /// all. Averaging alone lifts a curve off a curved surface - it moves each point toward the chord
    /// through its neighbours, and a chord is inside the surface - so an unpinned smooth walks the line
    /// into the solid and the flange is then swept from points that are not on the body. Snapping after
    /// each pass rather than only at the end keeps the drift to within one pass's worth, which is what
    /// stops the two fighting.
    /// </para>
    ///
    /// <para>
    /// The anchors do not move. A user placed them, and a smoothing pass that slid them would undo the
    /// edit it was asked to tidy up. It also gives the pass fixed ends to work between, so pressing it
    /// repeatedly converges on a line through those handles rather than shrinking toward a point.
    /// </para>
    ///
    /// <para>
    /// Averaging along the line and putting the result back on the surface is a curve-shortening flow on
    /// that surface, so what it converges to between two handles is the geodesic between them - the same
    /// curve <see cref="PartingBandGraph.WalkGeodesic"/> produces directly. The two are the same idea
    /// arriving from opposite ends, and that is why pressing this repeatedly is meaningful rather than
    /// merely more: each press moves the line further toward the piecewise geodesic through the handles,
    /// and stops there.
    /// </para>
    /// </summary>
    /// <param name="strength">
    /// How far each point moves toward the average of its neighbours per pass, in [0, 1].
    /// </param>
    /// <param name="clearanceFloor">
    /// How near a crease, as a share of the way across the wall, a point may be eased. Defaulted to the
    /// same number the diagnosis calls a stretch faulty below, deliberately: smoothing is allowed to
    /// spend clearance, since a straighter line inside a curved wall must come nearer the inside crease
    /// on every bend, but it must not spend so much that it creates the fault the analysis would then
    /// report. A point already nearer than this is not pushed further in, and is not dragged out either
    /// - this is a guard on the smoothing, not a centring pass, and
    /// <see cref="PartingLineTreatment"/> is what exists to move a line that is off centre.
    /// </param>
    public static SectionedPartingLine Smooth(
        SectionedPartingLine line, PartingBandGraph graph,
        int passes = 12, float strength = 0.5f, float clearanceFloor = -1f)
    {
        if (clearanceFloor < 0f) clearanceFloor = PartingLineSectionOptions.Default.ClearanceFloor;

        if (line is null || graph is null || line.Spans.Count == 0 || passes <= 0) return line!;

        // Smoothed as one closed ring rather than span by span. A span-at-a-time pass holds both ends of
        // every span still, which is every anchor plus nothing in between having any say - so the line
        // keeps a corner at each join, and the joins are exactly where a dragged handle puts one.
        var ring = new List<Vector3>();
        var anchored = new List<bool>();
        var lengths = new int[line.Spans.Count];

        for (int s = 0; s < line.Spans.Count; s++)
        {
            var points = line.Spans[s].Points;
            lengths[s] = points.Count;

            // The last point of each span is the first of the next, so it is added once, by the span
            // that starts there.
            for (int i = 0; i < points.Count - 1; i++)
            {
                ring.Add(points[i]);
                anchored.Add(i == 0);
            }
        }

        int n = ring.Count;
        if (n < 8) return line;

        var work = ring.ToArray();
        var buffer = new Vector3[n];
        float lambda = Math.Clamp(strength, 0f, 1f);

        // Built once for the whole pass rather than per query. The guard measures every point against
        // both creases on every pass, and a crease is a polyline of thousands of points - walked
        // outright that is a quadratic in the two biggest numbers here, and it measured 2.5 seconds on
        // scalp, which is a button press the user watches.
        var first = new CreaseIndex(graph.Band.First);
        var second = new CreaseIndex(graph.Band.Second);

        // Where each point's three lookups landed last pass - the band face it snapped to, and the
        // vertex of each crease it was nearest. A pass moves a point a fraction of an edge, so last
        // pass's answer is next door to this one, and searching from it rather than from nothing is the
        // difference between this pass costing a moment and costing a second and a half on the largest
        // body in the set.
        var onBand = new int[n];
        var onFirst = new int[n];
        var onSecond = new int[n];
        Array.Fill(onBand, -1);
        Array.Fill(onFirst, -1);
        Array.Fill(onSecond, -1);

        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = 0; i < n; i++)
            {
                if (anchored[i]) { buffer[i] = work[i]; continue; }

                var average = (work[((i - 1) % n + n) % n] + work[(i + 1) % n]) * 0.5f;
                var moved = Vector3.Lerp(work[i], average, lambda);

                buffer[i] = Pinned(
                    graph, first, second, work[i], moved, clearanceFloor,
                    ref onBand[i], ref onFirst[i], ref onSecond[i]);
            }

            (work, buffer) = (buffer, work);
        }

        // Cut back into the spans it came from, which keeps the anchor-to-span correspondence the
        // handles are indexed by - and keeps the section count, so the view patches rather than rebuilds.
        var spans = new PartingSpan[line.Spans.Count];
        int at = 0;

        for (int s = 0; s < spans.Length; s++)
        {
            var points = new List<Vector3>(lengths[s]);
            for (int i = 0; i < lengths[s]; i++) points.Add(work[(at + i) % n]);

            spans[s] = line.Spans[s] with { Points = points };
            at += lengths[s] - 1;
        }

        return new SectionedPartingLine(line.Anchors, spans);
    }

    /// <summary>
    /// Puts a smoothed point back on the wall, and refuses the move if it would take the line nearer a
    /// crease than it is allowed to be - or nearer than it already was, which is the case the floor
    /// alone does not cover: a stretch already inside the floor has to be able to sit still.
    /// </summary>
    private static Vector3 Pinned(
        PartingBandGraph graph, CreaseIndex first, CreaseIndex second,
        Vector3 was, Vector3 moved, float clearanceFloor,
        ref int onBand, ref int onFirst, ref int onSecond)
    {
        var pinned = graph.Snap(moved, ref onBand);

        float after = Clearance(first, second, pinned, ref onFirst, ref onSecond);
        if (after >= clearanceFloor) return pinned;

        // The two positions are a fraction of an edge apart, so the hints carry from one to the other.
        return after >= Clearance(first, second, was, ref onFirst, ref onSecond) ? pinned : was;
    }

    /// <summary>How far a point sits from the nearer crease, as a share of the way across the wall.</summary>
    private static float Clearance(
        CreaseIndex first, CreaseIndex second, Vector3 point,
        ref int firstHint, ref int secondHint)
    {
        var onFirst = first.Closest(point, ref firstHint);
        var onSecond = second.Closest(point, ref secondHint);

        var axis = onSecond - onFirst;
        float span = axis.LengthSquared();
        if (span < 1e-9f) return 0.5f;

        float across = Vector3.Dot(point - onFirst, axis) / span;
        return MathF.Min(across, 1f - across);
    }

    /// <summary>
    /// The nearest point on one crease, answered from a grid rather than by walking the whole contour.
    ///
    /// <para>
    /// Deliberately narrow. <see cref="PartingBand.Closest"/> is the general answer and is exact by
    /// walking every segment, which is right for the handful of queries most callers make; this is for
    /// the one caller that makes hundreds of thousands of them against a contour that does not change.
    /// It finds the nearest crease <em>vertex</em> from the grid and then measures only the segments
    /// meeting it, which is the same answer wherever a segment is short against the curve it describes
    /// - true of these contours, which are resampled to a fixed multiple of the mesh's edge length.
    /// </para>
    /// </summary>
    private sealed class CreaseIndex
    {
        private readonly RidgeContour _contour;
        private readonly IReadOnlyList<Vector3> _points;
        private readonly bool _closed;
        private readonly Dictionary<(int, int, int), List<int>> _cells = new();
        private readonly float _cell;

        public CreaseIndex(RidgeContour contour)
        {
            _contour = contour;
            _points = contour.Points;
            _closed = contour.IsClosed;

            double total = 0d;
            int spans = Spans;
            for (int i = 0; i < spans; i++)
                total += Vector3.Distance(_points[i], _points[(i + 1) % _points.Count]);

            _cell = spans == 0 ? 1f : MathF.Max((float)(total / spans) * 4f, 1e-4f);

            for (int i = 0; i < _points.Count; i++)
            {
                var key = Cell(_points[i]);
                if (!_cells.TryGetValue(key, out var bucket)) _cells[key] = bucket = new List<int>(4);
                bucket.Add(i);
            }
        }

        private int Spans => _points.Count == 0 ? 0 : _closed ? _points.Count : _points.Count - 1;

        private (int, int, int) Cell(Vector3 p) => (
            (int)MathF.Floor(p.X / _cell), (int)MathF.Floor(p.Y / _cell), (int)MathF.Floor(p.Z / _cell));

        /// <summary>
        /// <see cref="Closest(Vector3)"/> seeded with the crease vertex the last answer was near, for
        /// the same reason the band graph's snap has one: a relaxation moves its points a fraction of an
        /// edge per pass, and re-searching the grid for each of them is where the time goes.
        /// </summary>
        /// <param name="hint">In: a vertex to start from, or -1. Out: the vertex the answer is nearest.</param>
        public Vector3 Closest(Vector3 from, ref int hint)
        {
            const int Window = 6;

            if (hint >= 0 && hint < _points.Count)
            {
                int bestOffset = 0;
                float bestDistance = float.MaxValue;

                for (int k = -Window; k <= Window; k++)
                {
                    int i = hint + k;
                    if (_closed) i = ((i % _points.Count) + _points.Count) % _points.Count;
                    else if (i < 0 || i >= _points.Count) continue;

                    float d = Vector3.DistanceSquared(_points[i], from);
                    if (d >= bestDistance) continue;

                    bestDistance = d;
                    bestOffset = k;
                }

                // Accepted only when the winner is strictly inside the window, so it is flanked on both
                // sides by vertices it beat. A winner at the edge means the point has moved further than
                // the window covers, and the grid is the only thing that can say where to.
                if (MathF.Abs(bestOffset) < Window)
                {
                    int nearest = hint + bestOffset;
                    if (_closed) nearest = ((nearest % _points.Count) + _points.Count) % _points.Count;

                    hint = nearest;
                    return OnSegmentsAt(nearest, from, bestDistance);
                }
            }

            var answer = Closest(from);
            hint = Nearest(from);
            return answer;
        }

        public Vector3 Closest(Vector3 from)
        {
            if (_points.Count == 0) return from;

            int nearest = Nearest(from);
            return nearest < 0
                ? PartingBand.Closest(from, _contour).Point
                : OnSegmentsAt(nearest, from, Vector3.DistanceSquared(_points[nearest], from));
        }

        /// <summary>The crease vertex nearest a point, or -1 if the grid holds nothing within reach.</summary>
        private int Nearest(Vector3 from)
        {
            if (_points.Count == 0) return -1;

            var (cx, cy, cz) = Cell(from);

            int nearest = -1;
            float nearestDistance = float.MaxValue;
            int firstHit = -1;

            for (int radius = 1; radius <= 6; radius++)
            {
                for (int x = cx - radius; x <= cx + radius; x++)
                    for (int y = cy - radius; y <= cy + radius; y++)
                        for (int z = cz - radius; z <= cz + radius; z++)
                        {
                            if (!_cells.TryGetValue((x, y, z), out var bucket)) continue;

                            if (firstHit < 0) firstHit = radius;

                            foreach (int i in bucket)
                            {
                                float d = Vector3.DistanceSquared(_points[i], from);
                                if (d >= nearestDistance) continue;

                                nearestDistance = d;
                                nearest = i;
                            }
                        }

                if (firstHit >= 0 && radius > firstHit) break;
            }

            // Nothing within reach of the grid at all, which means the point is a long way off this
            // crease. A real answer for a wall the line has not gone near, and the caller falls back to
            // the full walk for it.
            return nearest;
        }

        /// <summary>
        /// The nearest point on the two segments meeting at <paramref name="vertex"/>. The closest point
        /// on a polyline lies on one of the segments at its closest vertex, which is what the caller has
        /// found - so this is the whole of the answer, not a refinement of it.
        /// </summary>
        private Vector3 OnSegmentsAt(int vertex, Vector3 from, float vertexDistance)
        {
            var best = _points[vertex];
            float bestDistance = vertexDistance;

            for (int step = -1; step <= 0; step++)
            {
                int i = vertex + step;
                if (!_closed && (i < 0 || i >= _points.Count - 1)) continue;

                int a = ((i % _points.Count) + _points.Count) % _points.Count;
                int b = (a + 1) % _points.Count;

                var edge = _points[b] - _points[a];
                float length = edge.LengthSquared();
                float t = length < 1e-12f
                    ? 0f
                    : Math.Clamp(Vector3.Dot(from - _points[a], edge) / length, 0f, 1f);

                var on = _points[a] + (edge * t);
                float d = Vector3.DistanceSquared(on, from);
                if (d >= bestDistance) continue;

                bestDistance = d;
                best = on;
            }

            return best;
        }
    }

    /// <summary>
    /// Re-walks the span leaving <paramref name="from"/>, keeping whatever the analysis had made of it.
    ///
    /// <para>
    /// Taken as the geodesic across the wall rather than as the walk's own chain of face centres - see
    /// <see cref="PartingBandGraph.WalkGeodesic"/>. This is the shape the user is editing: a dragged
    /// handle re-walks the two spans meeting at it on every frame, so whatever a re-walk produces is
    /// what the parting line becomes and what the flange is swept along.
    /// </para>
    /// </summary>
    /// <returns>
    /// Whether the span was re-walked. False means the wall could not be crossed between these two
    /// handles at all, and the span has been left exactly as it was - which the caller has to act on
    /// rather than ignore, since a span left behind by a handle that moved no longer meets it.
    /// </returns>
    private static bool Retrace(
        PartingSpan[] spans, PartingAnchor[] anchors, int from, PartingBandGraph graph,
        ISurfaceGeodesic? geodesic)
    {
        int n = anchors.Length;
        if (n == 0 || spans.Length != n) return false;

        from = ((from % n) + n) % n;
        int to = (from + 1) % n;

        // Which way round the rim this span runs, taken from the span itself as it stands. Assuming
        // forward is right only when the line happens to run the same way round the rim as the crease
        // it is indexed against - see PartingBandGraph.ArcForward.
        var existing = spans[from].Points;
        bool forward = existing.Count < 3
            || graph.ArcForward(
                anchors[from].Position, anchors[to].Position, existing[existing.Count / 2]);

        // The unconstrained path where one was supplied, and the band walk otherwise. It falls back to
        // the band walk rather than failing when the surface cannot join the two ends at all, since a
        // span that vanishes is worse than one traced the other way.
        var walked =
            geodesic?.Path(anchors[from].Position, anchors[to].Position)
            ?? graph.WalkGeodesic(anchors[from].Position, anchors[to].Position, forward);

        if (walked is null || walked.Count < 2) return false;

        // Anchored at both ends explicitly. A handle that has not been dragged yet sits where the trace
        // put it rather than on a gate, so the walk's own ends are the snapped versions of it - and a
        // span that does not meet its own anchors leaves a step at every join.
        //
        // Added through the duplicate guard rather than around it. Once a handle has been dragged it is
        // already snapped, so the walk's first point is that same point re-derived - equal to within
        // rounding, and prepending on top of it leaves a zero-length segment at every anchor, which is
        // what everything downstream divides by when it normalises a direction along the line.
        float settled = graph.MeanEdge * 1e-3f;

        var points = new List<Vector3>(walked.Count + 2) { anchors[from].Position };
        foreach (var point in walked)
            if (Vector3.DistanceSquared(points[^1], point) > settled * settled) points.Add(point);

        if (Vector3.DistanceSquared(points[^1], anchors[to].Position) > settled * settled)
            points.Add(anchors[to].Position);
        else
            points[^1] = anchors[to].Position;

        if (points.Count < 2) return false;

        spans[from] = spans[from] with { Points = points, IsRetraced = true };
        return true;
    }
}
