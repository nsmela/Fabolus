using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using GeometryEngine.Core.Geometry;
using SkiaSharp;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// Where the specks a parting cut leaves come from (issue #88). The view's recipe on scalp_bolus parts
/// the mould 57/43 and leaves seven components of under 0.1 mm3 along the rim; this rebuilds the same
/// cut stage by stage and places each speck against everything that could have made it - the mould's
/// two walls, the cutter's two sheets, the flange the cutter was extruded from, and the crossings the
/// extrusion folds into it.
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class SpeckAnatomy
{
    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;
    private readonly ITestOutputHelper _log;

    public SpeckAnatomy(GeometryEngineFixture assets, ITestOutputHelper log)
    {
        _assets = assets;
        _engine = assets.Engine;
        _log = log;
    }

    /// <summary>The recipe PartingSplitViewModel fixes for new work.</summary>
    private static PartingLineParameters Line => PartingLineParameters.Default with
    {
        Source = PartingLineSource.ExtrusionBorder,
        PullDirection = Vector3.UnitY,
        NeutralBand = PartingNeutralBand.FromDegrees(-5.0, 5.0),
    };

    private static PartingMeshParameters Mesh => PartingMeshParameters.Default with
    {
        AxisSource = PartingMeshAxisSource.PartingLine,
        Sweep = PartingMeshSweep.MouldLoft,
        SplitMethod = PartingSplitMethod.SeveredComponents,
        Thickening = PartingMeshThickening.Extrude,
        FlangeMaxSlopeDeg = 80.0,
        NormalFollowMm = 100.0,
        RawFlange = false,
        NormalSmoothingPasses = 5,
        Axis = Vector3.UnitY,
    };

    [DiagnosticFact]
    public void WhereTheSpecksSit()
    {
        var files = Environment.GetEnvironmentVariable("FABOLUS_SPECK_FILES") is { Length: > 0 } chosen
            ? chosen.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : ["scalp_bolus.stl", "chin_bolus.stl", "nose_bolus.stl", "ear_bolus.stl", "eye_bolus.stl", "flat_vulva.stl", "larynx_bolus.stl", "test bolus 107mL.stl", "test_smoothed_bolus.stl"];
        foreach (string file in files)
            Anatomy(file);
    }

    private void Anatomy(string file)
    {
        try { AnatomyOf(file); }
        catch (Exception e) { _log.WriteLine($"{file}: threw {e.GetType().Name}: {e.Message}"); }
    }

    private void AnatomyOf(string file)
    {
        var (workspace, bodyId) = GeometryEngineFixture.AddBody(Workspace.CreateEmpty(), _assets.LoadStl(file));
        var built = new GenerateMould(_engine).Execute(workspace, bodyId,
            MouldDefinition.OfShape(MouldShapeType.Concave, 2.5, 5.0) with { TargetMeshId = bodyId });
        if (built.IsFailure) { _log.WriteLine($"{file}: mould - {built.Error.Description}"); return; }

        var mould = MouldMesh.Create(built.Value.GetMesh(bodyId).Value, built.Value.GetRecord(bodyId).Value).Value;
        var feature = new PartingMeshFeature(_engine);

        var line = feature.GeneratePartingLine(mould, Line);
        if (line.IsFailure) { _log.WriteLine($"{file}: line - {line.Error.Description}"); return; }
        var resolved = PartingMeshFeature.ResolveAxis(line.Value, Mesh);
        if (resolved.IsFailure) { _log.WriteLine($"{file}: axis - {resolved.Error.Description}"); return; }
        var settings = resolved.Value;
        var contour = feature.GenerateOuterContour(mould, settings);
        if (contour.IsFailure) { _log.WriteLine($"{file}: contour - {contour.Error.Description}"); return; }
        var bodyResult = feature.GetBodyMesh(mould);
        if (bodyResult.IsFailure) { _log.WriteLine($"{file}: body - {bodyResult.Error.Description}"); return; }
        var body = bodyResult.Value;
        var surfaceResult = feature.GenerateFlangeSurface(line.Value, contour.Value, settings, body);
        if (surfaceResult.IsFailure) { _log.WriteLine($"{file}: flange - {surfaceResult.Error.Description}"); return; }
        var surface = surfaceResult.Value;
        var cutterResult = feature.ExtrudeFlange(surface, settings);
        if (cutterResult.IsFailure) { _log.WriteLine($"{file}: cutter - {cutterResult.Error.Description}"); return; }
        var cutter = cutterResult.Value;
        var rawResult = feature.CutMouldWith(mould, cutter);
        if (rawResult.IsFailure) { _log.WriteLine($"{file}: cut - {rawResult.Error.Description}"); return; }
        var raw = rawResult.Value;

        var components = _engine.Evaluators.SeparateComponents(raw).Value
            .Select(c => (Mesh: c, Volume: PartingPieces.Volume(c)))
            .OrderByDescending(c => c.Volume)
            .ToList();
        double total = components.Sum(c => c.Volume);
        var specks = components.Where(c => PartingPieces.IsSpeck(c.Mesh)).ToList();

        // Mean thickness, 2V/A: a slab of thickness h has volume hA/2 for its two faces' area A. The
        // halves are mould walls millimetres thick; a sliver trapped under a 0.1 mm cutter is not.
        foreach (var (mesh, volume) in components)
            _log.WriteLine($"    component {volume,12:F4} mm3  {volume / total,8:P3}  area {Area(mesh),10:F2} mm2  mean thickness {2.0 * volume / Area(mesh),7:F3} mm");

        var axis = settings.Axis.Normalize();
        _log.WriteLine($"=== {file}: {components.Count} components, {specks.Count} specks; " +
            $"axis ({axis.X:F3}, {axis.Y:F3}, {axis.Z:F3}), depth {settings.Depth} mm; " +
            $"self-intersections: flange {Count(surface)}, cutter {Count(cutter)}, mould {Count(mould.Mesh)}");
        var mouldIndex = _engine.Spatial.IndexFor(mould.Mesh).Value;
        var bodyIndex = _engine.Spatial.IndexFor(body.Mesh).Value;
        var surfaceIndex = _engine.Spatial.IndexFor(surface).Value;

        // Where the flange passes through the body's surface - the cavity wall - and at what angle.
        // A thin cutter meeting a faceted wall nearly edge-on runs along it rather than through it.
        var crossing = Crossing(surface, bodyIndex);
        _log.WriteLine($"  flange through the cavity wall: {crossing.Length} faces within 0.3 mm of it; angle to the wall " +
            $"min {Pct(crossing, 0.0):F1}, 5% {Pct(crossing, 0.05):F1}, 25% {Pct(crossing, 0.25):F1}, " +
            $"median {Pct(crossing, 0.5):F1} deg; under 10 deg: {crossing.Count(a => a.Angle < 10)}, under 5: {crossing.Count(a => a.Angle < 5)}");
        // The same profile taken at points spaced round the line, where there is (mostly) no speck: how
        // often the flange crosses the cavity wall more than once anywhere.
        var loop = line.Value.Loops[0];
        int step = Math.Max(1, loop.Count / 60), multiple = 0, sampled = 0;
        var multiAt = new List<Vector3>();
        for (int i = 0; i < loop.Count; i += step)
        {
            sampled++;
            var (crossings, _) = Crossings(loop[i], surfaceIndex, bodyIndex);
            if (crossings > 1) { multiple++; multiAt.Add(loop[i]); }
        }
        _log.WriteLine($"  along the line: {multiple} of {sampled} sampled points cross the cavity wall more than once");
        if (specks.Count == 0) return;
        var cutterIndex = _engine.Spatial.IndexFor(cutter).Value;

        var cutterCrossings = SelfIntersectingFaces.Centroids(cutter);
        var surfaceCrossings = SelfIntersectingFaces.Centroids(surface);
        var rim = BoundaryPoints(surface);
        var partingLine = line.Value.Loops.SelectMany(l => l).ToList();

        foreach (var (speck, volume) in specks)
        {
            var v = speck.Vertices;
            var t = speck.Triangles;
            var c = new Vector3(v.Average(p => p.X), v.Average(p => p.Y), v.Average(p => p.Z));
            var min = new Vector3(v.Min(p => p.X), v.Min(p => p.Y), v.Min(p => p.Z));
            var max = new Vector3(v.Max(p => p.X), v.Max(p => p.Y), v.Max(p => p.Z));

            // Which surface each face of the speck came from: the boolean keeps faces of the mould
            // and of the cutter, so a face lying on neither is a sign the two met badly.
            int onMould = 0, onCutter = 0, onBoth = 0, onNeither = 0;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                var f = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3.0;
                bool m = Math.Abs(mouldIndex.SignedDistance(f)) < 1e-3;
                bool k = Math.Abs(cutterIndex.SignedDistance(f)) < 1e-3;
                if (m && k) onBoth++; else if (m) onMould++; else if (k) onCutter++; else onNeither++;
            }

            var onSurface = surfaceIndex.ClosestPoint(c);
            double fromSurface = onSurface.HasValue ? onSurface.Value.Distance : double.NaN;
            double alongAxis = onSurface.HasValue ? (c - onSurface.Value.Point).Dot(axis) : double.NaN;

            _log.WriteLine(
                $"  speck {volume,9:F4} mm3, {t.Length / 3,3} faces, {Boundary(speck)} open edges, " +
                $"size {max.X - min.X:F2} x {max.Y - min.Y:F2} x {max.Z - min.Z:F2}, at ({c.X:F1}, {c.Y:F1}, {c.Z:F1})");
            _log.WriteLine(
                $"      faces: mould {onMould}, cutter {onCutter}, both {onBoth}, neither {onNeither}");
            _log.WriteLine(
                $"      from mould {mouldIndex.SignedDistance(c),7:F3}  body {bodyIndex.SignedDistance(c),7:F3}  " +
                $"cutter {cutterIndex.SignedDistance(c),7:F3}  flange {fromSurface,7:F3} (along axis {alongAxis,7:F3})");
            _log.WriteLine(
                $"      to flange rim {Nearest(rim, c),6:F2} mm, to parting line {Nearest(partingLine, c),6:F2} mm; " +
                $"crossings within 3 mm: cutter {Within(cutterCrossings, c, 3.0)}, flange {Within(surfaceCrossings, c, 3.0)}");
            var near = crossing.Where(a => a.At.DistanceTo(c) <= 2.0).Select(a => a.Angle).DefaultIfEmpty(double.NaN).ToList();
            _log.WriteLine($"      flange meets the wall within 2 mm at {near.Min():F1} to {near.Max():F1} deg");
            _log.WriteLine($"      flange profile through it: {Profile(c, surfaceIndex, bodyIndex)}");

            if (Environment.GetEnvironmentVariable("FABOLUS_SPECK_IMAGES") is { Length: > 0 } folder)
            {
                // Sliced across the parting line at the speck, and a little either side along it, with
                // the flange axis kept upright so the cutter reads as a thin band.
                var tangent = Tangent(partingLine, c);
                int n = specks.FindIndex(sp => ReferenceEquals(sp.Mesh, speck));
                foreach (double offset in new[] { -0.6, -0.3, 0.0, 0.3, 0.6 })
                {
                    var name = $"{Path.GetFileNameWithoutExtension(file)}-speck{n}-{offset:+0.0;-0.0;0.0}.png";
                    Section(Path.Combine(folder, name), c + tangent * offset, tangent, axis, 3.0,
                        [(mould.Mesh, new SKColor(150, 150, 150), 3f),
                         (body.Mesh, new SKColor(60, 120, 230), 2f),
                         (surface, new SKColor(40, 170, 60), 1f),
                         (cutter, new SKColor(220, 40, 40), 2f),
                         (speck, new SKColor(255, 150, 0), 3f)]);
                }
            }
        }
    }

    /// <summary>
    /// Each flange face within 0.3 mm of the body's surface, with the angle between the two surfaces
    /// there: 0 where the flange lies along the wall, 90 where it passes straight through.
    /// </summary>
    private static (Vector3 At, double Angle)[] Crossing(IMesh surface, ISpatialIndex bodyIndex)
    {
        var v = surface.Vertices;
        var t = surface.Triangles;
        var found = new List<(Vector3, double)>();
        for (int i = 0; i + 2 < t.Length; i += 3)
        {
            var a = v[t[i]]; var b = v[t[i + 1]]; var c = v[t[i + 2]];
            var f = (a + b + c) / 3.0;
            var hit = bodyIndex.ClosestPoint(f);
            if (!hit.HasValue || hit.Value.Distance > 0.3) continue;

            var n = (b - a).Cross(c - a);
            if (n.Length < 1e-12) continue;
            double cos = Math.Abs(n.Normalize().Dot(hit.Value.Normal.Normalize()));
            found.Add((f, 90.0 - Math.Acos(Math.Clamp(cos, 0.0, 1.0)) * 180.0 / Math.PI));
        }
        return [.. found];
    }

    private static double Pct((Vector3 At, double Angle)[] values, double q)
    {
        if (values.Length == 0) return double.NaN;
        var sorted = values.Select(a => a.Angle).OrderBy(a => a).ToArray();
        return sorted[(int)Math.Min(sorted.Length - 1, q * sorted.Length)];
    }

    /// <summary>
    /// Walks the flange through <paramref name="at"/>, across the cavity wall: from 3 mm on one side
    /// to 3 mm on the other, along the flange, in the direction the body's surface faces. Returns how
    /// many times the walk changes side of the body's surface, and where.
    /// </summary>
    private static (int Count, List<double> At) Crossings(Vector3 at, ISpatialIndex surfaceIndex, ISpatialIndex bodyIndex)
    {
        var onFlange = surfaceIndex.ClosestPoint(at);
        if (!onFlange.HasValue) return (0, []);
        var p = onFlange.Value.Point;
        var nf = onFlange.Value.Normal.Normalize();
        var onBody = bodyIndex.ClosestPoint(p);
        if (!onBody.HasValue) return (0, []);
        var nb = onBody.Value.Normal.Normalize();
        var d = nb - nf * nb.Dot(nf);
        if (d.Length < 1e-9) return (0, []);
        d = d.Normalize();

        var at2 = new List<double>();
        int? side = null;
        for (double s = -3.0; s <= 3.0001; s += 0.02)
        {
            var q = surfaceIndex.ClosestPoint(p + d * s);
            if (!q.HasValue) continue;
            double sd = bodyIndex.SignedDistance(q.Value.Point);
            int now = sd < 0 ? -1 : 1;
            if (side is not null && now != side) at2.Add(s);
            side = now;
        }
        return (at2.Count, at2);
    }

    private static string Profile(Vector3 at, ISpatialIndex surfaceIndex, ISpatialIndex bodyIndex)
    {
        var (count, where) = Crossings(at, surfaceIndex, bodyIndex);
        return $"{count} crossing(s) at {string.Join(", ", where.Select(w => w.ToString("F2")))} mm";
    }

    /// <summary>
    /// Two ways of pushing the flange into the cavity where it skims the body's surface, each cut
    /// again and compared with the cut as it is: specks left, the halves' volumes, and whether the
    /// pushed flange folds.
    /// </summary>
    [DiagnosticFact]
    public void PushTheFlangeIntoTheCavity()
    {
        var files = Environment.GetEnvironmentVariable("FABOLUS_SPECK_FILES") is { Length: > 0 } chosen
            ? chosen.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : ["scalp_bolus.stl", "chin_bolus.stl", "nose_bolus.stl", "eye_bolus.stl", "flat_vulva.stl", "larynx_bolus.stl", "test bolus 107mL.stl", "test_smoothed_bolus.stl"];
        foreach (string file in files)
        {
            try { Push(file); }
            catch (Exception e) { _log.WriteLine($"{file}: threw {e.GetType().Name}: {e.Message}"); }
        }
    }

    private void Push(string file)
    {
        var (workspace, bodyId) = GeometryEngineFixture.AddBody(Workspace.CreateEmpty(), _assets.LoadStl(file));
        var built = new GenerateMould(_engine).Execute(workspace, bodyId,
            MouldDefinition.OfShape(MouldShapeType.Concave, 2.5, 5.0) with { TargetMeshId = bodyId });
        if (built.IsFailure) { _log.WriteLine($"{file}: mould - {built.Error.Description}"); return; }
        var mould = MouldMesh.Create(built.Value.GetMesh(bodyId).Value, built.Value.GetRecord(bodyId).Value).Value;
        var feature = new PartingMeshFeature(_engine);
        var line = feature.GeneratePartingLine(mould, Line);
        if (line.IsFailure) { _log.WriteLine($"{file}: line - {line.Error.Description}"); return; }
        var settings = PartingMeshFeature.ResolveAxis(line.Value, Mesh).Value;
        var contour = feature.GenerateOuterContour(mould, settings).Value;
        var body = feature.GetBodyMesh(mould).Value;
        var surface = feature.GenerateFlangeSurface(line.Value, contour, settings, body).Value;
        var bodyIndex = _engine.Spatial.IndexFor(body.Mesh).Value;

        var before = CutReport(feature, mould, surface, settings);
        _log.WriteLine($"=== {file}: as built     {before.Text}");

        // Reactive: only the flange near the specks this cut left, pushed, then cut again - twice,
        // in case pushing uncovers more.
        var reactive = surface;
        var report = before;
        for (int pass = 1; pass <= 3 && report.Specks.Count > 0; pass++)
        {
            var near = report.Specks.SelectMany(sp => sp.Vertices).ToList();
            reactive = PushIn(reactive, bodyIndex, settings.Depth, v => near.Any(p => p.DistanceTo(v) <= 2.0), out int moved);
            report = CutReport(feature, mould, reactive, settings);
            _log.WriteLine($"    reactive pass {pass}: moved {moved,4}  {report.Text}");
        }

        // Proactive: every flange point lying within half a millimetre of the body's surface.
        var proactive = PushIn(surface, bodyIndex, settings.Depth, _ => true, out int all);
        _log.WriteLine($"    proactive:       moved {all,4}  {CutReport(feature, mould, proactive, settings).Text}");
    }

    /// <summary>
    /// Flange vertices that sit just outside the body's surface, or just inside it but not deeper than
    /// the cutter reaches, moved to a cutter's depth plus a margin inside it, along the surface normal.
    /// </summary>
    private IMesh PushIn(IMesh surface, ISpatialIndex bodyIndex, double depth, Func<Vector3, bool> where, out int moved)
    {
        const double reach = 0.5, margin = 0.1;
        var vertices = surface.Vertices.ToBuilder();
        moved = 0;
        for (int i = 0; i < vertices.Count; i++)
        {
            var v = vertices[i];
            double sd = bodyIndex.SignedDistance(v);
            if (sd > reach || sd < -(depth + margin)) continue;
            if (!where(v)) continue;
            var hit = bodyIndex.ClosestPoint(v);
            if (!hit.HasValue) continue;
            vertices[i] = hit.Value.Point - hit.Value.Normal.Normalize() * (depth + margin);
            moved++;
        }
        return _engine.CreateMesh(vertices.ToImmutable(), surface.Triangles, surface.Metadata).Value;
    }

    private sealed record Cut(string Text, List<IMesh> Specks);

    private Cut CutReport(PartingMeshFeature feature, MouldMesh mould, IMesh surface, PartingMeshParameters settings)
    {
        var cutter = feature.ExtrudeFlange(surface, settings);
        if (cutter.IsFailure) return new($"cutter - {cutter.Error.Description}", []);
        var raw = feature.CutMouldWith(mould, cutter.Value);
        if (raw.IsFailure) return new($"cut - {raw.Error.Description}", []);
        var parts = _engine.Evaluators.SeparateComponents(raw.Value).Value
            .Select(c => (Mesh: c, Volume: PartingPieces.Volume(c), Thick: 2.0 * PartingPieces.Volume(c) / Area(c)))
            .OrderByDescending(c => c.Volume)
            .ToList();
        var thick = parts.Where(p => p.Thick >= 1.0).ToList();
        var thin = parts.Where(p => p.Thick < 1.0).ToList();
        return new(
            $"pieces {string.Join(" / ", thick.Select(p => (p.Volume / 1000.0).ToString("F2") + " mL"))}; " +
            $"specks {thin.Count} ({thin.Sum(p => p.Volume):F4} mm3); flange crossings {Count(surface)}, cutter {Count(cutter.Value)}",
            thin.Select(p => p.Mesh).ToList());
    }

    /// <summary>Direction of the parting line at its point nearest <paramref name="c"/>.</summary>
    private static Vector3 Tangent(IReadOnlyList<Vector3> line, Vector3 c)
    {
        int best = 0;
        for (int i = 1; i < line.Count; i++)
            if (line[i].DistanceTo(c) < line[best].DistanceTo(c)) best = i;
        var t = line[(best + 1) % line.Count] - line[(best - 1 + line.Count) % line.Count];
        return t.Normalize();
    }

    /// <summary>
    /// The meshes cut by the plane through <paramref name="at"/> square to <paramref name="normal"/>,
    /// drawn as their outlines over a window <paramref name="half"/> mm either side, with
    /// <paramref name="up"/> (projected into the plane) pointing up the image.
    /// </summary>
    private static void Section(string path, Vector3 at, Vector3 normal, Vector3 up, double half,
        (IMesh Mesh, SKColor Colour, float Width)[] layers)
    {
        const int size = 900;
        var v = (up - normal * up.Dot(normal)).Normalize();
        var u = v.Cross(normal).Normalize();
        double scale = size / (2.0 * half);
        SKPoint ToPixel(Vector3 p)
        {
            var d = p - at;
            return new SKPoint((float)(size / 2 + d.Dot(u) * scale), (float)(size / 2 - d.Dot(v) * scale));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new SKBitmap(size, size);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var grid = new SKPaint { Color = new SKColor(230, 230, 230), StrokeWidth = 1 };
            for (int mm = -(int)half; mm <= (int)half; mm++)
            {
                float g = (float)(size / 2 + mm * scale);
                canvas.DrawLine(g, 0, g, size, grid);
                canvas.DrawLine(0, g, size, g, grid);
            }

            foreach (var (mesh, colour, width) in layers)
            {
                using var paint = new SKPaint { Color = colour, StrokeWidth = width, IsAntialias = true };
                var p = mesh.Vertices;
                var t = mesh.Triangles;
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    var a = p[t[i]]; var b = p[t[i + 1]]; var c = p[t[i + 2]];
                    double da = (a - at).Dot(normal), db = (b - at).Dot(normal), dc = (c - at).Dot(normal);
                    var hits = new List<Vector3>(2);
                    void Edge(Vector3 x, double dx, Vector3 y, double dy)
                    {
                        if ((dx < 0) != (dy < 0)) hits.Add(x + (y - x) * (dx / (dx - dy)));
                    }
                    Edge(a, da, b, db); Edge(b, db, c, dc); Edge(c, dc, a, da);
                    if (hits.Count != 2) continue;
                    if ((hits[0] - at).Length > half * 1.5 && (hits[1] - at).Length > half * 1.5) continue;
                    canvas.DrawLine(ToPixel(hits[0]), ToPixel(hits[1]), paint);
                }
            }

            using var label = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            using var font = new SKFont { Size = 18 };
            canvas.DrawText($"{Path.GetFileNameWithoutExtension(path)}  grid 1 mm  grey mould, blue body, green flange, red cutter, orange speck",
                8, 22, font, label);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    private static double Area(IMesh mesh)
    {
        var v = mesh.Vertices;
        var t = mesh.Triangles;
        double area = 0;
        for (int i = 0; i + 2 < t.Length; i += 3)
            area += (v[t[i + 1]] - v[t[i]]).Cross(v[t[i + 2]] - v[t[i]]).Length / 2.0;
        return area;
    }

    private int Count(IMesh mesh) => _engine.Evaluators.CountSelfIntersections(mesh).Value;

    private static int Within(IEnumerable<Vector3> points, Vector3 c, double radius) =>
        points.Count(p => p.DistanceTo(c) <= radius);

    private static double Nearest(IReadOnlyList<Vector3> points, Vector3 c) =>
        points.Count == 0 ? double.NaN : points.Min(p => p.DistanceTo(c));

    /// <summary>Vertices on edges used by a single face: an open surface's rim.</summary>
    private static List<Vector3> BoundaryPoints(IMesh mesh)
    {
        var edges = EdgeUse(mesh);
        var on = new HashSet<int>();
        foreach (var (edge, uses) in edges)
            if (uses == 1) { on.Add(edge.A); on.Add(edge.B); }
        return on.Select(i => mesh.Vertices[i]).ToList();
    }

    private static int Boundary(IMesh mesh) => EdgeUse(mesh).Count(e => e.Value == 1);

    private static Dictionary<(int A, int B), int> EdgeUse(IMesh mesh)
    {
        var t = mesh.Triangles;
        var uses = new Dictionary<(int, int), int>();
        for (int i = 0; i + 2 < t.Length; i += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                int a = t[i + k], b = t[i + (k + 1) % 3];
                var key = a < b ? (a, b) : (b, a);
                uses[key] = uses.GetValueOrDefault(key) + 1;
            }
        }
        return uses;
    }
}
