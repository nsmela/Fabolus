using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Features.Transforms;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Diagnostics;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Core.Tests.Diagnostics;

/// <summary>
/// Why the parting cut left <c>flat_vulva</c> in one piece once the body was tilted 30-60 degrees
/// about X (issue #88), stage by stage. Runs the recipe <c>PartingSplitViewModel</c> fixes for new
/// work and prints what each stage hands the next: the traced line, the axis resolved from it, the
/// loft flange, the cutter, and the pieces the subtraction leaves.
///
/// <para>
/// What it found, before the fix. The axis was never the problem - it followed the tilt to within a
/// fraction of a degree - and nor was the cut. The line was. At 0, 15 and 75 degrees it came back as
/// 122 points, 374mm round and 17.6mm tall along its axis; at 30, 45 and 60 as 137 points, 403mm and
/// 29.2mm, up to 14.3mm off the untilted line once turned back into its frame, and doubling back on
/// itself six times seen along the axis. The mould-loft flange forces its bearings to advance, so
/// where the line went backwards the loft folded: 119-124 self-intersections in the flange, 561-603
/// in the cutter, against none at the other tilts. The folded cutter left one piece at 100% of the
/// mould and five to seven specks.
/// </para>
///
/// <para>
/// The line came from the crease offset at every tilt, off the same base crease, so the source was
/// not changing either. What changed was one walk. On the body's wide flap the creases run 40-45mm
/// apart and the walks across travel 57-68mm, and there two offset walks travel their full 25mm but
/// finish beside the crease they started from - sample 91 at 1mm at every tilt, and sample 88 at 6mm
/// at 30-60 degrees against 21mm at the rest. The first is pulled back out by the line treatment; the
/// two together are more than it can undo. Across all six tilts every other walk finished at least
/// 0.56 of its distance from the base crease (median 0.94), and these at 0.04 and 0.24. See
/// <see cref="CreaseOffsetOptions.LeastHeadway"/>, which now drops them; with it every tilt splits two
/// ways, 52/48 to 53/47 at 30-60 degrees.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class TiltedPartingCut
{
    private readonly IGeometryEngine _engine;
    private readonly PartingMeshFeature _sut;
    private readonly ITestOutputHelper _out;

    public TiltedPartingCut(GeometryEngineFixture fixture, ITestOutputHelper output)
    {
        _engine = fixture.Engine;
        _sut = new PartingMeshFeature(_engine);
        _out = output;
    }

    [DiagnosticTheory]
    [InlineData(0.0)]
    [InlineData(15.0)]
    [InlineData(30.0)]
    [InlineData(45.0)]
    [InlineData(60.0)]
    [InlineData(75.0)]
    public void CutStageByStage(double tiltDeg)
    {
        var mould = Mould(tiltDeg);
        var body = _sut.GetBodyMesh(mould).Value;

        var line = _sut.GeneratePartingLine(mould, LineRecipe).Value;

        var resolved = PartingMeshFeature.ResolveAxis(line, MeshRecipe).Value;
        var axis = resolved.Axis;

        // The line, and how far it climbs along the axis fitted to it.
        var loop = line.Loops[0];
        var heights = loop.Select(p => p.Dot(axis)).ToArray();
        _out.WriteLine(
            $"tilt {tiltDeg,4:F0}: line {line.Loops.Count} loop(s), {loop.Count} pts, " +
            $"perimeter {Perimeter(loop),6:F1}mm, height along axis {heights.Min(),7:F2}..{heights.Max(),7:F2}");
        _out.WriteLine(
            $"           axis ({axis.X,6:F3}, {axis.Y,6:F3}, {axis.Z,6:F3}), " +
            $"{Degrees(axis, Vector3.UnitY),5:F1} deg off +Y, {Degrees(axis, Vector3.UnitZ),5:F1} off +Z");

        var (u, v) = Frame(axis);
        _out.WriteLine($"           bearing turn round the line {Turning(loop, u, v) / Math.Tau,6:F2} turns, " +
                       $"backward steps {BackwardSteps(loop, u, v)}");

        var mouldExtent = Extent(mould.Mesh.Vertices, axis);
        _out.WriteLine($"           mould along axis {mouldExtent.Min,7:F2}..{mouldExtent.Max,7:F2}");

        var contour = _sut.GenerateOuterContour(mould, resolved).Value;
        var surface = _sut.GenerateFlangeSurface(line, contour, resolved, body).Value;

        var surfaceTopology = _engine.Evaluators.ValidateTopology(surface).Value;
        var surfaceCrossings = _engine.Evaluators.CountSelfIntersections(surface).Value;
        _out.WriteLine($"           flange {surface.VertexCount} verts, boundary edges {surfaceTopology.BoundaryEdgeCount}, " +
                       $"self-intersections {surfaceCrossings}");

        // Where the flange's outermost ring sits against the mould: every point of it must be outside,
        // or the mould wall is not cut through there. The loft stitches its rings in order, so the
        // last ring's worth of vertices is the outer one.
        using (var mouldIndex = _engine.Spatial.BuildIndex(mould.Mesh).Value)
        {
            var outer = surface.Vertices.Skip(surface.VertexCount - loop.Count).ToArray();
            var distances = outer.Select(p => mouldIndex.SignedDistance(p)).ToArray();
            _out.WriteLine($"           outer ring to mould: min {distances.Min(),6:F2}mm, " +
                           $"inside {distances.Count(d => d < 0)}/{distances.Length}");
        }

        var cutter = _sut.ExtrudeFlange(surface, resolved).Value;
        var cutterTopology = _engine.Evaluators.ValidateTopology(cutter).Value;
        var cutterCrossings = _engine.Evaluators.CountSelfIntersections(cutter).Value;
        _out.WriteLine($"           cutter watertight {cutterTopology.IsWatertight}, self-intersections {cutterCrossings}");

        var cut = _engine.Booleans.Subtract(mould.Mesh, cutter).Value;
        var pieces = _engine.Evaluators.SeparateComponents(cut).Value
            .Select(Volume).OrderByDescending(x => x).ToArray();
        double total = Volume(mould.Mesh);
        _out.WriteLine($"           pieces {pieces.Length}: " +
                       string.Join(", ", pieces.Take(4).Select(p => $"{100.0 * p / total:F1}%")));
    }

    /// <summary>
    /// The extrusion-border line taken apart into the sources <c>GeneratePartingLineFromThickness</c>
    /// chooses between, so it shows which one answered and what the ridge it was built on looked like.
    /// Every line is turned back into the untilted body's frame, so they can be laid against the
    /// untilted line directly.
    /// </summary>
    [DiagnosticTheory]
    [InlineData(0.0)]
    [InlineData(20.0)]
    [InlineData(25.0)]
    [InlineData(30.0)]
    [InlineData(45.0)]
    [InlineData(60.0)]
    [InlineData(70.0)]
    [InlineData(75.0)]
    public void LineSourcesByTilt(double tiltDeg)
    {
        var body = _sut.GetBodyMesh(Mould(tiltDeg)).Value;
        var reference = _sut.GetBodyMesh(Mould(0.0)).Value;
        var untilted = _sut.GeneratePartingLine(Mould(0.0), LineRecipe).Value.Loops[0];

        var thickness = WallThickness.Measure(_engine, body.Mesh, WallThicknessOptions.Default).Value;
        var contours = RidgeDetection.FindRidgeContours(body.Mesh, RidgeDetectionOptions.Default);
        var closed = contours.Where(c => c.IsClosed).ToList();
        var rims = PartingStrategy.Rims(closed, thickness.Statistics.Median);

        _out.WriteLine(
            $"tilt {tiltDeg,4:F0}: body {body.Mesh.VertexCount} verts (untilted {reference.Mesh.VertexCount}), " +
            $"wall median {thickness.Statistics.Median,5:F2}mm");
        _out.WriteLine(
            $"           contours {contours.Count} ({closed.Count} closed): " +
            string.Join(", ", contours.Select(c => $"{c.Points.Count}{(c.IsClosed ? "" : " open")}/{Perimeter(c.Points):F0}mm")));
        _out.WriteLine(
            $"           rims: " + string.Join(", ", rims.Select(r => $"#{r.Id} {r.Kind} [{string.Join(",", r.ContourIndices)}]")));

        using var projector = new PartingTools(_engine).CreateSurfaceProjector(body.Mesh).Value;
        foreach (var rim in rims.Where(r => r.Kind == PartingRimKind.Wall))
        {
            var band = new PartingBand(closed[rim.ContourIndices[0]], closed[rim.ContourIndices[1]]);
            var offset = CreaseOffsetLine.Trace(body.Mesh, band, out var report, projector);
            _out.WriteLine(
                $"           offset on rim #{rim.Id}: " +
                (offset is null ? "none" : $"{offset.Count} pts, {Perimeter(offset):F0}mm, {Deviation(Untilt(offset, tiltDeg), untilted)}") +
                (report is null ? "" : $"; base {report.Base}, crossing {report.Crossing:F2}, reached {report.Reached}/{report.Samples}, clamped {report.Clamped}"));

            if (offset is null || report is null) continue;
            _out.WriteLine("             walks: crossing " + string.Join(", ", report.Crossings.Select(kv => $"{kv.Key} {kv.Value}")) +
                           "; offset " + string.Join(", ", report.Offsets.Select(kv => $"{kv.Key} {kv.Value}")) +
                           $"; short {report.Short}, turned back {report.TurnedBack}");

            // The stretches of the offset that run away from the untilted line, and where they run
            // to: still between the creases, or off the wall entirely.
            var baseCrease = report.Base == 0 ? band.First : band.Second;
            var farCrease = report.Base == 0 ? band.Second : band.First;
            var back = Untilt(offset, tiltDeg);
            for (int i = 0; i < offset.Count; i++)
            {
                double off = untilted.Min(q => back[i].DistanceTo(q));
                if (off < 3.0) continue;
                _out.WriteLine(
                    $"             [{i,3}] {off,5:F1}mm off; to base crease {PartingBand.Closest(offset[i], baseCrease).Distance,5:F1}, " +
                    $"to far crease {PartingBand.Closest(offset[i], farCrease).Distance,5:F1}");
            }
        }

        var line = _sut.GeneratePartingLine(Mould(tiltDeg), LineRecipe).Value.Loops[0];
        _out.WriteLine($"           final line {line.Count} pts, {Perimeter(line):F0}mm, {Deviation(Untilt(line, tiltDeg), untilted)}");
    }

    /// <summary>
    /// The same cut drawn twice, without the headway check and with it, into a contact sheet under
    /// <c>FABOLUS_TILT_IMAGES</c>. The line here is built from the public stages
    /// <c>GeneratePartingLineFromThickness</c> runs - crease offset, then treatment - so the check can
    /// be switched off for the "before" row; the "after" row is checked against the feature's own line.
    /// </summary>
    [DiagnosticTheory]
    [InlineData(45.0)]
    [InlineData(30.0)]
    public void DrawBeforeAndAfter(double tiltDeg)
    {
        var directory = Environment.GetEnvironmentVariable("FABOLUS_TILT_IMAGES");
        if (string.IsNullOrWhiteSpace(directory)) { _out.WriteLine("FABOLUS_TILT_IMAGES not set"); return; }

        const int size = 520;
        var options = RenderOptions.Default;
        var lineColour = new Rgb(80, 220, 255);
        var baseColour = new Rgb(255, 170, 60);
        var farColour = new Rgb(255, 230, 90);
        var walkColour = new Rgb(255, 70, 90);
        var foldColour = new Rgb(235, 60, 60);

        var mould = Mould(tiltDeg);
        var body = _sut.GetBodyMesh(mould).Value;

        var thickness = WallThickness.Measure(_engine, body.Mesh, WallThicknessOptions.Default).Value;
        var closed = RidgeDetection.FindRidgeContours(body.Mesh, RidgeDetectionOptions.Default)
            .Where(c => c.IsClosed).ToList();
        var rim = PartingStrategy.Rims(closed, thickness.Statistics.Median).Single(r => r.Kind == PartingRimKind.Wall);
        var band = new PartingBand(closed[rim.ContourIndices[0]], closed[rim.ContourIndices[1]]);

        using var projector = new PartingTools(_engine).CreateSurfaceProjector(body.Mesh).Value;

        var shipped = _sut.GeneratePartingLine(mould, LineRecipe).Value;
        var tiles = new List<Tile>();
        Camera? flap = null;
        IReadOnlyList<Vector3>? beforeLine = null;

        // The walk ends the check drops: those of the unchecked trace that the checked one has nothing
        // near. Marked in both rows, so the "after" row shows the line passing them by.
        var unguarded = CreaseOffsetLine.Trace(body.Mesh, band, projector,
            CreaseOffsetOptions.Default with { LeastHeadway = 0.0, SmoothingPasses = 0 })!;
        var guarded = CreaseOffsetLine.Trace(body.Mesh, band, projector,
            CreaseOffsetOptions.Default with { SmoothingPasses = 0 })!;
        var dropped = unguarded.Where(p => guarded.Min(q => p.DistanceTo(q)) > 3.0).ToList();
        _out.WriteLine($"dropped walk ends drawn: {dropped.Count}");

        foreach (var (label, headway) in new[] { ("before", 0.0), ("after", CreaseOffsetOptions.Default.LeastHeadway) })
        {
            var offsetOptions = CreaseOffsetOptions.Default with { LeastHeadway = headway };

            // The walks' end points as laid, with nothing smoothed: what the turned-back walks look like.
            var walks = CreaseOffsetLine.Trace(body.Mesh, band, out var report, projector,
                offsetOptions with { SmoothingPasses = 0 })!;
            var offset = CreaseOffsetLine.Trace(body.Mesh, band, projector, offsetOptions)!;
            var line = PartingLineTreatment.Apply(new PartingLine([offset]), [band],
                PartingLineTreatmentOptions.Default, projector);
            var loop = line.Loops[0];

            if (label == "after")
                _out.WriteLine($"after line {loop.Count} pts vs feature's {shipped.Loops[0].Count}, " +
                               $"{Deviation(loop, shipped.Loops[0])}");

            var resolved = PartingMeshFeature.ResolveAxis(line, MeshRecipe).Value;
            var axis = resolved.Axis;
            var contour = _sut.GenerateOuterContour(mould, resolved).Value;
            var surface = _sut.GenerateFlangeSurface(line, contour, resolved, body).Value;
            var cutter = _sut.ExtrudeFlange(surface, resolved).Value;
            var cut = _engine.Booleans.Subtract(mould.Mesh, cutter).Value;
            var pieces = _engine.Evaluators.SeparateComponents(cut).Value
                .OrderByDescending(Volume).ToList();
            double total = Volume(mould.Mesh);
            int crossings = _engine.Evaluators.CountSelfIntersections(surface).Value;

            var along = new View("axis", axis);

            // 1. The whole body seen along the axis, with the line on it.
            var whole = Camera.Fit(body.Mesh, along, size, size);
            var r1 = MeshRasterizer.Render(body.Mesh, whole, size, size, options);
            MeshRasterizer.DrawPolyline(r1, whole, band.First.Points, true, baseColour, options);
            MeshRasterizer.DrawPolyline(r1, whole, band.Second.Points, true, farColour, options);
            MeshRasterizer.DrawPolyline(r1, whole, loop, true, lineColour, options);
            tiles.Add(new Tile($"{label}: line along axis", r1,
                $"{BackwardSteps(loop, Frame(axis).U, Frame(axis).V)} back-steps"));

            // 2. Close on the flap where the walks turn back, framed once on the "before" line's
            //    worst stretch so both rows show the same place.
            beforeLine ??= loop;
            if (flap is null)
            {
                var worst = beforeLine.OrderByDescending(p => shipped.Loops[0].Min(q => p.DistanceTo(q))).First();
                flap = Camera.Focus(body.Mesh, along, worst, 22.0, size, size);
            }
            var r2 = MeshRasterizer.Render(body.Mesh, flap, size, size, options);
            MeshRasterizer.DrawPolyline(r2, flap, band.First.Points, true, baseColour, options);
            MeshRasterizer.DrawPolyline(r2, flap, band.Second.Points, true, farColour, options);
            MeshRasterizer.DrawPolyline(r2, flap, walks, true, walkColour, options);
            MeshRasterizer.DrawPolyline(r2, flap, loop, true, lineColour, options);
            foreach (var point in dropped) MeshRasterizer.DrawMarker(r2, flap, point, walkColour, options, 7);
            tiles.Add(new Tile($"{label}: flap close-up", r2, $"turned back {report?.TurnedBack ?? 0}"));

            // 3. The flange along the axis, faces turned over against the majority in red: those
            //    are the folds.
            var flipped = Flipped(surface, axis);
            var flangeCamera = Camera.Fit(surface, along, size, size);
            var r3 = MeshRasterizer.Render(surface, flangeCamera, size, size, options,
                face => flipped[face] ? foldColour : options.DefaultFace);
            MeshRasterizer.DrawPolyline(r3, flangeCamera, loop, true, lineColour, options);
            tiles.Add(new Tile($"{label}: flange", r3, $"{flipped.Count(f => f)} folded, {crossings} crossings"));

            // 4. The cut, each piece in its own colour and pushed apart along the axis.
            var exploded = Explode(pieces, axis, 25.0);
            var cutCamera = Camera.Fit(exploded.Mesh, Views.OrbitA, size, size);
            var r4 = MeshRasterizer.Render(exploded.Mesh, cutCamera, size, size, options,
                face => MeshRasterizer.ContourColour(exploded.PieceOf[face] + 1));
            tiles.Add(new Tile($"{label}: cut", r4,
                $"{pieces.Count} pieces: " + string.Join("/", pieces.Take(2).Select(p => $"{100 * Volume(p) / total:F0}%"))));
        }

        var path = Path.Combine(directory, $"flat_vulva-tilt{tiltDeg:F0}-before-after.png");
        ContactSheet.Save(path, tiles, 4, size, options.Background,
            $"flat_vulva tilted {tiltDeg:F0}° about X: before / after the headway check",
            [("parting line", lineColour), ("base crease", baseColour), ("far crease", farColour),
             ("walk ends, unsmoothed (dots: dropped)", walkColour), ("folded flange face", foldColour)]);
        _out.WriteLine(path);
    }

    /// <summary>Which faces point against the way most of the surface's area faces along the axis.</summary>
    private static bool[] Flipped(IMesh mesh, Vector3 axis)
    {
        var vs = mesh.Vertices;
        var ts = mesh.Triangles;
        var facing = new double[ts.Length / 3];
        for (int f = 0; f < facing.Length; f++)
        {
            var a = vs[ts[f * 3]];
            facing[f] = (vs[ts[(f * 3) + 1]] - a).Cross(vs[ts[(f * 3) + 2]] - a).Dot(axis);
        }

        double sign = facing.Sum() >= 0 ? 1.0 : -1.0;
        return facing.Select(d => d * sign < 0).ToArray();
    }

    /// <summary>The pieces as one mesh, each pushed away from the others' centre along the axis.</summary>
    private (IMesh Mesh, int[] PieceOf) Explode(IReadOnlyList<IMesh> pieces, Vector3 axis, double gap)
    {
        double middle = pieces.Average(p => p.Vertices.Average(v => v.Dot(axis)));
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var pieceOf = new List<int>();

        for (int k = 0; k < pieces.Count; k++)
        {
            var piece = pieces[k];
            double height = piece.Vertices.Average(v => v.Dot(axis));
            var shift = axis * (height >= middle ? gap : -gap);
            int start = vertices.Count;

            vertices.AddRange(piece.Vertices.Select(v => v + shift));
            triangles.AddRange(piece.Triangles.Select(t => t + start));
            pieceOf.AddRange(Enumerable.Repeat(k, piece.Triangles.Length / 3));
        }

        var mesh = _engine.CreateMesh([.. vertices], [.. triangles], pieces[0].Metadata).Value;
        return (mesh, pieceOf.ToArray());
    }

    /// <summary>Points turned back by the tilt, into the untilted body's frame.</summary>
    private static IReadOnlyList<Vector3> Untilt(IReadOnlyList<Vector3> points, double tiltDeg)
    {
        double a = -tiltDeg * Math.PI / 180.0, c = Math.Cos(a), s = Math.Sin(a);
        return points.Select(p => new Vector3(p.X, (p.Y * c) - (p.Z * s), (p.Y * s) + (p.Z * c))).ToList();
    }

    /// <summary>How far a line runs from another: the median and worst distance of its points to it.</summary>
    private static string Deviation(IReadOnlyList<Vector3> line, IReadOnlyList<Vector3> reference)
    {
        var d = line.Select(p => reference.Min(q => p.DistanceTo(q))).OrderBy(x => x).ToArray();
        return $"off untilted line median {d[d.Length / 2]:F2}mm, worst {d[^1]:F2}mm";
    }

    private static PartingLineParameters LineRecipe => new()
    {
        Source = PartingLineSource.ExtrusionBorder,
        PullDirection = Vector3.UnitY,
        NeutralBand = PartingNeutralBand.FromDegrees(-5.0, 5.0),
    };

    private static PartingMeshParameters MeshRecipe => PartingMeshParameters.Default with
    {
        AxisSource = PartingMeshAxisSource.PartingLine,
        Sweep = PartingMeshSweep.MouldLoft,
        SplitMethod = PartingSplitMethod.SeveredComponents,
        Thickening = PartingMeshThickening.Extrude,
        Depth = 0.1,
        FlangeMaxSlopeDeg = 80.0,
        NormalFollowMm = 100.0,
        RawFlange = false,
        NormalSmoothingPasses = 5,
        Axis = Vector3.UnitY,
    };

    /// <summary>flat_vulva, tilted about X, with a concave mould generated round it.</summary>
    private MouldMesh Mould(double tiltDeg)
    {
        var workspace = new ImportMesh(_engine).Execute(Workspace.CreateEmpty(), Asset("flat_vulva.stl")).Value;
        var id = workspace.GetActiveRecord().Value.Id;

        if (tiltDeg != 0.0)
            workspace = new TransformMesh(_engine)
                .Rotate(workspace, id, (float)(tiltDeg * Math.PI / 180.0), Vector3.UnitX).Value;

        workspace = new GenerateMould(_engine).Execute(workspace, id,
            MouldDefinition.OfShape(MouldShapeType.Concave, 2.5, 5.0) with { TargetMeshId = id }).Value;

        return MouldMesh.Create(workspace.GetMesh(id).Value, workspace.GetRecord(id).Value).Value;
    }

    private static double Perimeter(IReadOnlyList<Vector3> loop)
    {
        double total = 0.0;
        for (int i = 0; i < loop.Count; i++) total += loop[i].DistanceTo(loop[(i + 1) % loop.Count]);
        return total;
    }

    private static double Degrees(Vector3 a, Vector3 b) =>
        Math.Acos(Math.Clamp(Math.Abs(a.Normalize().Dot(b.Normalize())), 0.0, 1.0)) * 180.0 / Math.PI;

    private static (double Min, double Max) Extent(IReadOnlyList<Vector3> vertices, Vector3 axis)
    {
        double min = double.MaxValue, max = double.MinValue;
        foreach (var p in vertices)
        {
            min = Math.Min(min, p.Dot(axis));
            max = Math.Max(max, p.Dot(axis));
        }
        return (min, max);
    }

    /// <summary>The plane frame the loft works in, built the same way.</summary>
    private static (Vector3 U, Vector3 V) Frame(Vector3 axis)
    {
        var seed = Math.Abs(axis.Dot(Vector3.UnitY)) < 0.9 ? Vector3.UnitY : Vector3.UnitX;
        var u = seed.Cross(axis).Normalize();
        return (u, axis.Cross(u));
    }

    private static double[] Bearings(IReadOnlyList<Vector3> loop, Vector3 u, Vector3 v)
    {
        double cu = loop.Average(p => p.Dot(u)), cv = loop.Average(p => p.Dot(v));
        return loop.Select(p => Math.Atan2(p.Dot(v) - cv, p.Dot(u) - cu)).ToArray();
    }

    private static double Wrap(double a)
    {
        while (a > Math.PI) a -= Math.Tau;
        while (a < -Math.PI) a += Math.Tau;
        return a;
    }

    private static double Turning(IReadOnlyList<Vector3> loop, Vector3 u, Vector3 v)
    {
        var b = Bearings(loop, u, v);
        double t = 0.0;
        for (int i = 0; i < b.Length; i++) t += Wrap(b[(i + 1) % b.Length] - b[i]);
        return t;
    }

    private static int BackwardSteps(IReadOnlyList<Vector3> loop, Vector3 u, Vector3 v)
    {
        var b = Bearings(loop, u, v);
        double sign = Turning(loop, u, v) >= 0 ? 1.0 : -1.0;
        int count = 0;
        for (int i = 0; i < b.Length; i++)
            if (sign * Wrap(b[(i + 1) % b.Length] - b[i]) < 0) count++;
        return count;
    }

    private static double Volume(IMesh mesh)
    {
        var vs = mesh.Vertices;
        var ts = mesh.Triangles;
        double total = 0;
        for (int i = 0; i + 2 < ts.Length; i += 3)
            total += vs[ts[i]].Dot(vs[ts[i + 1]].Cross(vs[ts[i + 2]])) / 6.0;
        return Math.Abs(total);
    }

    private static string Asset(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "files")))
            dir = dir.Parent;

        return Path.Combine(dir!.FullName, "tests", "files", name);
    }
}
