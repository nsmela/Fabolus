using System.Collections.Immutable;
using System.Security.Cryptography;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Diagnostics;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Core.Tests.Diagnostics;

/// <summary>
/// Why the two larynx moulds do not split with the view recipe (ExtrusionBorder line, PartingLine
/// axis, MouldLoft sweep, 0.1 mm extrude, severed components), and why the answer changed between runs.
///
/// <para>
/// Run to run: the rim analysis itself repeats exactly on one mesh. The body did not - it is the
/// mould's history replayed, and the smoothing in it comes back different every call
/// (<see cref="SmoothingSteps"/>, <see cref="LevelSetSeqExperiment"/>). Replays are now remembered.
/// </para>
///
/// <para>
/// The split: both bodies have a hole through them (genus 1) and need two parting lines.
/// larynx_large finds both rims, but its outer line climbs 85mm along the axis and, seen along it,
/// crosses itself and does not wind about its own centre - so no loft out to the mould's outline can
/// avoid folding through the body (<see cref="LoftAnatomy"/>), and no sweep or axis in
/// <see cref="RecipeSweep"/> severs it. larynx_small used to be 59% rim band: below the hole the ring
/// narrows to little more than a wall, the fill took a stretch of each skin there as band, and the outer
/// and hole rims came back as one rim whose two contours each ran half round the outside and half round
/// the hole. The ridge fill now gives such pieces back to the skin (<see cref="Regions"/>,
/// <see cref="RimsOverOpenings"/>), so it has two wall rims and two parting loops like larynx_large.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class LarynxParting
{
    private readonly IGeometryEngine _engine;
    private readonly PartingMeshFeature _sut;
    private readonly ITestOutputHelper _out;

    public LarynxParting(GeometryEngineFixture fixture, ITestOutputHelper output)
    {
        _engine = fixture.Engine;
        _sut = new PartingMeshFeature(_engine);
        _out = output;
    }

    [DiagnosticTheory]
    [InlineData("3mf/larynx_small.3mf")]
    [InlineData("3mf/larynx_large.3mf")]
    public void RepeatedRuns(string file)
    {
        _out.WriteLine($"{file}");
        _out.WriteLine("  run | body       | thickness  median | contours           | rims");
        for (int run = 0; run < 4; run++)
        {
            // Re-opened every run, so a difference can come from the import and body recovery too.
            var mould = SavedMoulds.Mould(SavedMoulds.Open(_engine, Asset(file)).Value).Value;
            var body = _sut.GetBodyMesh(mould).Value;

            var thickness = WallThickness.Measure(_engine, body.Mesh, WallThicknessOptions.Default).Value;
            var contours = RidgeDetection.FindRidgeContours(body.Mesh, RidgeDetectionOptions.Default);
            var closed = contours.Where(c => c.IsClosed).ToList();
            var rims = PartingStrategy.Rims(closed, thickness.Statistics.Median);

            string rimText = string.Join(" ", rims.Select(r => $"{r.Kind}[{string.Join(",", r.ContourIndices)}]"));
            _out.WriteLine(
                $"  {run,3} | {Hash(body.Mesh.Vertices.SelectMany(v => new[] { v.X, v.Y, v.Z })),-10} | " +
                $"{Hash(thickness.PerFace),-10} {thickness.Statistics.Median,6:F3} | " +
                $"{contours.Count,2}/{closed.Count,2} {Hash(contours.SelectMany(c => c.Points).SelectMany(v => new[] { v.X, v.Y, v.Z })),-10} | {rimText}");
        }

        // One entry, its body recovered afresh each time, as repeated visits to the parting split do.
        var opened = SavedMoulds.Open(_engine, Asset(file)).Value;
        for (int run = 0; run < 3; run++)
        {
            var body = _sut.GetBodyMesh(SavedMoulds.Mould(opened).Value).Value;
            _out.WriteLine($"  same entry {run} | body {Hash(body.Mesh.Vertices.SelectMany(v => new[] { v.X, v.Y, v.Z }))}");
        }

        // The same mesh object measured again, so the import and body recovery are taken out of it.
        var once = _sut.GetBodyMesh(SavedMoulds.Mould(SavedMoulds.Open(_engine, Asset(file)).Value).Value).Value;
        for (int run = 0; run < 3; run++)
        {
            var thickness = WallThickness.Measure(_engine, once.Mesh, WallThicknessOptions.Default).Value;
            var contours = RidgeDetection.FindRidgeContours(once.Mesh, RidgeDetectionOptions.Default);
            _out.WriteLine(
                $"  same mesh {run} | thickness {Hash(thickness.PerFace)} | contours {contours.Count} " +
                $"{Hash(contours.SelectMany(c => c.Points).SelectMany(v => new[] { v.X, v.Y, v.Z }))}");
        }
    }

    /// <summary>
    /// The body is the mould's history replayed to the Transform stage. Fingerprints the base mesh and
    /// the mesh after each replayed command, several times over, to find the command that answers
    /// differently.
    /// </summary>
    [DiagnosticTheory]
    [InlineData("3mf/larynx_small.3mf")]
    [InlineData("3mf/larynx_large.3mf")]
    public void ReplayedCommands(string file)
    {
        _out.WriteLine(file);
        for (int run = 0; run < 3; run++)
        {
            var opened = new Fabolus.Core.Features.MeshIO.ImportMesh(_engine).Execute(Workspace.CreateEmpty(), Asset(file)).Value;
            var record = opened.GetActiveRecord().Value;
            var mesh = record.BaseMesh!;
            var line = $"  run {run}: mould {Hash(Flat(opened.GetActiveMesh().Value))} base {Hash(Flat(mesh))}";
            foreach (var command in record.Commands.Where(c => c.Priority <= Fabolus.Core.Geometry.Metadata.CommandPriority.Transform))
            {
                mesh = command.Apply(_engine, mesh).Value;
                line += $" -> {command.GetType().Name} {Hash(Flat(mesh))} ({mesh.Vertices.Length}v)";
            }
            _out.WriteLine(line);
        }
    }

    /// <summary>
    /// The smoothing command's three engine calls, each fingerprinted on the same input several
    /// times. "sorted" hashes the vertex positions in sorted order, so a column that changes only in
    /// the plain hash is a reordering of the same geometry rather than different geometry.
    /// </summary>
    [DiagnosticTheory]
    [InlineData("3mf/larynx_small.3mf")]
    [InlineData("3mf/larynx_large.3mf")]
    public void SmoothingSteps(string file)
    {
        var opened = new Fabolus.Core.Features.MeshIO.ImportMesh(_engine).Execute(Workspace.CreateEmpty(), Asset(file)).Value;
        var record = opened.GetActiveRecord().Value;
        var smooth = record.Commands.OfType<Fabolus.Core.Features.Smoothing.SmoothSettings>().Single();
        var input = record.BaseMesh!;
        _out.WriteLine($"{file}: {smooth}");

        var grown = _engine.Modifiers.Offset(input, smooth.Intensity, smooth.Resolution).Value;
        var shrunk = _engine.Modifiers.Offset(grown, -smooth.Intensity, smooth.Resolution).Value;
        var inflated = _engine.Modifiers.Offset(shrunk, smooth.Inflation, smooth.Resolution).Value;
        int target = (int)(input.TriangleCount * Math.Max(smooth.RemeshRatio, 1.0));

        for (int run = 0; run < 4; run++)
        {
            _out.WriteLine(
                $"  run {run}: grow {Both(_engine.Modifiers.Offset(input, smooth.Intensity, smooth.Resolution).Value)}" +
                $" | shrink {Both(_engine.Modifiers.Offset(grown, -smooth.Intensity, smooth.Resolution).Value)}" +
                $" | inflate {Both(_engine.Modifiers.Offset(shrunk, smooth.Inflation, smooth.Resolution).Value)}" +
                $" | decimate {Both(_engine.Modifiers.Decimate(inflated, target).Value)}");
        }

        static string Both(IMesh mesh) =>
            $"{Hash(Flat(mesh))}/{Hash(mesh.Vertices.Select(v => (v.X, v.Y, v.Z)).OrderBy(v => v).SelectMany(v => new[] { v.X, v.Y, v.Z }))} {mesh.VertexCount}v";
    }

    /// <summary>
    /// The strategy report for each body: its genus says how many rims it should have, and the rims
    /// and per-contour separations say how many were found.
    /// </summary>
    [DiagnosticTheory]
    [InlineData("3mf/larynx_small.3mf")]
    [InlineData("3mf/larynx_large.3mf")]
    public void Strategy(string file)
    {
        var mould = SavedMoulds.Mould(SavedMoulds.Open(_engine, Asset(file)).Value).Value;
        var body = _sut.GetBodyMesh(mould).Value;
        var thickness = WallThickness.Measure(_engine, body.Mesh, WallThicknessOptions.Default).Value;
        var report = PartingStrategy.Evaluate(body.Mesh, wallThickness: thickness.Statistics.Median);

        _out.WriteLine($"{file}: {body.Mesh.VertexCount}v {body.Mesh.TriangleCount}t, wall {thickness.Statistics.Median:F2}mm " +
                       $"(unmeasured {thickness.Statistics.UnmeasuredFraction:P0})");
        _out.WriteLine($"  shape {report.Shape}, genus {report.Genus}, closed {report.IsClosed}, cuts needed {report.CutsNeeded}");
        _out.WriteLine($"  closed contours {report.ClosedContours}: separating {report.SeparatingContours}, not {report.NonSeparatingContours}; " +
                       $"combined {report.Combined.SubstantialPieces} pieces ({report.Combined.LargestShare:P0}/{report.Combined.SecondShare:P0})");
        foreach (var s in report.Separations)
            _out.WriteLine($"    contour {s.ContourIndex}: {s.Components} comps, {s.SubstantialPieces} substantial, {s.LargestShare:P0}/{s.SecondShare:P0}");
        foreach (var r in report.Rims)
            _out.WriteLine($"    rim {r.Id}: {r.Kind} contours [{string.Join(",", r.ContourIndices)}] {r.Points} pts spacing {r.Spacing:F2}");
        _out.WriteLine($"  recommended {report.Recommended}, hybrid {report.NeedsHybrid}: {report.Summary}");

        var all = RidgeDetection.FindRidgeContours(body.Mesh, RidgeDetectionOptions.Default);
        foreach (var c in all)
            _out.WriteLine($"    contour rim {c.Rim}: {c.Points.Count} pts, closed {c.IsClosed}, length {Length(c.Points, c.IsClosed):F1}mm");

        static double Length(IReadOnlyList<Vector3> p, bool closed)
        {
            double t = 0;
            for (int i = 0; i + 1 < p.Count; i++) t += p[i].DistanceTo(p[i + 1]);
            return closed && p.Count > 1 ? t + p[^1].DistanceTo(p[0]) : t;
        }
    }

    /// <summary>
    /// The body with its ridge contours and the parting line drawn over it, from every standard view,
    /// then the flange surface built on that line. Written under <c>FABOLUS_TILT_IMAGES</c>.
    /// </summary>
    [DiagnosticTheory]
    [InlineData("3mf/larynx_small.3mf")]
    [InlineData("3mf/larynx_large.3mf")]
    public void DrawContours(string file)
    {
        var directory = Environment.GetEnvironmentVariable("FABOLUS_TILT_IMAGES");
        if (string.IsNullOrWhiteSpace(directory)) { _out.WriteLine("FABOLUS_TILT_IMAGES not set"); return; }

        const int size = 420;
        var options = RenderOptions.Default;
        var lineColour = new Rgb(80, 220, 255);

        var mould = SavedMoulds.Mould(SavedMoulds.Open(_engine, Asset(file)).Value).Value;
        var body = _sut.GetBodyMesh(mould).Value;
        var ridge = RidgeDetection.FindRidge(body.Mesh, RidgeDetectionOptions.Default);
        var contours = ridge.Contours;
        var line = _sut.GeneratePartingLineFromThickness(body);
        var rimShades = new[] { new Rgb(200, 90, 90), new Rgb(90, 170, 90), new Rgb(90, 110, 210), new Rgb(200, 170, 70) };
        var rimIds = ridge.FaceRims.Where(r => r >= 0).Distinct().OrderBy(r => r).ToList();
        _out.WriteLine($"band faces {ridge.Band.Count(b => b)}, ridge faces {ridge.Faces.Count(b => b)}, rims on faces [{string.Join(",", rimIds)}]");
        Rgb? Shade(int face) =>
            face < ridge.FaceRims.Length && ridge.FaceRims[face] >= 0 ? rimShades[rimIds.IndexOf(ridge.FaceRims[face]) % rimShades.Length]
            : face < ridge.Band.Length && ridge.Band[face] ? new Rgb(150, 150, 150)
            : face < ridge.Faces.Length && ridge.Faces[face] ? new Rgb(120, 100, 140)
            : null;
        _out.WriteLine(line.IsSuccess ? $"{line.Value.Loops.Count} loop(s): {string.Join(", ", line.Value.Loops.Select(l => l.Count))} pts" : line.Error.Description);

        var tiles = new List<Tile>();
        foreach (var view in Views.Standard)
        {
            var camera = Camera.Fit(body.Mesh, view, size, size);
            var raster = MeshRasterizer.Render(body.Mesh, camera, size, size, options, f => Shade(f) ?? new Rgb(205, 205, 210));
            for (int i = 0; i < contours.Count; i++)
                MeshRasterizer.DrawPolyline(raster, camera, contours[i].Points, contours[i].IsClosed, MeshRasterizer.ContourColour(i), options);
            if (line.IsSuccess)
                foreach (var loop in line.Value.Loops)
                    MeshRasterizer.DrawPolyline(raster, camera, loop, true, lineColour, options);
            tiles.Add(new Tile(view.Name, raster));
        }

        if (line.IsSuccess)
        {
            var resolved = PartingMeshFeature.ResolveAxis(line.Value, PartingMeshParameters.Default with { AxisSource = PartingMeshAxisSource.PartingLine }).Value;
            var contour = _sut.GenerateOuterContour(mould, resolved).Value;
            var surface = _sut.GenerateFlangeSurface(line.Value, contour, resolved, body);
            if (surface.IsSuccess)
            {
                foreach (var view in new[] { Views.Top, Views.OrbitA, Views.OrbitB })
                {
                    var camera = Camera.Fit(surface.Value, view, size, size);
                    tiles.Add(new Tile($"flange {view.Name}", MeshRasterizer.Render(surface.Value, camera, size, size, options)));
                }
            }
        }

        var name = Path.GetFileNameWithoutExtension(file);
        var path = Path.Combine(directory, $"{name}-contours.png");
        ContactSheet.Save(path, tiles, 4, size, options.Background, $"{name}: ridge contours and parting line",
            Enumerable.Range(0, contours.Count).Select(i => ($"contour {i} (rim {contours[i].Rim})", MeshRasterizer.ContourColour(i)))
                .Append(("parting line", lineColour)).ToList());
        _out.WriteLine(path);
    }

    /// <summary>
    /// The body comes back slightly different every time the mould is opened (the engine's level set is
    /// not deterministic), so the rims are judged over several openings rather than one.
    /// </summary>
    [DiagnosticTheory]
    [InlineData("3mf/larynx_small.3mf", 16)]
    [InlineData("3mf/larynx_large.3mf", 3)]
    public void RimsOverOpenings(string file, int openings)
    {
        int good = 0;
        for (int run = 0; run < openings; run++)
        {
            var mould = SavedMoulds.Mould(SavedMoulds.Open(_engine, Asset(file)).Value).Value;
            var body = _sut.GetBodyMesh(mould).Value;
            var topology = RidgeDetection.MeasureTopology(body.Mesh);
            var contours = RidgeDetection.FindRidgeContours(body.Mesh, RidgeDetectionOptions.Default);
            var closed = contours.Where(c => c.IsClosed).ToList();
            var rims = PartingStrategy.Rims(closed, WallThickness.Measure(_engine, body.Mesh, WallThicknessOptions.Default).Value.Statistics.Median);
            var line = _sut.GeneratePartingLineFromThickness(body);
            int walls = rims.Count(r => r.Kind == PartingRimKind.Wall);
            if (walls == 2 && line.IsSuccess && line.Value.Loops.Count == 2) good++;
            _out.WriteLine($"  {run}: {body.Mesh.VertexCount}v genus {topology.Genus}; contours {contours.Count} ({closed.Count} closed); " +
                           $"rims {string.Join(" ", rims.Select(r => $"{r.Kind}[{string.Join(",", r.ContourIndices)}]"))}; " +
                           $"line {(line.IsSuccess ? string.Join("+", line.Value.Loops.Select(l => l.Count)) : line.Error.Description)}");
        }
        _out.WriteLine($"{file}: {good}/{openings} with two wall rims and a two-loop line");
    }

    /// <summary>
    /// What the ridge fill cut the body into: the two skins with their Euler characteristic (1 for a
    /// disc, 0 for the annulus each skin of a ring should be), and the band groups - one per rim. Images
    /// of the regions, coloured by which skins each meets, are written under <c>FABOLUS_TILT_IMAGES</c>.
    /// </summary>
    [DiagnosticTheory]
    [InlineData("3mf/larynx_small.3mf")]
    [InlineData("3mf/larynx_large.3mf")]
    public void Regions(string file)
    {
        var mould = SavedMoulds.Mould(SavedMoulds.Open(_engine, Asset(file)).Value).Value;
        var body = _sut.GetBodyMesh(mould).Value;
        var diagnosis = RidgeDetection.Diagnose(body.Mesh, RidgeDetectionOptions.Default);
        var t = diagnosis.Territories;
        var mesh = body.Mesh;
        int faces = mesh.TriangleCount;

        var area = new Dictionary<int, double>();
        var count = new Dictionary<int, int>();
        for (int f = 0; f < faces; f++)
        {
            var a = mesh.Vertices[mesh.Triangles[f * 3]];
            var b = mesh.Vertices[mesh.Triangles[(f * 3) + 1]];
            var c = mesh.Vertices[mesh.Triangles[(f * 3) + 2]];
            int r = t.FaceRegion[f];
            area[r] = area.GetValueOrDefault(r) + ((b - a).Cross(c - a).Length * 0.5);
            count[r] = count.GetValueOrDefault(r) + 1;
        }

        _out.WriteLine($"{file}: {faces} faces, {area.Count} regions; surface A = {t.First} ({count[t.First]} faces, {area[t.First]:F0}mm²), " +
                       $"surface B = {t.Second} ({count[t.Second]} faces, {area[t.Second]:F0}mm²)");
        foreach (var g in Enumerable.Range(0, t.RegionIsBand.Length).Where(r => t.RegionIsBand[r]).GroupBy(r => t.RegionBandGroup[r]))
            _out.WriteLine($"  band group {g.Key}: {g.Count()} regions, {g.Sum(r => count.GetValueOrDefault(r))} faces, {g.Sum(r => area.GetValueOrDefault(r)):F0}mm²; " +
                           $"largest {string.Join(" ", g.OrderByDescending(r => area.GetValueOrDefault(r)).Take(6).Select(r => $"{r}:{count.GetValueOrDefault(r)}f/{area.GetValueOrDefault(r):F0}"))}");
        var loose = Enumerable.Range(0, t.RegionIsBand.Length).Where(r => !t.RegionIsBand[r] && r != t.First && r != t.Second).ToList();
        _out.WriteLine($"  loose regions {loose.Count}, {loose.Sum(r => count.GetValueOrDefault(r))} faces, {loose.Sum(r => area.GetValueOrDefault(r)):F0}mm²");

        // Which skins each region meets across an edge, and the skins' own Euler characteristic.
        var edgeFaces = new Dictionary<(int, int), List<int>>();
        for (int f = 0; f < faces; f++)
            for (int c = 0; c < 3; c++)
            {
                int a = mesh.Triangles[(f * 3) + c], b = mesh.Triangles[(f * 3) + ((c + 1) % 3)];
                var key = a < b ? (a, b) : (b, a);
                if (!edgeFaces.TryGetValue(key, out var list)) edgeFaces[key] = list = new List<int>(2);
                list.Add(f);
            }
        var meetsA = new HashSet<int>();
        var meetsB = new HashSet<int>();
        foreach (var pair in edgeFaces.Values.Where(l => l.Count == 2))
        {
            int r0 = t.FaceRegion[pair[0]], r1 = t.FaceRegion[pair[1]];
            if (r0 == t.First) meetsA.Add(r1); if (r1 == t.First) meetsA.Add(r0);
            if (r0 == t.Second) meetsB.Add(r1); if (r1 == t.Second) meetsB.Add(r0);
        }
        foreach (var (label, set) in new[] { ("A", (Func<int, bool>)(r => r == t.First)), ("B", r => r == t.Second),
                                            ("band", r => r >= 0 && r < t.RegionIsBand.Length && t.RegionIsBand[r]) })
        {
            var fs = Enumerable.Range(0, faces).Where(f => set(t.FaceRegion[f])).ToList();
            var vs = fs.SelectMany(f => new[] { mesh.Triangles[f * 3], mesh.Triangles[(f * 3) + 1], mesh.Triangles[(f * 3) + 2] }).Distinct().Count();
            var es = edgeFaces.Count(kv => kv.Value.Any(f => set(t.FaceRegion[f])));
            _out.WriteLine($"  {label}: {fs.Count} faces, chi {vs - es + fs.Count}");
        }
        _out.WriteLine($"  mesh chi {mesh.Vertices.Length - edgeFaces.Count + faces} ({mesh.Vertices.Length}v)");

        var directory = Environment.GetEnvironmentVariable("FABOLUS_TILT_IMAGES");
        if (string.IsNullOrWhiteSpace(directory)) return;

        const int size = 420;
        var options = RenderOptions.Default;

        int biggest = Enumerable.Range(0, t.RegionIsBand.Length).Where(r => t.RegionIsBand[r]).OrderByDescending(r => area.GetValueOrDefault(r)).First();
        Rgb Shade(int f)
        {
            int r = t.FaceRegion[f];
            if (r == t.First) return new Rgb(215, 215, 220);
            if (r == t.Second) return new Rgb(110, 110, 120);
            if (r == biggest) return new Rgb(255, 150, 30);
            if (!t.RegionIsBand[r]) return new Rgb(40, 40, 40);
            bool a = meetsA.Contains(r), b = meetsB.Contains(r);
            return a && b ? new Rgb(200, 60, 220) : a ? new Rgb(230, 60, 60) : b ? new Rgb(60, 90, 240) : new Rgb(60, 170, 90);
        }

        var tiles = new List<Tile>();
        foreach (var view in Views.Standard)
        {
            var camera = Camera.Fit(mesh, view, size, size);
            var raster = MeshRasterizer.Render(mesh, camera, size, size, options, Shade);
            for (int i = 0; i < diagnosis.Contours.Count; i++)
                MeshRasterizer.DrawPolyline(raster, camera, diagnosis.Contours[i].Points, diagnosis.Contours[i].IsClosed, new Rgb(255, 255, 255), options);
            tiles.Add(new Tile(view.Name, raster));
        }

        Rgb Distinct(int f)
        {
            int r = t.FaceRegion[f];
            if (r == t.First) return new Rgb(215, 215, 220);
            if (r == t.Second) return new Rgb(110, 110, 120);
            uint h = (uint)r * 2654435761u;
            return new Rgb((byte)(80 + (h % 160)), (byte)(80 + ((h >> 8) % 160)), (byte)(80 + ((h >> 16) % 160)));
        }
        var big = new List<Tile>();
        foreach (var view in new[] { Views.Right, Views.Left, Views.Top, Views.Bottom, Views.OrbitA, Views.Front })
        {
            var camera = Camera.Fit(mesh, view, 800, 800);
            var raster = MeshRasterizer.Render(mesh, camera, 800, 800, options, Distinct);
            for (int i = 0; i < diagnosis.Contours.Count; i++)
                MeshRasterizer.DrawPolyline(raster, camera, diagnosis.Contours[i].Points, diagnosis.Contours[i].IsClosed, MeshRasterizer.ContourColour(i), options);
            big.Add(new Tile(view.Name, raster));
        }
        ContactSheet.Save(Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(file)}-regions-big.png"), big, 3, 800, options.Background,
            "regions, distinct colour each; A light, B dark", new List<(string, Rgb)>());

        var name = Path.GetFileNameWithoutExtension(file);
        var path = Path.Combine(directory, $"{name}-regions.png");
        ContactSheet.Save(path, tiles, 4, size, options.Background, $"{name}: light A, dark B; band meeting A red, B blue, both purple, neither green; largest band region orange",
            new List<(string, Rgb)>());
        _out.WriteLine(path);
    }

    /// <summary>
    /// Where the mould loft breaks. Per loop: how far its bearings about the loop's own centre run
    /// backwards (the loft forces them forwards, so every backward stretch is folded), then the flange
    /// built from the outer loop alone, the hole patches alone, and both: crossings, and faces that sit
    /// inside the body further out than the bleed is meant to reach.
    /// </summary>
    [DiagnosticTheory]
    [InlineData("3mf/larynx_small.3mf")]
    [InlineData("3mf/larynx_large.3mf")]
    [InlineData("3mf/chin.3mf")]
    [InlineData("3mf/nose.3mf")]
    public void LoftAnatomy(string file)
    {
        var mould = SavedMoulds.Mould(SavedMoulds.Open(_engine, Asset(file)).Value).Value;
        var body = _sut.GetBodyMesh(mould).Value;
        var line = _sut.GeneratePartingLineFromThickness(body).Value;
        var resolved = PartingMeshFeature.ResolveAxis(line, MeshRecipe).Value;
        var axis = resolved.Axis;
        var tools = new PartingTools(_engine);
        using var index = _engine.Spatial.IndexFor(body.Mesh).Value;

        _out.WriteLine($"{file}: axis ({axis.X:F3}, {axis.Y:F3}, {axis.Z:F3}), {line.Loops.Count} loop(s)");
        for (int k = 0; k < line.Loops.Count; k++)
        {
            var loop = line.Loops[k];
            var (back, longest, total) = Backwards(loop, axis);
            double climb = loop.Max(p => p.Dot(axis)) - loop.Min(p => p.Dot(axis));
            _out.WriteLine($"  loop {k}: {loop.Count} pts, climbs {climb:F1}mm; winds {Turning(loop, axis):F0}° about its centre, " +
                           $"crosses itself {SelfCrossings(loop, axis)}x seen along the axis; bearings back at {back} pts, " +
                           $"{total:F0}° backwards in all, longest backward run {longest:F0}°");
        }

        // Whether any axis at all would show the outer loop as a simple, star-shaped curve.
        var best = new List<(double Back, int Crossings, Vector3 Axis)>();
        for (int polar = 0; polar <= 90; polar += 5)
        for (int azimuth = 0; azimuth < 360; azimuth += polar == 0 ? 360 : 10)
        {
            double t = polar * Math.PI / 180, a = azimuth * Math.PI / 180;
            var (u0, v0) = (axis.Cross(Math.Abs(axis.X) < 0.9 ? Vector3.UnitX : Vector3.UnitY).Normalize(), Vector3.Zero);
            v0 = axis.Cross(u0);
            var candidate = ((axis * Math.Cos(t)) + (((u0 * Math.Cos(a)) + (v0 * Math.Sin(a))) * Math.Sin(t))).Normalize();
            best.Add((Backwards(line.Loops[0], candidate).Total, SelfCrossings(line.Loops[0], candidate), candidate));
        }
        foreach (var b in best.OrderBy(b => b.Crossings).ThenBy(b => b.Back).Take(3))
            _out.WriteLine($"  best axis ({b.Axis.X:F2}, {b.Axis.Y:F2}, {b.Axis.Z:F2}) at {Math.Acos(Math.Clamp(b.Axis.Dot(axis), -1, 1)) * 180 / Math.PI:F0}° " +
                           $"from the resolved one: {b.Crossings} crossings, {b.Back:F0}° backwards");

        var outer = tools.GenerateMouldLoftFlangeMesh(line.Loops[0], axis, body.Mesh, mould.Mesh);
        if (outer.IsFailure) { _out.WriteLine($"  outer loft failed: {outer.Error.Description}"); return; }
        _out.WriteLine($"  outer loft:  {Crossings(outer.Value),6} crossings, {Buried(outer.Value, line.Loops[0], index),5} faces buried past the bleed");

        for (int k = 1; k < line.Loops.Count; k++)
        {
            var patch = tools.GenerateHolePatch(line.Loops[k], axis);
            _out.WriteLine(patch.IsFailure
                ? $"  hole patch {k}: failed, {patch.Error.Description}"
                : $"  hole patch {k}: {Crossings(patch.Value),6} crossings, {Buried(patch.Value, line.Loops[k], index),5} faces buried");
        }

        var contour = _sut.GenerateOuterContour(mould, resolved).Value;
        var surface = _sut.GenerateFlangeSurface(line, contour, resolved, body).Value;
        _out.WriteLine($"  combined:    {Crossings(surface),6} crossings");

        int Crossings(IMesh mesh) => _engine.Evaluators.CountSelfIntersections(mesh).Value;
    }

    /// <summary>
    /// Every sweep against both axis sources, on one body and one line so only the recipe changes:
    /// flange crossings, unsealed inner-rim points, and what the cut leaves.
    /// </summary>
    [DiagnosticTheory]
    [InlineData("3mf/larynx_small.3mf")]
    [InlineData("3mf/larynx_large.3mf")]
    [InlineData("3mf/chin.3mf")]
    public void RecipeSweep(string file)
    {
        var mould = SavedMoulds.Mould(SavedMoulds.Open(_engine, Asset(file)).Value).Value;
        var body = _sut.GetBodyMesh(mould).Value;
        var line = _sut.GeneratePartingLineFromThickness(body).Value;
        _out.WriteLine($"{file}: {line.Loops.Count} loop(s)");
        _out.WriteLine("  sweep           axis          | crossings  unsealed | pieces  split");

        foreach (var sweep in Enum.GetValues<PartingMeshSweep>())
        foreach (var source in new[] { PartingMeshAxisSource.PartingLine, PartingMeshAxisSource.PullDirection })
        {
            var recipe = MeshRecipe with { Sweep = sweep, AxisSource = source };
            string row = $"  {sweep,-15} {source,-13} | ";
            try
            {
                var resolved = PartingMeshFeature.ResolveAxis(line, recipe).Value;
                var contour = _sut.GenerateOuterContour(mould, resolved).Value;
                var surface = _sut.GenerateFlangeSurface(line, contour, resolved, body);
                if (surface.IsFailure) { _out.WriteLine(row + surface.Error.Description); continue; }

                int crossings = _engine.Evaluators.CountSelfIntersections(surface.Value).Value;
                var seal = _sut.InspectFlangeSeal(surface.Value, body, line, resolved);
                row += $"{crossings,9} {(seal.IsSuccess ? $"{seal.Value.Count(s => !s.IsSealed)}/{seal.Value.Count}" : "?"),9} | ";

                var cutter = _sut.ExtrudeFlange(surface.Value, resolved);
                var cut = cutter.IsSuccess ? _engine.Booleans.Subtract(mould.Mesh, cutter.Value) : cutter;
                if (cut.IsFailure) { _out.WriteLine(row + "cut failed"); continue; }

                var pieces = _engine.Evaluators.SeparateComponents(cut.Value).Value.Select(Volume).OrderByDescending(v => v).ToArray();
                double total = pieces.Sum();
                row += pieces.Length < 2
                    ? $"{pieces.Length,3}  unsplit"
                    : $"{pieces.Length,3}  {100 * pieces[0] / total:F0}/{100 * pieces[1] / total:F0}";
            }
            catch (Exception e) { row += $"threw {e.GetType().Name}"; }
            _out.WriteLine(row);
        }
    }

    private static double Volume(IMesh mesh)
    {
        var vs = mesh.Vertices;
        var ts = mesh.Triangles;
        double total = 0;
        for (int i = 0; i + 2 < ts.Length; i += 3) total += vs[ts[i]].Dot(vs[ts[i + 1]].Cross(vs[ts[i + 2]])) / 6.0;
        return Math.Abs(total);
    }

    /// <summary>
    /// The hole's parting surface is a patch spanning the hole loop. It used to end exactly on the loop,
    /// on the body's surface, where the outer flange bleeds into the body; the mould material filling
    /// the hole then stayed joined round it. Per opening (the smoothing differs between openings until
    /// GeometryEngine's offset is repeatable): the patch edge's seal without and with the body, and
    /// what the view recipe's cut leaves.
    /// </summary>
    [DiagnosticTheory]
    [InlineData("3mf/larynx_small.3mf")]
    [InlineData("3mf/larynx_large.3mf")]
    public void HoleSeal(string file)
    {
        _out.WriteLine(file);
        var tools = new PartingTools(_engine);
        for (int opening = 0; opening < 8; opening++)
        {
            var mould = SavedMoulds.Mould(SavedMoulds.Open(_engine, Asset(file)).Value).Value;
            var body = _sut.GetBodyMesh(mould).Value;
            var line = _sut.GeneratePartingLineFromThickness(body).Value;
            var resolved = PartingMeshFeature.ResolveAxis(line, MeshRecipe).Value;
            using var index = _engine.Spatial.IndexFor(body.Mesh).Value;

            var row = $"  opening {opening}: {line.Loops.Count} loop(s)";
            for (int k = 1; k < line.Loops.Count; k++)
            {
                row += $" | hole {k} edge outside the body: on the surface {Outside(tools.GenerateHolePatch(line.Loops[k], resolved.Axis).Value)}" +
                       $", bled {Outside(tools.GenerateHolePatch(line.Loops[k], resolved.Axis, body.Mesh).Value)}";
            }

            // The same outer flange both times; only the hole patches differ.
            var outer = tools.GenerateMouldLoftFlangeMesh(line.Loops[0], resolved.Axis, body.Mesh, mould.Mesh).Value;
            var onSurface = line.Loops.Skip(1).Select(l => tools.GenerateHolePatch(l, resolved.Axis).Value).Prepend(outer);
            var bled = line.Loops.Skip(1).Select(l => tools.GenerateHolePatch(l, resolved.Axis, body.Mesh).Value).Prepend(outer);
            row += $" | outer crossings {_engine.Evaluators.CountSelfIntersections(outer).Value}" +
                   $" | cut, patch on the surface: {Cut(onSurface)}; bled: {Cut(bled)}";
            _out.WriteLine(row);

            string Cut(IEnumerable<IMesh> parts)
            {
                var list = parts.ToList();
                var vertices = list.SelectMany(m => m.Vertices).ToImmutableArray();
                var triangles = new List<int>();
                int offset = 0;
                foreach (var m in list) { triangles.AddRange(m.Triangles.Select(t => t + offset)); offset += m.VertexCount; }
                var surface = _engine.CreateMesh(vertices, triangles.ToImmutableArray(), list[0].Metadata).Value;

                var cutter = _sut.ExtrudeFlange(surface, resolved);
                var cut = cutter.IsSuccess ? _engine.Booleans.Subtract(mould.Mesh, cutter.Value) : cutter;
                if (cut.IsFailure) return "failed";
                var pieces = _engine.Evaluators.SeparateComponents(cut.Value).Value.Select(Volume).OrderByDescending(v => v).ToArray();
                double total = pieces.Sum();
                return $"{pieces.Length} pieces " + string.Join("/", pieces.Take(2).Select(v => $"{100 * v / total:F0}"));
            }

            string Outside(IMesh patch)
            {
                // The patch's edge: vertices on exactly one boundary edge pair.
                var edges = new Dictionary<(int, int), int>();
                var ts = patch.Triangles;
                for (int f = 0; f + 2 < ts.Length; f += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = ts[f + e], b = ts[f + ((e + 1) % 3)];
                    var key = a < b ? (a, b) : (b, a);
                    edges[key] = edges.GetValueOrDefault(key) + 1;
                }
                var rim = edges.Where(e => e.Value == 1).SelectMany(e => new[] { e.Key.Item1, e.Key.Item2 }).Distinct().ToList();
                var depth = rim.Select(v => index.SignedDistance(patch.Vertices[v])).ToArray();
                return $"{depth.Count(d => d >= 0)}/{depth.Length} (deepest {depth.Min():F2}, worst {depth.Max():F2}mm)";
            }
        }
    }

    /// <summary>
    /// Opens larynx_small until the view recipe's cut fails to part it (about one opening in sixteen
    /// with the hole patch sealed), then tries the cut again with one thing changed at a time.
    /// </summary>
    [DiagnosticFact]
    public void FailingOpening()
    {
        var tools = new PartingTools(_engine);
        MouldMesh current = null!;
        for (int opening = 0; opening < 60; opening++)
        {
            var mould = SavedMoulds.Mould(SavedMoulds.Open(_engine, Asset("3mf/larynx_small.3mf")).Value).Value;
            current = mould;
            var body = _sut.GetBodyMesh(mould).Value;
            var line = _sut.GeneratePartingLineFromThickness(body).Value;
            var resolved = PartingMeshFeature.ResolveAxis(line, MeshRecipe).Value;
            if (line.Loops.Count != 2) { _out.WriteLine($"opening {opening}: {line.Loops.Count} loop(s)"); continue; }

            var outer = tools.GenerateMouldLoftFlangeMesh(line.Loops[0], resolved.Axis, body.Mesh, mould.Mesh).Value;
            var patch = tools.GenerateHolePatch(line.Loops[1], resolved.Axis, body.Mesh).Value;
            string baseline = Cut(new[] { outer, patch }, resolved);
            if (!baseline.Contains("100/0")) continue;

            _out.WriteLine($"opening {opening} fails: loops {line.Loops[0].Count}/{line.Loops[1].Count} pts; {baseline}");
            using var index = _engine.Spatial.IndexFor(body.Mesh).Value;
            var seal = tools.InspectFlangeSeal(outer, body.Mesh, resolved.Axis, line.Loops[0]).Value;
            _out.WriteLine($"  outer flange: {_engine.Evaluators.CountSelfIntersections(outer).Value} crossings, " +
                           $"{seal.Count(p => !p.IsSealed)}/{seal.Count} inner-rim points outside (worst {seal.Max(p => p.SignedDistance):F2}mm); " +
                           $"patch {_engine.Evaluators.CountSelfIntersections(patch).Value} crossings");
            foreach (var (i, p) in seal.Select((p, i) => (i, p)).Where(x => !x.p.IsSealed))
                _out.WriteLine($"    outside at ({p.Position.X:F1}, {p.Position.Y:F1}, {p.Position.Z:F1}) by {p.SignedDistance:F2}mm");

            foreach (double depth in new[] { 0.2, 0.5, 1.0 })
                _out.WriteLine($"  extruded {depth}mm: {Cut(new[] { outer, patch }, resolved with { Depth = depth })}");
            _out.WriteLine($"  outer flange alone: {Cut(new[] { outer }, resolved)}; patch alone: {Cut(new[] { patch }, resolved)}");

            // The loft's rim pushed inside wherever it is not.
            var verts = outer.Vertices.ToArray();
            int pushed = 0;
            foreach (var p in seal.Where(p => !p.IsSealed))
            {
                int v = Array.FindIndex(verts, q => q.DistanceTo(p.Position) < 1e-9);
                if (v < 0) continue;
                var hit = index.ClosestPoint(verts[v]);
                if (!hit.HasValue) continue;
                verts[v] = verts[v] + ((hit.Value.Point - verts[v]) * 2.0);   // mirrored through the surface
                pushed++;
            }
            var sealedOuter = _engine.CreateMesh(verts.ToImmutableArray(), outer.Triangles, outer.Metadata).Value;
            _out.WriteLine($"  outer rim pushed in ({pushed} points): {Cut(new[] { sealedOuter, patch }, resolved)}");
            return;
        }
        _out.WriteLine("no failing opening in 60");

        string Cut(IEnumerable<IMesh> parts, PartingMeshParameters resolved)
        {
            var list = parts.ToList();
            var vertices = list.SelectMany(m => m.Vertices).ToImmutableArray();
            var triangles = new List<int>();
            int offset = 0;
            foreach (var m in list) { triangles.AddRange(m.Triangles.Select(t => t + offset)); offset += m.VertexCount; }
            var surface = _engine.CreateMesh(vertices, triangles.ToImmutableArray(), list[0].Metadata).Value;
            var cutter = _sut.ExtrudeFlange(surface, resolved);
            if (cutter.IsFailure) return "extrude failed";
            var cut = _engine.Booleans.Subtract(current.Mesh, cutter.Value);
            if (cut.IsFailure) return "cut failed";
            var pieces = _engine.Evaluators.SeparateComponents(cut.Value).Value.Select(Volume).OrderByDescending(v => v).ToArray();
            double total = pieces.Sum();
            return $"{pieces.Length} pieces " + string.Join("/", pieces.Take(2).Select(v => $"{100 * v / total:F0}"));
        }
    }

    /// <summary>Faces whose centre is more than 1 mm inside the body and more than 4 mm from the loop.</summary>
    private static int Buried(IMesh mesh, IReadOnlyList<Vector3> loop, GeometryEngine.Core.Geometry.ISpatialIndex index)
    {
        int count = 0;
        var vs = mesh.Vertices;
        var ts = mesh.Triangles;
        for (int f = 0; f + 2 < ts.Length; f += 3)
        {
            var c = (vs[ts[f]] + vs[ts[f + 1]] + vs[ts[f + 2]]) / 3.0;
            if (index.SignedDistance(c) > -1.0) continue;
            if (loop.Min(p => p.DistanceTo(c)) < 4.0) continue;
            count++;
        }
        return count;
    }

    private static (Vector3 U, Vector3 V) Frame(Vector3 axis)
    {
        var seed = Math.Abs(axis.Dot(Vector3.UnitY)) < 0.9 ? Vector3.UnitY : Vector3.UnitX;
        var u = seed.Cross(axis).Normalize();
        return (u, axis.Cross(u));
    }

    private static double Turning(IReadOnlyList<Vector3> loop, Vector3 axis)
    {
        var (u, v) = Frame(axis);
        double cx = loop.Average(p => p.Dot(u)), cy = loop.Average(p => p.Dot(v));
        double total = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            var a = loop[i]; var b = loop[(i + 1) % loop.Count];
            double d = Math.Atan2(b.Dot(v) - cy, b.Dot(u) - cx) - Math.Atan2(a.Dot(v) - cy, a.Dot(u) - cx);
            while (d > Math.PI) d -= Math.Tau; while (d < -Math.PI) d += Math.Tau;
            total += d;
        }
        return total * 180 / Math.PI;
    }

    /// <summary>How many times the loop crosses itself, projected along the axis.</summary>
    private static int SelfCrossings(IReadOnlyList<Vector3> loop, Vector3 axis)
    {
        var (u, v) = Frame(axis);
        var p = loop.Select(q => (X: q.Dot(u), Y: q.Dot(v))).ToArray();
        int n = p.Length, count = 0;
        for (int i = 0; i < n; i++)
        for (int j = i + 2; j < n; j++)
        {
            if (i == 0 && j == n - 1) continue;
            var (a, b, c, d) = (p[i], p[(i + 1) % n], p[j], p[(j + 1) % n]);
            double d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
            if (((d1 > 0) != (d2 > 0)) && ((d3 > 0) != (d4 > 0))) count++;
        }
        return count;

        static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
            ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));
    }

    private static (int Points, double Longest, double Total) Backwards(IReadOnlyList<Vector3> loop, Vector3 axis)
    {
        var seed = Math.Abs(axis.Dot(Vector3.UnitY)) < 0.9 ? Vector3.UnitY : Vector3.UnitX;
        var u = seed.Cross(axis).Normalize();
        var v = axis.Cross(u);
        double cx = loop.Average(p => p.Dot(u)), cy = loop.Average(p => p.Dot(v));
        var bearing = loop.Select(p => Math.Atan2(p.Dot(v) - cy, p.Dot(u) - cx)).ToArray();

        int n = loop.Count;
        var steps = new double[n];
        double turning = 0;
        for (int i = 0; i < n; i++) { steps[i] = Wrap(bearing[(i + 1) % n] - bearing[i]); turning += steps[i]; }
        double winding = turning >= 0 ? 1 : -1;

        int points = 0;
        double total = 0, run = 0, longest = 0;
        for (int i = 0; i < 2 * n; i++)
        {
            double s = winding * steps[i % n];
            if (s < 0)
            {
                if (i < n) { points++; total -= s; }
                run -= s;
                longest = Math.Max(longest, run);
            }
            else run = Math.Max(0, run - s);   // a backward stretch ends once the line has caught up
        }
        return (points, longest * 180 / Math.PI, total * 180 / Math.PI);

        static double Wrap(double a) { while (a > Math.PI) a -= Math.Tau; while (a < -Math.PI) a += Math.Tau; return a; }
    }

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

    private static IEnumerable<double> Flat(IMesh mesh) =>
        mesh.Vertices.SelectMany(v => new[] { v.X, v.Y, v.Z }).Concat(mesh.Triangles.Select(t => (double)t));

    private static string Hash(IEnumerable<double> values)
    {
        var bytes = values.SelectMany(BitConverter.GetBytes).ToArray();
        return Convert.ToHexString(SHA256.HashData(bytes))[..10];
    }

    private static string Asset(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "files")))
            dir = dir.Parent;

        return Path.Combine(dir!.FullName, "tests", "files", name);
    }
}
