using System.Numerics;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// Four ways of centring the parting line in the rim wall, run on the same band and judged on the
/// same numbers.
///
/// <para>
/// The measurement that matters is <c>across</c>: a signed coordinate over the wall, 0 on one crease
/// and 1 on the other. A ratio of distances cannot serve here because it folds at the creases - a
/// point just inside one and a point well beyond it both read the same - so a method that overshoots
/// would score as though it had not.
/// </para>
///
/// <para>
/// Beside it, the quantities a parting line is actually for: how sharply it turns, how evenly it is
/// sampled, how close it comes to itself, and whether it stayed on the body. A curve can sit exactly
/// down the middle and still be useless to the flange swept along it.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class CentringMethodComparison
{
    private const int Panel = 780;
    private static readonly Rgb Background = new(24, 26, 32);
    private static readonly Rgb Wall = new(94, 110, 171);
    private static readonly Rgb Plain = new(190, 193, 200);
    private static readonly Rgb Crease = new(198, 76, 255);
    private static readonly Rgb Suspect = new(240, 140, 40);

    private static readonly (string Name, Rgb Colour)[] Palette =
    {
        ("traced", new Rgb(255, 170, 60)),
        ("current", new Rgb(255, 235, 60)),
        ("tail only", new Rgb(120, 255, 120)),
        ("harmonic", new Rgb(64, 224, 208)),
        ("geodesic", new Rgb(255, 90, 190)),
        ("cross-section", new Rgb(150, 170, 255)),
        ("geodesic bridged", new Rgb(120, 255, 160)),
        ("PRODUCTION", new Rgb(255, 235, 60)),
        ("crease-repaired", new Rgb(90, 255, 220)),
        ("normal-split", new Rgb(255, 120, 90)),
        ("straightened", new Rgb(120, 220, 255)),
        ("crease-offset", new Rgb(120, 255, 120)),
        ("crease-offset constant", new Rgb(255, 226, 40)),
        ("crease-offset raw", new Rgb(255, 110, 110)),
    };

    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;
    private readonly ITestOutputHelper _log;

    public CentringMethodComparison(GeometryEngineFixture assets, ITestOutputHelper log)
    {
        _assets = assets;
        _engine = assets.Engine;
        _log = log;
    }

    [Fact]
    public void CompareTheWaysOfFindingTheMiddle()
    {
        string? directory = Environment.GetEnvironmentVariable("FABOLUS_RIDGE_REPORT_DIR");

        foreach (var (id, asset) in PartingLineCentringSweep.Models)
        {
            var imported = _engine.IO.Import(_assets.GetAssetPath(asset));
            var mould = MouldMesh.Create(imported.Value);
            Compare(id, new PartingMeshFeature(_engine).GetBodyMesh(mould.Value).Value.Mesh, directory);
        }
    }

    /// <summary>
    /// The same comparison on the STL bodies the parting-solid tests actually run on. A separate set,
    /// and the reason a promotion went wrong twice: a method can win on every 3mf body and lose on
    /// these, and the suite only ever judges these.
    /// </summary>
    [Fact]
    public void CompareOnTheStlBodies()
    {
        string? directory = Environment.GetEnvironmentVariable("FABOLUS_RIDGE_REPORT_DIR");

        foreach (string file in PartingLineCentringSweep.Bodies)
            Compare(Path.GetFileNameWithoutExtension(file), _assets.LoadStl(file), directory);
    }

    private void Compare(string id, IMesh body, string? directory)
    {
        {
            var thickness = _engine.Evaluators.MeasureWallThickness(body, WallThicknessOptions.Default);
            if (thickness.IsFailure) { _log.WriteLine($"{id}: no thickness"); return; }

            var projector = _engine.PartingTools.CreateSurfaceProjector(body);
            var surface = projector.IsSuccess ? projector.Value : null;

            var traced = ThicknessParting.Trace(
                body, thickness.Value, ThicknessPartingOptions.Default, surface);
            if (traced.IsFailure) { _log.WriteLine($"{id}: no seam"); return; }

            float wall = thickness.Value.Statistics.Median;
            var ridge = RidgeDetection.FindRidge(body, RidgeDetectionOptions.Default);
            var contours = ridge.Contours.Where(c => c.IsClosed).ToList();
            var rims = PartingStrategy.Rims(contours, wall);
            var walls = rims.Where(r => r.Kind == PartingRimKind.Wall).ToList();

            if (walls.Count == 0) { _log.WriteLine($"{id}: no wall rim, nothing to centre in"); return; }

            var bands = walls
                .Select(r => new PartingBand(contours[r.ContourIndices[0]], contours[r.ContourIndices[1]]))
                .ToList();

            var shaded = ridge.Band.Length == ridge.Faces.Length ? ridge.Band : ridge.Faces;

            // One band surface per wall rim, so a field solved over it cannot run from one rim's
            // crease to another's.
            var surfaces = new List<BandSurface>();
            for (int i = 0; i < walls.Count; i++)
            {
                var built = BandSurface.Build(body, shaded, ridge.FaceRims, walls[i].Id, bands[i]);
                if (built is not null) surfaces.Add(built);
            }

            if (surfaces.Count == 0) { _log.WriteLine($"{id}: band surface would not build"); return; }

            float spacing = Spacing(traced.Value);

            var candidates = new List<(string Name, PartingLine Line)>
            {
                ("traced", traced.Value),
                ("current", PartingLineCentring.Centre(
                    traced.Value, bands, PartingLineCentringOptions.Default, surface)),
                ("tail only", PartingLineCentring.Centre(
                    traced.Value, bands,
                    PartingLineCentringOptions.Default with { DeadZone = 0.15f, Ramp = 0.05f, Strength = 0.7f },
                    surface)),
                ("harmonic", CentringMethods.Finish(
                    surfaces.SelectMany(CentringMethods.Harmonic).ToList(), spacing, surface)),
                ("geodesic", CentringMethods.Finish(
                    surfaces.SelectMany(CentringMethods.GeodesicMedial).ToList(), spacing, surface)),
                ("cross-section", CentringMethods.Finish(
                    surfaces.SelectMany(s => CentringMethods.CrossSections(s)).ToList(), spacing, surface)),
            };

            // The width check, and the stretches it rules out. Solved on what is left and bridged
            // across the rest, so a pinch stops the field being taken through it rather than stopping
            // the method.
            var suspect = new bool[body.Triangles.Length / 3];
            var dropped = new bool[body.Triangles.Length / 3];
            var bridged = new List<Vector3[]>();
            foreach (var s in surfaces)
            {
                bridged.AddRange(CentringMethods.GeodesicBridged(s, out var flagged, out var skipped));
                for (int f = 0; f < suspect.Length && f < flagged.Length; f++)
                {
                    if (flagged[f]) suspect[f] = true;
                    if (skipped[f]) dropped[f] = true;
                }
            }

            candidates.Add(("geodesic bridged", CentringMethods.Finish(bridged, spacing, surface)));

            // Straightening applied to whatever the feature currently produces, so the comparison is
            // "the shipped line" against "the shipped line made as straight as the wall allows".
            var toStraighten = new PartingMeshFeature(_engine)
                .GeneratePartingLineFromThickness(BodyMesh.Create(body).Value);

            if (toStraighten.IsSuccess)
            {
                var straightened = new List<Vector3[]>();
                foreach (var loop in toStraighten.Value.Loops)
                {
                    var pick = Nearest(loop[0], bands);
                    straightened.Add(CentringMethods.Straighten(loop, pick, surface));
                }
                candidates.Add(("straightened", new PartingLine(straightened)));
            }

            candidates.Add(("normal-split", CentringMethods.Finish(
                surfaces.SelectMany(CentringMethods.NormalSplit).ToList(), spacing, surface)));

            // The crease offset: one crease of each wall, chosen for steadiness, walked half a width
            // across. Reported per rim because which crease it trusted is the whole of the method, and
            // a body where it picks the faint one is a body where the number to look at is that choice
            // rather than the curve.
            foreach (var (label, offsetOptions) in new[]
            {
                ("crease-offset", CreaseOffsetOptions.Default),
                ("crease-offset constant", CreaseOffsetOptions.Default with { Constancy = 1f }),
                ("crease-offset raw", CreaseOffsetOptions.Default with { SmoothingPasses = 0 }),
            })
            {
                var offsetLoops = new List<IReadOnlyList<Vector3>>();
                foreach (var pair in bands)
                {
                    var loop = CreaseOffsetLine.Trace(body, pair, out var built, surface, offsetOptions);
                    if (loop is not null) offsetLoops.Add(loop);

                    if (label != "crease-offset") continue;
                    if (built is null)
                    {
                        _log.WriteLine($"  offset: no report at all (span {pair.Span:F2} mm)");
                        continue;
                    }

                    if (loop is null) _log.WriteLine($"  offset: REFUSED this rim");

                    string Stops(IReadOnlyDictionary<WalkStop, int> counts) => string.Join(
                        " ", counts.OrderByDescending(c => c.Value).Select(c => $"{c.Key}={c.Value}"));

                    _log.WriteLine(
                        $"  offset base: crease {built.Base}  crossing {built.Crossing:F2} mm " +
                        $"(p5 {built.Narrowest:F2} p95 {built.Widest:F2}, " +
                        $"p95/med {built.Widest / built.Crossing:F2}x)  " +
                        $"offset {built.Offset:F2} mm  reached {(float)built.Reached / built.Samples:P1}  " +
                        $"clamped {(float)built.Clamped / built.Samples:P1}  " +
                        $"short {(float)built.Short / built.Samples:P1}  |  " +
                        $"first turn-p95 {built.First.TurnP95:F1} var {built.First.WidthVariation:F2} " +
                        $"score {built.First.Score:F2}  /  second turn-p95 {built.Second.TurnP95:F1} " +
                        $"var {built.Second.WidthVariation:F2} score {built.Second.Score:F2}");
                    _log.WriteLine(
                        $"  offset stops: crossing [{Stops(built.Crossings)}]  " +
                        $"laying [{Stops(built.Offsets)}]");
                }

                if (offsetLoops.Count == bands.Count) candidates.Add((label, new PartingLine(offsetLoops)));
            }

            // The creases repaired against each other first, then the same medial solve. Where a wall
            // reads thick and one of its two creases is faint, the faint one is rebuilt at the expected
            // thickness from the confident one - so the band handed to the solve is the wall rather
            // than the wall plus wherever a weak crease drifted to.
            var folds = new CreaseCertainty.FoldIndex(body, CreaseCertainty.Folds(body));
            var repairedLoops = new List<Vector3[]>();
            int repairedStations = 0;

            for (int i = 0; i < walls.Count; i++)
            {
                var (a, b, count) = CreaseRepair.Repair(
                    contours[walls[i].ContourIndices[0]], contours[walls[i].ContourIndices[1]],
                    folds, surface);
                repairedStations += count;

                var line = BandMedialLine.Trace(
                    body, shaded, ridge.FaceRims, walls[i].Id, new PartingBand(a, b),
                    BandMedialOptions.Default with { FewestFacesAcross = 0f, MostPinched = 1f },
                    surface);

                if (line is not null) repairedLoops.Add(line.ToArray());
            }

            _log.WriteLine($"  crease repair: {repairedStations} station(s) rebuilt");
            if (repairedLoops.Count == walls.Count)
                candidates.Add(("crease-repaired", new PartingLine(repairedLoops)));

            // What the feature actually hands back, so the comparison includes the shipped answer
            // rather than a reconstruction of it. On a body the medial line suits this should match
            // "geodesic"; where it is refused it should match "current".
            var shipped = new PartingMeshFeature(_engine)
                .GeneratePartingLineFromThickness(BodyMesh.Create(body).Value);
            if (shipped.IsSuccess) candidates.Add(("PRODUCTION", shipped.Value));

            // The width distribution itself, because "out of tolerance" only means anything against
            // what the rest of the wall is doing, and a threshold that flags nothing may be loose
            // rather than the wall being even.
            foreach (var s in surfaces)
            {
                var w = CentringMethods.Width(s);
                var measured = s.FaceList.Where(f => !float.IsPositiveInfinity(w[f]))
                    .Select(f => w[f]).OrderBy(v => v).ToArray();
                if (measured.Length == 0) continue;

                float med = measured[measured.Length / 2];
                float[] ratio = measured.Select(v => v / med).ToArray();

                _log.WriteLine(
                    $"  width mm: med {med:F2}  p5 {measured[(int)(measured.Length * 0.05f)]:F2}  " +
                    $"p95 {measured[(int)(measured.Length * 0.95f)]:F2}  " +
                    $"min {measured[0]:F2}  max {measured[^1]:F2}");
                _log.WriteLine(
                    $"  width / median: p95 {ratio[(int)(ratio.Length * 0.95f)]:F2}  " +
                    $"p99 {ratio[(int)(ratio.Length * 0.99f)]:F2}  max {ratio[^1]:F2}  |  " +
                    $"over 1.2x {ratio.Count(r => r > 1.2f) * 100f / ratio.Length:F1}%  " +
                    $"over 1.4x {ratio.Count(r => r > 1.4f) * 100f / ratio.Length:F1}%  " +
                    $"over 1.6x {ratio.Count(r => r > 1.6f) * 100f / ratio.Length:F1}%  " +
                    $"under 0.6x {ratio.Count(r => r < 0.6f) * 100f / ratio.Length:F1}%");
            }

            // How many faces span the wall. This is what decides whether a level set through the band
            // can resolve anything: the field is sampled at vertices, so a band three faces across
            // carries three or four distinct values and the 0.5 contour through them is quantisation
            // rather than geometry.
            foreach (var s4 in surfaces)
            {
                var w = CentringMethods.Width(s4);
                var measured = s4.FaceList.Where(f => !float.IsPositiveInfinity(w[f]))
                    .Select(f => w[f]).OrderBy(v => v).ToArray();
                if (measured.Length == 0) continue;

                var verts = body.Vertices;
                var tris = body.Triangles;
                double edge = 0d; int edges = 0;
                foreach (int f in s4.FaceList)
                    for (int e = 0; e < 3; e++)
                    {
                        edge += Vector3.Distance(
                            verts[tris[(f * 3) + e]], verts[tris[(f * 3) + ((e + 1) % 3)]]);
                        edges++;
                    }

                float meanEdge = edges == 0 ? 1f : (float)(edge / edges);
                float median = measured[measured.Length / 2];
                _log.WriteLine(
                    $"  wall {median:F2} mm over mean edge {meanEdge:F2} mm = " +
                    $"{median / meanEdge:F1} faces across");
            }

            // Three readings of the same wall, to find out whether the band is uneven or only the
            // measure of it is. Geodesic sums two walks through the band graph, so it inflates wherever
            // the path curves or detours round a notch. Chord sums two straight lines, so it inflates
            // where the point sits off the line joining its two nearest crease points. Separation
            // measures the two creases against each other and does not involve the face at all - if
            // that one is flat where the others spike, the wall is even and the fault is in the ruler.
            foreach (var s3 in surfaces)
            {
                var geodesic = CentringMethods.Width(s3);
                var chord = new List<float>();
                var separation = new List<float>();
                var geo = new List<float>();

                var verts = body.Vertices;
                var tris = body.Triangles;

                foreach (int f in s3.FaceList)
                {
                    if (float.IsPositiveInfinity(geodesic[f])) continue;

                    var centre = (verts[tris[f * 3]] + verts[tris[(f * 3) + 1]] + verts[tris[(f * 3) + 2]]) / 3f;
                    var a = PartingBand.Closest(centre, s3.Band.First);
                    var b = PartingBand.Closest(centre, s3.Band.Second);

                    geo.Add(geodesic[f]);
                    chord.Add(a.Distance + b.Distance);
                    separation.Add(Vector3.Distance(a.Point, b.Point));
                }

                if (geo.Count == 0) continue;
                _log.WriteLine($"  width rulers (median, p95/med, max/med, over 1.25x):");
                Ruler("    geodesic  ", geo.ToArray());
                Ruler("    chord     ", chord.ToArray());
                Ruler("    separation", separation.ToArray());
            }

            // Does the body account for the wide stretches? A rim wall is the shell's own thickness
            // seen edge-on, so where the shell genuinely thickens the band must widen with it - that is
            // the body being thick, not the detector being wrong. A band that widens while the wall
            // beside it holds steady is the opposite, and the only one of the two worth fixing.
            foreach (var s2 in surfaces)
            {
                var w = CentringMethods.Width(s2);
                var flagged = CentringMethods.OutOfTolerance(s2, w);

                var wide = new List<float>();
                var normal = new List<float>();

                foreach (int f in s2.FaceList)
                {
                    if (float.IsPositiveInfinity(w[f])) continue;
                    float local = LocalThickness(body, thickness.Value, f);
                    if (float.IsPositiveInfinity(local)) continue;
                    (flagged[f] ? wide : normal).Add(local);
                }

                if (wide.Count == 0 || normal.Count == 0) continue;

                float wideMedian = Median(wide.ToArray());
                float normalMedian = Median(normal.ToArray());
                _log.WriteLine(
                    $"  wall beside the band: in tolerance {normalMedian:F2} mm (n {normal.Count}), " +
                    $"out of tolerance {wideMedian:F2} mm (n {wide.Count})  " +
                    $"ratio {wideMedian / MathF.Max(normalMedian, 1e-6f):F2}");
            }

            int suspectCount = suspect.Count(x => x);
            int droppedCount = dropped.Count(x => x);
            int bandCount = shaded.Count(x => x);
            _log.WriteLine($"=== {id}   wall {wall:F2} mm, {walls.Count} wall rim(s), " +
                           $"out of tolerance {(float)suspectCount / MathF.Max(bandCount, 1):P1}, " +
                           $"pinched and skipped {(float)droppedCount / MathF.Max(bandCount, 1):P1}");

            foreach (var (name, line) in candidates)
                _log.WriteLine($"  {name,-14} {Describe(line, bands, surface)}");

            if (directory is not null)
                Draw(Path.Combine(directory, id), id, body, shaded, suspect, contours, candidates);
        }
    }

    // ---------------------------------------------------------------- measurement

    private static string Describe(
        PartingLine line, IReadOnlyList<PartingBand> bands, ISurfaceProjector? projector)
    {
        if (line.Loops.Count == 0) return "produced no loop";

        var across = new List<float>();
        foreach (var loop in line.Loops)
            foreach (var point in loop)
                across.Add(Across(point, Nearest(point, bands)));

        var t = across.ToArray();
        int off = t.Count(v => v < 0.35f || v > 0.65f);
        int outside = t.Count(v => v < 0f || v > 1f);

        // How near the line comes to a crease at its nearest, as a share of the way across. The
        // percentiles cannot answer this and the question is not statistical: the flange rim is offset
        // from the line and stops sealing before the line reaches a crease, so one point too close is
        // a leak whatever the other four hundred are doing.
        float nearest = t.Length == 0 ? 0f : t.Min(v => MathF.Min(v, 1f - v));

        var turns = new List<float>();
        float clearance = float.PositiveInfinity;
        float spread = 0f;
        float lift = 0f;

        foreach (var loop in line.Loops)
        {
            int n = loop.Count;
            if (n < 16) continue;

            for (int i = 0; i < n; i++)
            {
                var incoming = loop[i] - loop[(i - 1 + n) % n];
                var outgoing = loop[(i + 1) % n] - loop[i];
                if (incoming.LengthSquared() < 1e-12f || outgoing.LengthSquared() < 1e-12f) continue;

                turns.Add(MathF.Acos(Math.Clamp(
                    Vector3.Dot(Vector3.Normalize(incoming), Vector3.Normalize(outgoing)), -1f, 1f))
                    * 180f / MathF.PI);
            }

            const int Skip = 6;
            for (int i = 0; i < n; i++)
                for (int j = i + Skip; j < n; j++)
                {
                    if (n - (j - i) < Skip) continue;
                    clearance = MathF.Min(clearance, Vector3.Distance(loop[i], loop[j]));
                }

            var steps = new float[n];
            for (int i = 0; i < n; i++) steps[i] = Vector3.Distance(loop[i], loop[(i + 1) % n]);
            float median = Median(steps);
            if (median > 1e-6f) spread = MathF.Max(spread, steps.Max() / median);

            if (projector is not null)
                foreach (var point in loop)
                    lift = MathF.Max(lift, Vector3.Distance(point, projector.Project(point)));
        }

        var turn = turns.ToArray();

        return $"loops {line.Loops.Count,2}  n {t.Length,4}  " +
               $"across med {Median(t):+0.00;-0.00} p5 {Percentile(t, 0.05f):+0.00;-0.00} " +
               $"p95 {Percentile(t, 0.95f):+0.00;-0.00}  " +
               $"off-mid {(float)off / t.Length,6:P1}  past-crease {(float)outside / t.Length,6:P1}  " +
               $"nearest {nearest:+0.00;-0.00}  | " +
               $"turn p95 {(turn.Length == 0 ? 0 : Percentile(turn, 0.95f)),3:F0} max {(turn.Length == 0 ? 0 : turn.Max()),3:F0}  " +
               $"step {spread:F1}x  clear {clearance:F2}  lift {lift:F3}";
    }

    private static float Across(Vector3 point, PartingBand band)
    {
        var first = PartingBand.Closest(point, band.First).Point;
        var second = PartingBand.Closest(point, band.Second).Point;

        var axis = second - first;
        float length = axis.LengthSquared();
        return length < 1e-9f ? 0.5f : Vector3.Dot(point - first, axis) / length;
    }

    private static PartingBand Nearest(Vector3 point, IReadOnlyList<PartingBand> bands)
    {
        var best = bands[0];
        float bestDistance = float.MaxValue;

        foreach (var band in bands)
        {
            float d = MathF.Min(
                PartingBandProbe.Distance(point, band.First),
                PartingBandProbe.Distance(point, band.Second));
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = band;
        }

        return best;
    }

    private static float Spacing(PartingLine line)
    {
        var steps = new List<float>();
        foreach (var loop in line.Loops)
            for (int i = 0; i < loop.Count; i++)
                steps.Add(Vector3.Distance(loop[i], loop[(i + 1) % loop.Count]));

        return steps.Count == 0 ? 1f : Median(steps.ToArray());
    }

    // ---------------------------------------------------------------- pictures

    private static void Draw(
        string directory, string model, IMesh body, bool[] band, bool[] suspect,
        IReadOnlyList<RidgeContour> contours, List<(string Name, PartingLine Line)> candidates)
    {
        Directory.CreateDirectory(directory);

        // Hidden runs dropped: with six curves on one body, a ghosted far side is indistinguishable
        // from a near one and the sheet says nothing at all.
        var options = RenderOptions.Default with { DrawOccludedLines = false };
        int faceCount = body.Triangles.Length / 3;

        // Orange where the wall's width is out of tolerance - the stretches the bridged method refuses
        // to solve through. Drawn so the exclusion can be checked against the body rather than taken
        // on trust: a pinch should be a taper anyone can see, not a patch of arithmetic.
        Rgb FaceColour(int face) =>
            suspect.Length == faceCount && suspect[face] ? Suspect
            : band.Length == faceCount && band[face] ? Wall
            : Plain;

        // The two readings of the offset over each other, which is the one comparison the method has to
        // settle for itself: one thickness for the whole rim against the rim's own smoothed profile.
        // Drawn only where both came back, so a body that refused one does not get a sheet implying it
        // did. Separate from the per-method sheets because a millimetre apart over most of their length
        // is exactly the case a single sheet answers and eight separate ones do not.
        var constant = candidates.FirstOrDefault(c => c.Name == "crease-offset constant").Line;
        var following = candidates.FirstOrDefault(c => c.Name == "crease-offset").Line;

        if (constant is not null && following is not null)
        {
            var constantColour = Palette.First(p => p.Name == "crease-offset constant").Colour;
            var followingColour = Palette.First(p => p.Name == "crease-offset").Colour;

            var tiles = new List<Tile>();
            foreach (var view in Views.Standard)
            {
                var camera = Camera.Fit(body, view, Panel, Panel);
                var image = MeshRasterizer.Render(body, camera, Panel, Panel, options, FaceColour);

                foreach (var contour in contours)
                    MeshRasterizer.DrawPolyline(
                        image, camera, contour.Points, contour.IsClosed, Crease, options);

                foreach (var loop in following.Loops)
                    MeshRasterizer.DrawPolyline(image, camera, loop, true, followingColour, options);
                foreach (var loop in constant.Loops)
                    MeshRasterizer.DrawPolyline(image, camera, loop, true, constantColour, options);

                tiles.Add(new Tile(view.Name, image));
            }

            ContactSheet.Save(
                Path.Combine(directory, "compare-offset-constant-vs-profile.png"), tiles, 4, Panel,
                Background, $"{model} — one thickness (yellow) against the smoothed profile (green)",
                new[] { ("rim wall", Wall), ("crease", Crease),
                        ("one thickness", constantColour), ("profile", followingColour) });
        }

        // The two that matter, over each other, for the one comparison that decides anything: what
        // ships today against what is proposed. Everything else gets its own sheet.
        {
            var shipping = candidates.First(c => c.Name == "current").Line;
            var proposed = candidates.First(c => c.Name == "geodesic").Line;
            var shippingColour = Palette.First(p => p.Name == "current").Colour;
            var proposedColour = Palette.First(p => p.Name == "geodesic").Colour;

            var tiles = new List<Tile>();
            foreach (var view in Views.Standard)
            {
                var camera = Camera.Fit(body, view, Panel, Panel);
                var image = MeshRasterizer.Render(body, camera, Panel, Panel, options, FaceColour);

                foreach (var contour in contours)
                    MeshRasterizer.DrawPolyline(
                        image, camera, contour.Points, contour.IsClosed, Crease, options);

                foreach (var loop in shipping.Loops)
                    MeshRasterizer.DrawPolyline(image, camera, loop, true, shippingColour, options);
                foreach (var loop in proposed.Loops)
                    MeshRasterizer.DrawPolyline(image, camera, loop, true, proposedColour, options);

                tiles.Add(new Tile(view.Name, image));
            }

            ContactSheet.Save(
                Path.Combine(directory, "compare-current-vs-geodesic.png"), tiles, 4, Panel,
                Background, $"{model} — shipping (yellow) against geodesic (pink)",
                new[] { ("rim wall", Wall), ("out of tolerance", Suspect), ("crease", Crease),
                        ("current", shippingColour), ("geodesic", proposedColour) });
        }

        // One sheet per method rather than all six over each other, because the question is where each
        // one puts the line and six curves within a millimetre of each other answer it for none.
        foreach (var (name, line) in candidates)
        {
            var colour = Palette.First(p => p.Name == name).Colour;
            var tiles = new List<Tile>();

            foreach (var view in Views.Standard)
            {
                var camera = Camera.Fit(body, view, Panel, Panel);
                var image = MeshRasterizer.Render(body, camera, Panel, Panel, options, FaceColour);

                foreach (var contour in contours)
                    MeshRasterizer.DrawPolyline(
                        image, camera, contour.Points, contour.IsClosed, Crease, options);

                foreach (var loop in line.Loops)
                    MeshRasterizer.DrawPolyline(image, camera, loop, true, colour, options);

                tiles.Add(new Tile(view.Name, image));
            }

            ContactSheet.Save(
                Path.Combine(directory, $"method-{name.Replace(' ', '-')}.png"), tiles, 4, Panel,
                Background, $"{model} — {name}",
                new[] { ("rim wall", Wall), ("out of tolerance", Suspect), ("crease", Crease),
                        (name, colour) });
        }
    }

    /// <summary>
    /// Shell thickness beside a band face. Walked outward because a face on the wall is looking along
    /// the shell rather than across it, so its own probe never exits and asking it directly returns
    /// nothing almost every time.
    /// </summary>
    private static float LocalThickness(IMesh body, WallThickness thickness, int face, int steps = 4)
    {
        var triangles = body.Triangles;
        int faceCount = triangles.Length / 3;
        if (thickness.PerFace.Count != faceCount) return float.PositiveInfinity;

        var seen = new HashSet<int> { face };
        var frontier = new List<int> { face };
        var vertexFaces = new Dictionary<int, List<int>>();

        for (int f = 0; f < faceCount; f++)
            for (int e = 0; e < 3; e++)
            {
                int v = triangles[(f * 3) + e];
                if (!vertexFaces.TryGetValue(v, out var list)) vertexFaces[v] = list = new List<int>(6);
                list.Add(f);
            }

        for (int step = 0; step <= steps; step++)
        {
            float best = float.PositiveInfinity;
            foreach (int f in frontier)
            {
                float t = thickness.PerFace[f];
                if (float.IsPositiveInfinity(t) || thickness.PartnerFace[f] < 0) continue;
                best = MathF.Min(best, t);
            }
            if (!float.IsPositiveInfinity(best)) return best;

            var next = new List<int>();
            foreach (int f in frontier)
                for (int e = 0; e < 3; e++)
                    foreach (int n in vertexFaces[triangles[(f * 3) + e]])
                        if (seen.Add(n)) next.Add(n);

            if (next.Count == 0) break;
            frontier = next;
        }

        return float.PositiveInfinity;
    }

    private void Ruler(string label, float[] values)
    {
        Array.Sort(values);
        float median = values[values.Length / 2];
        if (median < 1e-6f) return;

        _log.WriteLine(
            $"{label}  {median,6:F2} mm   p95 {values[(int)(values.Length * 0.95f)] / median,4:F2}x   " +
            $"max {values[^1] / median,5:F2}x   " +
            $"over 1.25x {values.Count(v => v > median * 1.25f) * 100f / values.Length,5:F1}%");
    }

    private static float Median(float[] values)
    {
        if (values.Length == 0) return 0f;
        var sorted = (float[])values.Clone();
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }

    private static float Percentile(float[] values, float fraction)
    {
        if (values.Length == 0) return 0f;
        var sorted = (float[])values.Clone();
        Array.Sort(sorted);
        return sorted[Math.Clamp((int)MathF.Round(fraction * (sorted.Length - 1)), 0, sorted.Length - 1)];
    }
}
