using System.Numerics;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// The three edits a user can make to a sectioned parting line, exercised on <c>standard</c>.
///
/// <para>
/// The question each answers is the same: after the edit, is the line still on the wall and still a
/// closed loop? An editing model that lets a handle drag the line off the band is worse than no editing
/// model, because the result looks plausible on screen and fails at the seal.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class SectionedLineEditing
{
    private const int Panel = 900;
    private static readonly Rgb Background = new(24, 26, 32);
    private static readonly Rgb Wall = new(94, 110, 171);
    private static readonly Rgb Plain = new(190, 193, 200);
    private static readonly Rgb Crease = new(198, 76, 255);
    private static readonly Rgb Line = new(255, 226, 40);
    private static readonly Rgb Handle = new(80, 255, 140);
    private static readonly Rgb Touched = new(255, 110, 90);

    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;
    private readonly ITestOutputHelper _log;

    public SectionedLineEditing(GeometryEngineFixture assets, ITestOutputHelper log)
    {
        _assets = assets;
        _engine = assets.Engine;
        _log = log;
    }

    [Fact]
    public void SeedMoveRemoveAndInsert()
    {
        string? directory = Environment.GetEnvironmentVariable("FABOLUS_RIDGE_REPORT_DIR");

        var imported = _engine.IO.Import(_assets.GetAssetPath("3mf/test bolus standard.3mf"));
        var mould = MouldMesh.Create(imported.Value);
        var body = new PartingMeshFeature(_engine).GetBodyMesh(mould.Value).Value.Mesh;

        var thickness = _engine.Evaluators.MeasureWallThickness(body, WallThicknessOptions.Default);
        var projector = _engine.PartingTools.CreateSurfaceProjector(body);
        var surface = projector.IsSuccess ? projector.Value : null;

        var ridge = RidgeDetection.FindRidge(body, RidgeDetectionOptions.Default);
        var contours = ridge.Contours.Where(c => c.IsClosed).ToList();
        var rim = PartingStrategy.Rims(contours, thickness.Value.Statistics.Median)
            .Single(r => r.Kind == PartingRimKind.Wall);
        var band = new PartingBand(contours[rim.ContourIndices[0]], contours[rim.ContourIndices[1]]);

        var mask = ridge.Band.Length == ridge.Faces.Length ? ridge.Band : ridge.Faces;
        var graph = PartingBandGraph.Build(body, mask, ridge.FaceRims, rim.Id, band);
        Assert.NotNull(graph);

        var loop = CreaseOffsetLine.Trace(body, band, surface)!.ToArray();
        var seeded = PartingLineEditor.Seed(loop, band);

        _log.WriteLine($"seeded {seeded.Anchors.Count} anchors, {seeded.SpanCount} spans");
        for (int i = 0; i < seeded.Spans.Count; i++)
            _log.WriteLine(
                $"  span {i,2}  {seeded.Spans[i].Condition,-8} {seeded.Spans[i].Points.Count,4} points  " +
                $"{seeded.Spans[i].Length,6:F1} mm");

        // The handle bounding the detour, which is the one a user would reach for first.
        int detour = Math.Max(seeded.Spans.ToList().FindIndex(
            s => s.Condition == PartingLineCondition.Detour), 0);

        // Dragged across the wall by a third of its width, which is the kind of nudge the control is
        // for - and deliberately off the band, to check it is pinned back onto it.
        var from = seeded.Anchors[detour].Position;
        var toward = PartingBand.Closest(from, band.Second).Point;
        var moved = PartingLineEditor.Move(
            seeded, detour, from + ((toward - from) * 0.35f), graph!);

        var removed = PartingLineEditor.Remove(seeded, detour, graph!);

        // Added halfway along the longest span, which is where a user would want more control.
        int longest = 0;
        for (int i = 1; i < seeded.Spans.Count; i++)
            if (seeded.Spans[i].Length > seeded.Spans[longest].Length) longest = i;

        var middle = seeded.Spans[longest].Points[seeded.Spans[longest].Points.Count / 2];
        var inserted = PartingLineEditor.Insert(seeded, middle, graph!);

        var states = new (string Name, SectionedPartingLine Line, int Touched)[]
        {
            ("seeded", seeded, -1),
            ("moved", moved, detour),
            ("removed", removed, -1),
            ("inserted", inserted, longest + 1),
        };

        _log.WriteLine("                anchors  spans  points   nearest  off-mid  on-wall   length");
        foreach (var (name, state, _) in states)
            _log.WriteLine($"  {name,-12} {state.Anchors.Count,6} {state.SpanCount,6}  {Describe(state, band, graph!)}");

        foreach (var (name, state, _) in states)
        {
            Assert.True(state.Anchors.Count > 0, $"{name} lost its anchors");
            Assert.True(state.Flatten().Count > 16, $"{name} collapsed");
        }

        if (directory is not null)
            Draw(Path.Combine(directory, "standard-editing"), body, mask, contours, states);
    }

    /// <summary>
    /// The feature entry point the view actually calls, on every body - including the one that parts
    /// along two rims, which is where loop-to-rim matching has to be right.
    /// </summary>
    [Fact]
    public void EveryBodyCanBePutIntoEditableForm()
    {
        var feature = new PartingMeshFeature(_engine);

        foreach (var (id, asset) in PartingLineCentringSweep.Models)
        {
            var imported = _engine.IO.Import(_assets.GetAssetPath(asset));
            var mould = MouldMesh.Create(imported.Value);
            if (mould.IsFailure) { _log.WriteLine($"{id}: not a mould"); continue; }

            var body = feature.GetBodyMesh(mould.Value);
            if (body.IsFailure) { _log.WriteLine($"{id}: no body"); continue; }

            var line = feature.GeneratePartingLineFromThickness(body.Value);
            if (line.IsFailure) { _log.WriteLine($"{id}: no line - {line.Error.Description}"); continue; }

            var edit = feature.BeginPartingLineEdit(body.Value, line.Value);
            if (edit.IsFailure) { _log.WriteLine($"{id}: not editable - {edit.Error.Description}"); continue; }

            var built = edit.Value;
            _log.WriteLine(
                $"{id}: {built.Rims.Count} rim(s), " +
                string.Join(" / ", built.Rims.Select(r =>
                    $"{r.Line.Anchors.Count} handles, {r.Line.SpanCount} sections")));

            // Every rim came back with usable controls, and flattening still yields the loops the
            // rest of the pipeline expects.
            foreach (var rim in built.Rims)
            {
                Assert.True(rim.Line.Anchors.Count >= 2, $"{id}: too few handles to edit");
                Assert.Equal(rim.Line.Anchors.Count, rim.Line.SpanCount);
            }

            var flattened = built.ToPartingLine();
            Assert.Equal(line.Value.Loops.Count, flattened.Loops.Count);

            // Every handle of every rim thrown fifty millimetres off the body at once - far past
            // anything a user would do, and the point is that the result is still a line on the wall.
            foreach (var rim in built.Rims)
            {
                var edited = rim.Line;
                for (int a = 0; a < edited.Anchors.Count; a++)
                    edited = PartingLineEditor.Move(
                        edited, a, edited.Anchors[a].Position + new Vector3(50f, 50f, 50f), rim.Graph);

                // The anchors are the invariant. Every one is pinned by Snap, so no drag can leave one
                // off the wall - and that is what the editing model actually promises.
                foreach (var anchor in edited.Anchors)
                    Assert.True(
                        Vector3.Distance(anchor.Position, rim.Graph.Snap(anchor.Position)) < 1e-3f,
                        $"{id}: a dragged handle ended up off the wall");

                // The spans are a weaker claim on purpose: a span whose walk cannot find a route keeps
                // the points it had, which is the right failure - the alternative is a stretch that
                // vanishes. So this is reported rather than asserted tightly.
                var points = edited.Flatten();
                int onWall = points.Count(p =>
                    Vector3.Distance(p, rim.Graph.Snap(p)) < rim.Graph.MeanEdge * 2f);

                float share = (float)onWall / points.Count;
                _log.WriteLine($"    every handle thrown 50mm: anchors all pinned, {share:P1} of span points on wall");
                Assert.True(share > 0.9f, $"{id}: a drag stranded most of the line off the wall");
            }
        }
    }

    private static string Describe(
        SectionedPartingLine line, PartingBand band, PartingBandGraph graph)
    {
        var points = line.Flatten();
        var report = PartingLineSections.Analyse(points, band);

        int off = report.Samples.Count(s => s.Across < 0.35f || s.Across > 0.65f);

        // How much of the line is actually on the wall, which is the guarantee the whole model rests
        // on: a handle dragged anywhere must not be able to take the line off the band.
        int onWall = points.Count(p => graph.NearestFace(p) >= 0
            && Vector3.Distance(p, graph.Snap(p)) < graph.MeanEdge * 2f);

        float length = 0f;
        for (int i = 0; i < points.Count; i++)
            length += Vector3.Distance(points[i], points[(i + 1) % points.Count]);

        return $"{points.Count,6}  {report.Nearest,+7:0.00}  " +
               $"{(float)off / Math.Max(report.Samples.Count, 1),7:P1}  " +
               $"{(float)onWall / points.Count,7:P1}  {length,7:F1}";
    }

    private static void Draw(
        string directory, IMesh body, bool[] mask, IReadOnlyList<RidgeContour> contours,
        (string Name, SectionedPartingLine Line, int Touched)[] states)
    {
        Directory.CreateDirectory(directory);

        var options = RenderOptions.Default with { DrawOccludedLines = false };
        int faceCount = body.Triangles.Length / 3;

        Rgb FaceColour(int face) => mask.Length == faceCount && mask[face] ? Wall : Plain;

        foreach (var (name, line, touched) in states)
        {
            var tiles = new List<Tile>();

            foreach (var view in Views.Standard)
            {
                var camera = Camera.Fit(body, view, Panel, Panel);
                var image = MeshRasterizer.Render(body, camera, Panel, Panel, options, FaceColour);

                foreach (var contour in contours)
                    MeshRasterizer.DrawPolyline(
                        image, camera, contour.Points, contour.IsClosed, Crease, options);

                // Each span in the line colour, the one the edit touched picked out - so a sheet says
                // what changed as well as what the line now is.
                for (int s = 0; s < line.Spans.Count; s++)
                    MeshRasterizer.DrawPolyline(
                        image, camera, line.Spans[s].Points, false,
                        s == touched ? Touched : Line, options);

                foreach (var anchor in line.Anchors)
                    MeshRasterizer.DrawMarker(image, camera, anchor.Position, Handle, options, 9);

                tiles.Add(new Tile(view.Name, image));
            }

            ContactSheet.Save(
                Path.Combine(directory, $"edit-{name}.png"), tiles, 4, Panel, Background,
                $"standard - {name} - {line.Anchors.Count} handles, {line.SpanCount} sections",
                new[] { ("rim wall", Wall), ("crease", Crease), ("line", Line),
                        ("handle", Handle), ("edited", Touched) });
        }
    }
}
