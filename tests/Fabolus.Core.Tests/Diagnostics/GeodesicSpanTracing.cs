using System.Numerics;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// What a re-walked span is shaped like, and what a smoothing pass does to a whole line.
///
/// <para>
/// The two claims worth measuring are not the same claim. A geodesic span has to be <em>smoother</em>
/// than the chain of face centres it replaces - that is the point of it - and it has to still be on the
/// wall, which is what stops a shorter path from being a worse one. A path that cut the corner over a
/// crease would win every length and turn measurement here and be useless.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class GeodesicSpanTracing
{
    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;
    private readonly ITestOutputHelper _log;

    public GeodesicSpanTracing(GeometryEngineFixture assets, ITestOutputHelper log)
    {
        _assets = assets;
        _engine = assets.Engine;
        _log = log;
    }

    /// <summary>
    /// Every span of every body re-walked both ways, and the two compared on the things that decide
    /// whether the flange swept along them is faceted: how far the path turns at each sample, and how
    /// much longer than the straight-line distance it runs.
    /// </summary>
    [Fact]
    public void AGeodesicSpanIsStraighterThanTheChainOfFaceCentres()
    {
        var feature = new PartingMeshFeature(_engine);

        _log.WriteLine(
            "                       spans   walk turn   geo turn    walk len    geo len   on wall");

        foreach (var (id, asset) in PartingLineCentringSweep.Models)
        {
            var imported = _engine.IO.Import(_assets.GetAssetPath(asset));
            var mould = MouldMesh.Create(imported.Value);
            if (mould.IsFailure) continue;

            var body = feature.GetBodyMesh(mould.Value);
            if (body.IsFailure) continue;

            var line = feature.GeneratePartingLineFromThickness(body.Value);
            if (line.IsFailure) continue;

            var edit = feature.BeginPartingLineEdit(body.Value, line.Value);
            if (edit.IsFailure) continue;

            foreach (var rim in edit.Value.Rims)
            {
                var walkTurns = new List<float>();
                var geoTurns = new List<float>();
                double walkLength = 0d, geoLength = 0d, straight = 0d;
                int spans = 0, onWall = 0, points = 0;

                var anchors = rim.Line.Anchors;
                for (int a = 0; a < anchors.Count; a++)
                {
                    var from = anchors[a].Position;
                    var to = anchors[(a + 1) % anchors.Count].Position;

                    bool forward = Forward(rim, a);
                    var walked = rim.Graph.Walk(from, to, forward);
                    var geodesic = rim.Graph.WalkGeodesic(from, to, forward);
                    if (walked is null || geodesic is null || walked.Count < 3 || geodesic.Count < 3)
                        continue;

                    spans++;
                    straight += Vector3.Distance(from, to);
                    walkLength += Length(walked);
                    geoLength += Length(geodesic);
                    walkTurns.AddRange(Turns(walked));
                    geoTurns.AddRange(Turns(geodesic));

                    // The guarantee the editing model rests on, checked on the new path rather than
                    // assumed: a point on a gate between two band faces is on the wall, and Snap
                    // leaving it where it is says so independently of how it got there.
                    foreach (var p in geodesic)
                    {
                        points++;
                        if (Vector3.Distance(p, rim.Graph.Snap(p)) < rim.Graph.MeanEdge * 0.05f) onWall++;
                    }
                }

                if (spans == 0) continue;

                float share = (float)onWall / points;
                _log.WriteLine(
                    $"  {id,-18} {spans,5}  {Mean(walkTurns),7:F1}deg {Mean(geoTurns),7:F1}deg " +
                    $"{walkLength / straight,9:F3}x {geoLength / straight,9:F3}x {share,9:P1}");

                // Straighter is the whole point, and it is a large effect rather than a marginal one -
                // a chain of face centres turns at every face by construction.
                Assert.True(Mean(geoTurns) < Mean(walkTurns),
                    $"{id}: the geodesic turns harder than the chain of face centres it replaces");

                // Shorter, because it is the shortest path through the same corridor. Never longer.
                Assert.True(geoLength <= walkLength + 1e-3,
                    $"{id}: the geodesic came back longer than the walk it was pulled taut from");

                // And still on the wall. This is what an unconstrained geodesic would not promise.
                Assert.True(share > 0.999f, $"{id}: {1f - share:P1} of the geodesic left the wall");
            }
        }
    }

    /// <summary>
    /// The band-confined geodesic against MeshLib's unconstrained one, on the same spans.
    ///
    /// <para>
    /// Reported rather than asserted, because the question it settles is a design question and not a
    /// correctness one. Both are geodesics and the unconstrained one is by definition the shorter -
    /// the interesting numbers are the two prices it pays for that: how much of it leaves the rim wall,
    /// and how often it takes the short way round a rim when the caller asked for the long way. Either
    /// one produces a line that parts the mould somewhere other than where it is drawn.
    /// </para>
    /// </summary>
    [Fact]
    public void TheUnconstrainedGeodesicIsShorterAndLeavesTheWall()
    {
        var feature = new PartingMeshFeature(_engine);

        _log.WriteLine(
            "                       spans    band len    free len   band turn   free turn   " +
            "free on wall   off by mean/worst      wall   reversed   free ms/span");

        foreach (var (id, asset) in PartingLineCentringSweep.Models)
        {
            var imported = _engine.IO.Import(_assets.GetAssetPath(asset));
            var mould = MouldMesh.Create(imported.Value);
            if (mould.IsFailure) continue;

            var body = feature.GetBodyMesh(mould.Value);
            if (body.IsFailure) continue;

            var line = feature.GeneratePartingLineFromThickness(body.Value);
            if (line.IsFailure) continue;

            var edit = feature.BeginPartingLineEdit(body.Value, line.Value);
            if (edit.IsFailure) continue;

            var made = _engine.PartingTools.CreateSurfaceGeodesic(body.Value.Mesh);
            if (made.IsFailure) { _log.WriteLine($"{id}: {made.Error.Description}"); continue; }

            using var geodesic = made.Value;

            foreach (var rim in edit.Value.Rims)
            {
                var bandTurns = new List<float>();
                var freeTurns = new List<float>();
                double bandLength = 0d, freeLength = 0d, offTotal = 0d;
                float offWorst = 0f;
                int spans = 0, onWall = 0, points = 0, reversed = 0;

                var clock = new System.Diagnostics.Stopwatch();
                var anchors = rim.Line.Anchors;

                for (int a = 0; a < anchors.Count; a++)
                {
                    var from = anchors[a].Position;
                    var to = anchors[(a + 1) % anchors.Count].Position;

                    var confined = rim.Graph.WalkGeodesic(from, to, Forward(rim, a));

                    clock.Start();
                    var free = geodesic.Path(from, to);
                    clock.Stop();

                    if (confined is null || free is null || confined.Count < 3 || free.Count < 3)
                        continue;

                    spans++;
                    bandLength += Length(confined);
                    freeLength += Length(free);
                    bandTurns.AddRange(Turns(confined));
                    freeTurns.AddRange(Turns(free));

                    // How far off the wall, not merely whether. Snap answers with the nearest point on
                    // the band, so the distance to it is how far the path has strayed onto a shell -
                    // and that is the number that decides, because a tenth of a millimetre off a band
                    // eleven millimetres wide is not the same failure as five.
                    foreach (var p in free)
                    {
                        points++;

                        float off = Vector3.Distance(p, rim.Graph.Snap(p));
                        if (off < rim.Graph.MeanEdge * 0.05f) { onWall++; continue; }

                        offTotal += off;
                        offWorst = MathF.Max(offWorst, off);
                    }

                    // Which way round the rim it went, read off the middle of each path. A span whose
                    // midpoint lands a long way from where the confined walk put its own is a span that
                    // has taken the other route, and it is that stretch of the parting line reversed.
                    var confinedMiddle = confined[confined.Count / 2];
                    var freeMiddle = free[free.Count / 2];
                    if (Vector3.Distance(confinedMiddle, freeMiddle) > Vector3.Distance(from, to))
                        reversed++;
                }

                if (spans == 0) continue;

                int strayed = points - onWall;

                _log.WriteLine(
                    $"  {id,-18} {spans,5} {bandLength,11:F1} {freeLength,11:F1} " +
                    $"{Mean(bandTurns),9:F1}deg {Mean(freeTurns),9:F1}deg " +
                    $"{(float)onWall / points,13:P1} " +
                    $"{(strayed == 0 ? 0d : offTotal / strayed),9:F2} /{offWorst,7:F2}mm " +
                    $"{rim.Graph.Band.Span,7:F1}mm {reversed,10} " +
                    $"{(double)clock.ElapsedMilliseconds / spans,12:F1}");
            }
        }
    }

    /// <summary>
    /// Smoothing a whole line, measured on the two things that could go wrong with it: the line lifting
    /// off the surface, and the line being eased into a crease.
    ///
    /// <para>
    /// Measured on an <em>edited</em> line rather than on the automatic one, because that is the line
    /// this pass exists for. The automatic trace is already resampled and relaxed upstream, so there is
    /// little in it to take out. What a user is left with after dragging handles is a line whose
    /// re-walked spans meet its untouched ones at a corner, and the corners are at the handles.
    /// </para>
    /// </summary>
    [Fact]
    public void SmoothingEasesTheLineWithoutLeavingTheWall()
    {
        var feature = new PartingMeshFeature(_engine);
        float floor = PartingLineSectionOptions.Default.ClearanceFloor;

        _log.WriteLine("                      points   turn before  turn after   nearest before   after");

        foreach (var (id, asset) in PartingLineCentringSweep.Models)
        {
            var imported = _engine.IO.Import(_assets.GetAssetPath(asset));
            var mould = MouldMesh.Create(imported.Value);
            if (mould.IsFailure) continue;

            var body = feature.GetBodyMesh(mould.Value);
            if (body.IsFailure) continue;

            var line = feature.GeneratePartingLineFromThickness(body.Value);
            if (line.IsFailure) continue;

            var edit = feature.BeginPartingLineEdit(body.Value, line.Value);
            if (edit.IsFailure) continue;

            foreach (var rim in edit.Value.Rims)
            {
                // Every other handle nudged a third of the way across the wall - the kind of adjustment
                // the control is for, and enough to put a corner at every join.
                var edited = rim.Line;
                for (int a = 0; a < edited.Anchors.Count; a += 2)
                {
                    var from = edited.Anchors[a].Position;
                    var toward = PartingBand.Closest(from, rim.Graph.Band.Second).Point;
                    edited = PartingLineEditor.Move(
                        edited, a, from + ((toward - from) * 0.33f), rim.Graph);
                }

                var before = edited.Flatten();

                // Timed because this runs on a button press with the UI thread waiting on it, and the
                // clearance guard measures every point against both creases on every pass.
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var smoothed = PartingLineEditor.Smooth(edited, rim.Graph);
                clock.Stop();

                var after = smoothed.Flatten();

                Assert.Equal(before.Count, after.Count);
                Assert.Equal(edited.Anchors.Count, smoothed.Anchors.Count);

                var readBefore = PartingLineSections.Analyse(before, rim.Graph.Band);
                var readAfter = PartingLineSections.Analyse(after, rim.Graph.Band);

                float drift = 0f;
                foreach (var p in after) drift = MathF.Max(drift, Vector3.Distance(p, rim.Graph.Snap(p)));

                _log.WriteLine(
                    $"  {id,-18} {after.Count,5}  {Mean(Turns(before)),9:F1}deg {Mean(Turns(after)),8:F1}deg " +
                    $"{readBefore.Nearest,12:F3} {readAfter.Nearest,9:F3}   drift {drift:F4}mm " +
                    $"in {clock.ElapsedMilliseconds}ms");

                // The anchors are the user's, so they do not move.
                for (int a = 0; a < edited.Anchors.Count; a++)
                    Assert.Equal(edited.Anchors[a].Position, smoothed.Anchors[a].Position);

                // Smoother along its length, which is what it is for.
                Assert.True(Mean(Turns(after)) < Mean(Turns(before)),
                    $"{id}: smoothing left the line turning harder than it did");

                // Still on the surface. Averaging pulls toward the chord, which is inside the body, so
                // this is the failure the per-pass snap exists to prevent.
                Assert.True(drift < rim.Graph.MeanEdge * 0.05f,
                    $"{id}: smoothing lifted the line {drift:F3}mm off the wall");

                // And still clear of the creases. Smoothing is allowed to spend clearance - a straighter
                // line inside a curved wall must come nearer the inside crease on every bend - so what
                // is asserted is the guard's own promise: never below the floor the diagnosis calls a
                // stretch faulty at, and never worse than it already was where it was already below it.
                Assert.True(readAfter.Nearest >= MathF.Min(readBefore.Nearest, floor) - 0.01f,
                    $"{id}: smoothing pushed the line from {readBefore.Nearest:F3} to " +
                    $"{readAfter.Nearest:F3} of the way across the wall, past the {floor:F2} floor");
            }
        }
    }

    /// <summary>
    /// No two consecutive points of an edited line may coincide.
    ///
    /// <para>
    /// Its own test because a zero-length segment is invisible on screen and fatal downstream: the
    /// flange sweep normalises a direction along the line at every point, and a repeated point makes
    /// that a division by zero. The geodesic retrace runs straight into it - a dragged handle is already
    /// snapped to the wall, so the walk leaving it starts at that same point re-derived, and a span that
    /// prepends its own anchor on top of that has one at every join.
    /// </para>
    /// </summary>
    [Fact]
    public void AnEditedLineHasNoRepeatedPoints()
    {
        var feature = new PartingMeshFeature(_engine);

        foreach (var (id, asset) in PartingLineCentringSweep.Models)
        {
            var imported = _engine.IO.Import(_assets.GetAssetPath(asset));
            var mould = MouldMesh.Create(imported.Value);
            if (mould.IsFailure) continue;

            var body = feature.GetBodyMesh(mould.Value);
            if (body.IsFailure) continue;

            var line = feature.GeneratePartingLineFromThickness(body.Value);
            if (line.IsFailure) continue;

            var edit = feature.BeginPartingLineEdit(body.Value, line.Value);
            if (edit.IsFailure) continue;

            foreach (var rim in edit.Value.Rims)
            {
                var edited = rim.Line;
                for (int a = 0; a < edited.Anchors.Count; a++)
                    edited = PartingLineEditor.Move(
                        edited, a, edited.Anchors[a].Position, rim.Graph);

                edited = PartingLineEditor.Smooth(edited, rim.Graph);

                var points = edited.Flatten();
                float shortest = float.MaxValue;
                for (int i = 0; i < points.Count; i++)
                    shortest = MathF.Min(
                        shortest, Vector3.Distance(points[i], points[(i + 1) % points.Count]));

                _log.WriteLine($"  {id,-18} {points.Count,5} points, shortest segment {shortest:F5}mm");
                Assert.True(shortest > rim.Graph.MeanEdge * 1e-4f,
                    $"{id}: an edited line has a {shortest:F6}mm segment in it");
            }
        }
    }

    /// <summary>
    /// Dividing a section by clicking on it: the handle lands where the marker said it would, the
    /// section becomes two, and nothing else about the line moves.
    ///
    /// <para>
    /// The last of those is the one worth a test. Adding a handle is something a user does to a stretch
    /// they are <em>happy with</em> - they want to take hold of it, not to change it - so a division
    /// that re-walked anything would move the line out from under the click that asked for it.
    /// </para>
    /// </summary>
    [Fact]
    public void DividingASectionPutsAHandleWhereThePreviewSaid()
    {
        var feature = new PartingMeshFeature(_engine);

        foreach (var (id, asset) in PartingLineCentringSweep.Models)
        {
            var imported = _engine.IO.Import(_assets.GetAssetPath(asset));
            var mould = MouldMesh.Create(imported.Value);
            if (mould.IsFailure) continue;

            var body = feature.GetBodyMesh(mould.Value);
            if (body.IsFailure) continue;

            var line = feature.GeneratePartingLineFromThickness(body.Value);
            if (line.IsFailure) continue;

            var built = feature.BeginPartingLineEdit(body.Value, line.Value);
            if (built.IsFailure) continue;

            var edit = built.Value;

            for (int rim = 0; rim < edit.Rims.Count; rim++)
            {
                var before = edit.Rims[rim].Line;

                // A point a third of the way along the first section, standing in for a cursor on it.
                var points = before.Spans[0].Points;
                var at = points[points.Count / 3];

                Assert.True(
                    PartingLineEditor.TryPlan(edit, rim, 0, at, out var plan),
                    $"{id}: nothing to divide a third of the way along a section");

                var (after, anchor) = PartingLineEditor.Insert(before, plan);

                // One more of each, and the handle is the one the plan named.
                Assert.Equal(before.Anchors.Count + 1, after.Anchors.Count);
                Assert.Equal(before.SpanCount + 1, after.SpanCount);
                Assert.Equal(plan.At, after.Anchors[anchor].Position);
                Assert.Equal(PartingAnchorOrigin.User, after.Anchors[anchor].Origin);

                // The two halves are the section it divided, cut at that point and nowhere else.
                Assert.Equal(plan.At, after.Spans[plan.Span].Points[^1]);
                Assert.Equal(plan.At, after.Spans[plan.Span + 1].Points[0]);
                Assert.Equal(
                    before.Spans[0].Points.Count + 1,
                    after.Spans[plan.Span].Points.Count + after.Spans[plan.Span + 1].Points.Count);

                // And the line is unmoved - the same points in the same order, one of them now a handle.
                Assert.Equal(before.Flatten(), after.Flatten());

                // Planning is refused where there is nothing to divide: on a handle that already exists,
                // which is what stops a click near one from stacking a second on top of it.
                Assert.False(
                    PartingLineEditor.TryPlan(edit, rim, 0, before.Anchors[0].Position, out _),
                    $"{id}: offered to divide a section at the handle already bounding it");

                _log.WriteLine(
                    $"  {id,-18} rim {rim}: {before.SpanCount} -> {after.SpanCount} sections, " +
                    $"handle {anchor} of {after.Anchors.Count}");
            }
        }
    }

    /// <summary>
    /// Which way round the rim span <paramref name="span"/> runs, read off the span itself - the same
    /// question <c>Retrace</c> answers, asked the same way, so these measurements describe the walks the
    /// editor actually makes.
    /// </summary>
    private static bool Forward(PartingRimEdit rim, int span)
    {
        var points = rim.Line.Spans[span].Points;
        var from = rim.Line.Anchors[span].Position;
        var to = rim.Line.Anchors[(span + 1) % rim.Line.Anchors.Count].Position;

        return points.Count < 3 || rim.Graph.ArcForward(from, to, points[points.Count / 2]);
    }

    private static double Length(IReadOnlyList<Vector3> path)
    {
        double total = 0d;
        for (int i = 1; i < path.Count; i++) total += Vector3.Distance(path[i - 1], path[i]);
        return total;
    }

    /// <summary>Degrees turned at each interior point of a path.</summary>
    private static IEnumerable<float> Turns(IReadOnlyList<Vector3> path)
    {
        for (int i = 1; i < path.Count - 1; i++)
        {
            var incoming = path[i] - path[i - 1];
            var outgoing = path[i + 1] - path[i];
            if (incoming.LengthSquared() < 1e-12f || outgoing.LengthSquared() < 1e-12f) continue;

            yield return MathF.Acos(Math.Clamp(
                Vector3.Dot(Vector3.Normalize(incoming), Vector3.Normalize(outgoing)), -1f, 1f))
                * 180f / MathF.PI;
        }
    }

    private static float Mean(IEnumerable<float> values)
    {
        float total = 0f;
        int count = 0;
        foreach (float v in values) { total += v; count++; }
        return count == 0 ? 0f : total / count;
    }
}
