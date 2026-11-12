using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Diagnostics;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Core.Tests.Diagnostics;

/// <summary>
/// One scorecard for the whole parting split: every saved mould in tests/files/3mf, against each sweep
/// the view offers, under the recipe <c>PartingSplitViewModel</c> fixes for new work rather than the
/// record defaults - the two differ on exactly the settings that decide the flange.
///
/// <para>
/// It exists because the measurements were scattered. Slope lived in <see cref="FlangeWaviness"/>,
/// crossings in <c>FlangeSelfIntersection</c>, pieces in <c>PartingPieceCount</c>, centring in
/// <c>PartingLineCentringSweep</c>, each on its own subset of bodies, so a change judged on one could
/// regress another unseen. This prints all of them for all eight bodies at once, and adds the ones
/// nothing measured: whether the flange is a single-valued surface over the parting plane, whether the
/// finished halves actually pull apart along the axis, whether volume is conserved and the cavity left
/// alone, how creased the mating face is, and whether the line comes out the same for the same body.
/// </para>
///
/// <para>
/// Nothing here asserts. A gate that fails is printed as failing, and sorted into refusals and
/// warnings by <see cref="PartingScorecardReport.Judge"/>; the assertions belong in the regular suite,
/// against the production check, once that exists.
/// </para>
///
/// <para>
/// Set <c>FABOLUS_SCORECARD_DIR</c> to also write, per body, one JSON file and contact sheets of the
/// line and the flange, and <c>scorecard.md</c> with the tables for the whole run. Keep a run's
/// folder: <see cref="CompareWithBaseline"/> lays a later one against it.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class PartingScorecard
{
    /// <summary>The print limit the goal is stated against: no mating face steeper than this.</summary>
    private const double SteepDeg = 45.0;

    /// <summary>
    /// How far past vertical a face has to lean before it counts as facing against the axis. A face
    /// exactly edge-on to the axis has no sign to speak of, and rounding would hand it one at random.
    /// </summary>
    private const double AgainstCos = 0.02;

    /// <summary>Cell of the grid the flange is sampled on, looking along the axis, to find where a line
    /// parallel to the axis meets it twice.</summary>
    private const double GridMm = 0.5;

    /// <summary>How far apart along the axis two sheets over the same cell must be to count as two
    /// crossings. Above the cutter's own 0.1 mm, so neighbouring faces meeting at an edge never do.</summary>
    private const double MultiValuedMm = 0.3;

    /// <summary>What "away from the cut" means for the cavity check, measured from the cutter.</summary>
    private const double AwayFromCutMm = 1.0;

    /// <summary>What counts as on the surface for that check; an order above the boolean's own noise.</summary>
    private const double UnchangedMm = 0.01;

    private static readonly double[] PullOffsetsMm = [0.5, 2.0, 5.0, 15.0, 40.0];
    private static readonly double[] TiltsDeg = [30.0, 60.0, 90.0];

    private static readonly PartingMeshSweep[] Sweeps =
        [PartingMeshSweep.MouldLoft, PartingMeshSweep.SurfaceSweep, PartingMeshSweep.SkinBisector];

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private readonly IGeometryEngine _engine;
    private readonly PartingMeshFeature _sut;
    private readonly ITestOutputHelper _out;

    public PartingScorecard(GeometryEngineFixture fixture, ITestOutputHelper output)
    {
        _engine = fixture.Engine;
        _sut = new PartingMeshFeature(_engine);
        _out = output;
    }

    // The recipe PartingSplitViewModel fixes for new work. Mirrored rather than referenced because the
    // view model lives in the WPF assembly, which this project does not load.
    private static PartingLineParameters LineRecipe => new()
    {
        Source = PartingLineSource.ExtrusionBorder,
        PullDirection = Vector3.UnitY,
    };

    private static PartingMeshParameters MeshRecipe(PartingMeshSweep sweep) => PartingMeshParameters.Default with
    {
        AxisSource = PartingMeshAxisSource.PartingLine,
        Sweep = sweep,
        SplitMethod = PartingSplitMethod.SeveredComponents,
        Thickening = PartingMeshThickening.Extrude,
        Depth = PartingMeshParameters.DefaultDepth,
        FlangeMaxSlopeDeg = 80.0,
        NormalFollowMm = 100.0,
        RawFlange = false,
        NormalSmoothingPasses = 5,
        Axis = Vector3.UnitY,
    };

    [DiagnosticTheory]
    [InlineData("chin.3mf")]
    [InlineData("ear.3mf")]
    [InlineData("eye.3mf")]
    [InlineData("larynx_large.3mf")]
    [InlineData("larynx_small.3mf")]
    [InlineData("nose.3mf")]
    [InlineData("scalp.3mf")]
    [InlineData("test bolus standard.3mf")]
    public void Score(string file)
    {
        string id = Path.GetFileNameWithoutExtension(file).Replace(' ', '_');
        string? directory = Environment.GetEnvironmentVariable("FABOLUS_SCORECARD_DIR");
        var card = new Dictionary<string, object?> { ["body"] = id };

        try
        {
            Run(file, id, directory, card);
        }
        catch (Exception e)
        {
            card["threw"] = $"{e.GetType().Name}: {e.Message}";
            _out.WriteLine(e.ToString());
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, id + ".json"), JsonSerializer.Serialize(card, Json));

                // Rebuilt after every body rather than once at the end: the rows of a theory finish
                // in no promised order, so whichever is last has to be the one that writes them all.
                PartingScorecardReport.WriteSummary(directory);
            }
        }
    }

    /// <summary>
    /// Lays the run in <c>FABOLUS_SCORECARD_DIR</c> against an earlier one in
    /// <c>FABOLUS_SCORECARD_BASELINE</c>, for every body and sweep at once. This is the table a change
    /// is accepted or rejected on: an improvement on the body being fixed counts for nothing if another
    /// got worse, and the only way to know is to look at all of them.
    /// </summary>
    [DiagnosticFact]
    public void CompareWithBaseline()
    {
        string? after = Environment.GetEnvironmentVariable("FABOLUS_SCORECARD_DIR");
        string? before = Environment.GetEnvironmentVariable("FABOLUS_SCORECARD_BASELINE");
        if (string.IsNullOrWhiteSpace(after) || string.IsNullOrWhiteSpace(before))
        {
            _out.WriteLine("Set FABOLUS_SCORECARD_DIR and FABOLUS_SCORECARD_BASELINE to two scorecard folders.");
            return;
        }

        string table = PartingScorecardReport.Compare(before, after);
        File.WriteAllText(Path.Combine(after, "scorecard-delta.md"), table);
        _out.WriteLine(table);
    }

    /// <summary>
    /// Which stage of the trace first comes out different when the body is turned rigidly, and where on
    /// the line the difference lands. The scorecard only says that it does; this says whether the wall
    /// measurement, the ridge, the crease offset or the treatment is where it starts.
    ///
    /// <para>
    /// The millionth of a degree is the control. A turn that small moves no vertex by more than a few
    /// nanometres, so a line that changes under it is not responding to orientation at all - it is
    /// resting on a tie that any rounding breaks.
    /// </para>
    /// </summary>
    [DiagnosticTheory]
    [InlineData("eye.3mf", 30.0)]
    [InlineData("eye.3mf", 0.000001)]
    [InlineData("larynx_small.3mf", 30.0)]
    [InlineData("larynx_small.3mf", 0.000001)]
    [InlineData("chin.3mf", 30.0)]
    [InlineData("ear.3mf", 30.0)]
    [InlineData("larynx_large.3mf", 30.0)]
    [InlineData("nose.3mf", 30.0)]
    public void WhereTurningTheBodyChangesTheLine(string file, double tilt)
    {
        var (mould, _) = Open(Asset(file));
        var body = _sut.GetBodyMesh(mould).Value;
        var turned = BodyMesh.Create(_engine.CreateMesh(
            [.. body.Mesh.Vertices.Select(p => RotateAboutX(p, tilt))], body.Mesh.Triangles, body.Mesh.Metadata).Value).Value;

        IReadOnlyList<Vector3> Back(IReadOnlyList<Vector3> points) => points.Select(p => RotateAboutX(p, -tilt)).ToList();

        _out.WriteLine($"=== {file}, turned {tilt} deg about X");

        double wallA = WallThickness.Measure(_engine, body.Mesh, WallThicknessOptions.Default).Value.Median;
        double wallB = WallThickness.Measure(_engine, turned.Mesh, WallThicknessOptions.Default).Value.Median;
        _out.WriteLine($"  wall median {wallA:F6} vs {wallB:F6} mm");

        var closedA = RidgeDetection.FindRidgeContours(body.Mesh, RidgeDetectionOptions.Default).Where(c => c.IsClosed).ToList();
        var closedB = RidgeDetection.FindRidgeContours(turned.Mesh, RidgeDetectionOptions.Default).Where(c => c.IsClosed).ToList();
        _out.WriteLine($"  closed contours {closedA.Count} vs {closedB.Count}");
        for (int i = 0; i < closedA.Count; i++)
        {
            var others = closedB.Select(c => Back(c.Points)).ToList();
            var d = Distances([closedA[i].Points], others).OrderBy(x => x).ToArray();
            _out.WriteLine($"    contour {i}: {closedA[i].Points.Count} pts, to the turned ridge median {d[d.Length / 2]:F4} max {d[^1]:F4} mm");
        }

        var rimsA = PartingStrategy.Rims(closedA, wallA);
        var rimsB = PartingStrategy.Rims(closedB, wallB);
        _out.WriteLine($"  rims [{string.Join(", ", rimsA.Select(r => r.Kind))}] vs [{string.Join(", ", rimsB.Select(r => r.Kind))}]");

        using var projectorA = new PartingTools(_engine).CreateSurfaceProjector(body.Mesh).Value;
        using var projectorB = new PartingTools(_engine).CreateSurfaceProjector(turned.Mesh).Value;
        var wallsA = rimsA.Where(r => r.Kind == PartingRimKind.Wall).ToList();
        var wallsB = rimsB.Where(r => r.Kind == PartingRimKind.Wall).ToList();
        for (int i = 0; i < Math.Min(wallsA.Count, wallsB.Count); i++)
        {
            var bandA = new PartingBand(closedA[wallsA[i].ContourIndices[0]], closedA[wallsA[i].ContourIndices[1]]);
            var bandB = new PartingBand(closedB[wallsB[i].ContourIndices[0]], closedB[wallsB[i].ContourIndices[1]]);

            foreach (int passes in new[] { 0, CreaseOffsetOptions.Default.SmoothingPasses })
            {
                var options = CreaseOffsetOptions.Default with { SmoothingPasses = passes };
                var offsetA = CreaseOffsetLine.Trace(body.Mesh, bandA, out var reportA, projectorA, options);
                var offsetB = CreaseOffsetLine.Trace(turned.Mesh, bandB, out var reportB, projectorB, options);
                if (offsetA is null || offsetB is null)
                {
                    _out.WriteLine($"    rim {i} crease offset ({passes} passes): {(offsetA is null ? "none" : "line")} vs {(offsetB is null ? "none" : "line")}");
                    continue;
                }

                var d = Distances([offsetA], [Back(offsetB)]).OrderBy(x => x).ToArray();
                _out.WriteLine(
                    $"    rim {i} crease offset ({passes} passes): {offsetA.Count} vs {offsetB.Count} pts, median {d[d.Length / 2]:F4} max {d[^1]:F4} mm; " +
                    $"base {reportA?.Base} vs {reportB?.Base}, dropped {reportA?.Dropped} vs {reportB?.Dropped}, " +
                    $"back on crease {reportA?.BackOnCrease} vs {reportB?.BackOnCrease}, short {reportA?.Short} vs {reportB?.Short}");
            }
        }

        var lineA = _sut.GeneratePartingLineFromBody(body, LineRecipe).Value;
        var lineB = _sut.GeneratePartingLineFromBody(turned, LineRecipe).Value;
        for (int l = 0; l < Math.Min(lineA.Loops.Count, lineB.Loops.Count); l++)
        {
            var loop = lineA.Loops[l];
            var other = lineB.Loops.Select(Back).ToList();
            var d = Distances([loop], other);
            int worst = d.IndexOf(d.Max());
            int moved = d.Count(x => x > 0.25);
            _out.WriteLine(
                $"  final line loop {l}: {loop.Count} vs {lineB.Loops[l].Count} pts, max {d[worst]:F3} mm at point {worst} " +
                $"({loop[worst].X:F1}, {loop[worst].Y:F1}, {loop[worst].Z:F1}); {moved} of {loop.Count} points moved more than 0.25 mm");
        }
    }

    private void Run(string file, string id, string? directory, Dictionary<string, object?> card)
    {
        string path = Asset(file);
        var (mould, record) = Open(path);
        var body = _sut.GetBodyMesh(mould).Value;
        var bodyIndex = _engine.Spatial.IndexFor(body.Mesh).Value;
        var mouldIndex = _engine.Spatial.IndexFor(mould.Mesh).Value;

        // ------------------------------------------------------------ what the body is said to need
        double wall = WallThickness.Measure(_engine, body.Mesh, WallThicknessOptions.Default).Value.Median;
        var closed = RidgeDetection.FindRidgeContours(body.Mesh, RidgeDetectionOptions.Default)
            .Where(c => c.IsClosed).ToList();
        var rims = PartingStrategy.Rims(closed, wall);
        var bands = rims.Where(r => r.Kind == PartingRimKind.Wall)
            .Select(r => new PartingBand(closed[r.ContourIndices[0]], closed[r.ContourIndices[1]]))
            .ToList();
        var strategy = PartingStrategy.Evaluate(body.Mesh, wallThickness: wall);

        card["strategy"] = new Dictionary<string, object?>
        {
            ["shape"] = strategy.Shape.ToString(),
            ["genus"] = strategy.Genus,
            ["cutsNeeded"] = strategy.CutsNeeded,
            ["rims"] = rims.Select(r => r.Kind.ToString()).ToArray(),
            ["surfacePiecesWhenWalled"] = strategy.Combined.SubstantialPieces,
            ["recommended"] = strategy.Recommended?.ToString(),
            ["wallMm"] = R(wall),
        };
        _out.WriteLine(
            $"=== {id}: {strategy.Shape}, genus {strategy.Genus}, cuts needed {strategy.CutsNeeded}, " +
            $"rims [{string.Join(", ", rims.Select(r => r.Kind))}], wall {wall:F2} mm");

        // ------------------------------------------------------------ the line
        var clock = Stopwatch.StartNew();
        var traced = _sut.GeneratePartingLineFromBody(body, LineRecipe);
        long traceMs = clock.ElapsedMilliseconds;
        if (traced.IsFailure)
        {
            card["line"] = new Dictionary<string, object?> { ["error"] = traced.Error.Description };
            _out.WriteLine($"  line: FAILED - {traced.Error.Description}");
            return;
        }

        var line = traced.Value;
        var axisResolved = PartingMeshFeature.ResolveAxis(line, MeshRecipe(PartingMeshSweep.MouldLoft));
        if (axisResolved.IsFailure)
        {
            card["line"] = new Dictionary<string, object?> { ["error"] = axisResolved.Error.Description };
            _out.WriteLine($"  axis: FAILED - {axisResolved.Error.Description}");
            return;
        }

        var axis = axisResolved.Value.Axis.Normalize();
        var (u, v) = Frame(axis);

        var loops = new List<Dictionary<string, object?>>();
        var turnings = new List<double[]>();
        var biases = new List<double[]?>();
        for (int i = 0; i < line.Loops.Count; i++)
        {
            var (metrics, turning, bias) = LineMetrics(line.Loops[i], axis, u, v, bands, bodyIndex, wall);
            loops.Add(metrics);
            turnings.Add(turning);
            biases.Add(bias);
            _out.WriteLine($"  line loop {i}: " + string.Join(", ", metrics.Select(kv => $"{kv.Key}={kv.Value}")));
        }

        var consistency = Consistency(path, mould, record, body, line);
        _out.WriteLine("  line consistency, max mm: " + string.Join(", ", consistency.Select(kv =>
            $"{kv.Key}={(kv.Value is Dictionary<string, object?> retrace ? Get(retrace, retrace.ContainsKey("error") ? "error" : "maxMm") : kv.Value)}")));

        card["line"] = new Dictionary<string, object?>
        {
            ["traceMs"] = traceMs,
            ["loops"] = line.Loops.Count,
            ["wallRims"] = bands.Count,
            ["rims"] = rims.Count,
            ["axis"] = new[] { R(axis.X), R(axis.Y), R(axis.Z) },
            ["perLoop"] = loops,
            ["consistency"] = consistency,
        };

        if (!string.IsNullOrWhiteSpace(directory))
            DrawLine(Path.Combine(directory, id + "-line.png"), id, body.Mesh, line, bands, axis, turnings, biases);

        // ------------------------------------------------------------ each sweep
        var sweeps = new Dictionary<string, object?>();
        var tiles = new List<Tile>();
        string? onlySweep = Environment.GetEnvironmentVariable("FABOLUS_SCORECARD_SWEEP");

        foreach (var sweep in Sweeps)
        {
            if (!string.IsNullOrWhiteSpace(onlySweep) && !sweep.ToString().Equals(onlySweep, StringComparison.OrdinalIgnoreCase))
                continue;

            var result = new Dictionary<string, object?>();
            sweeps[sweep.ToString()] = result;
            try
            {
                ScoreSweep(sweep, mould, body, line, bodyIndex, mouldIndex, result, tiles, directory, id);
            }
            catch (Exception e)
            {
                result["threw"] = $"{e.GetType().Name}: {e.Message}";
                _out.WriteLine($"  {sweep}: threw {e}");
            }
        }

        card["sweeps"] = sweeps;

        if (!string.IsNullOrWhiteSpace(directory) && tiles.Count > 0)
        {
            ContactSheet.Save(Path.Combine(directory, id + "-flange.png"), tiles, 6, TileSize, Options.Background,
                $"{id}: flange per sweep, app recipe (only the part inside the mould is coloured)",
                [("flat", Flat), ("45 deg", Warm), ("past 45 deg", Steep), ("against the axis / doubled over / fouling", Fold),
                 ("inside the body (seal strip)", SealStrip), ("parting line", LineColour)]);
        }
    }

    // ==================================================================== the line

    /// <summary>
    /// Everything asked of one loop of the line: how smooth it is, how much it climbs, where it sits in
    /// its rim wall, and whether it doubles back seen along the axis.
    /// </summary>
    private static (Dictionary<string, object?> Metrics, double[] Turning, double[]? Bias) LineMetrics(
        IReadOnlyList<Vector3> loop, Vector3 axis, Vector3 u, Vector3 v,
        IReadOnlyList<PartingBand> bands, global::GeometryEngine.Core.Geometry.ISpatialIndex bodyIndex, double wall)
    {
        int n = loop.Count;
        var metrics = new Dictionary<string, object?>();

        var spacing = new double[n];
        for (int i = 0; i < n; i++) spacing[i] = loop[i].DistanceTo(loop[(i + 1) % n]);
        metrics["points"] = n;
        metrics["perimeterMm"] = R(spacing.Sum());
        metrics["spacingMedianMm"] = R(Median(spacing));

        // Turning at each point: the angle between the step in and the step out. Zero is straight on.
        var turning = new double[n];
        double steepest = 0.0;
        for (int i = 0; i < n; i++)
        {
            var a = loop[i] - loop[(i - 1 + n) % n];
            var b = loop[(i + 1) % n] - loop[i];
            turning[i] = a.Length < 1e-9 || b.Length < 1e-9 ? 0.0 : Angle(a, b);
            if (b.Length > 1e-9)
                steepest = Math.Max(steepest, Math.Asin(Math.Min(1.0, Math.Abs(b.Normalize().Dot(axis)))) * 180.0 / Math.PI);
        }

        metrics["turnMaxDeg"] = R(turning.Max());
        metrics["turnRmsDeg"] = R(Math.Sqrt(turning.Sum(t => t * t) / n));

        // The same turn with the body's own bending taken out. The line is held on the surface, so on
        // a coarsely faceted body it turns at every face edge it crosses however straight it runs
        // across the wall - that is the mesh, not the line. Measured in the tangent plane at each
        // point, what is left is the line steering left and right within the surface.
        var within = new double[n];
        for (int i = 0; i < n; i++)
        {
            var hit = bodyIndex.ClosestPoint(loop[i]);
            if (!hit.HasValue || hit.Value.Normal.Length < 1e-9) continue;

            var normal = hit.Value.Normal.Normalize();
            var a = Flatten(loop[i] - loop[(i - 1 + n) % n], normal);
            var b = Flatten(loop[(i + 1) % n] - loop[i], normal);
            within[i] = a.Length < 1e-9 || b.Length < 1e-9 ? 0.0 : Angle(a, b);
        }

        metrics["steerMaxDeg"] = R(within.Max());
        metrics["steerRmsDeg"] = R(Math.Sqrt(within.Sum(t => t * t) / n));

        var heights = loop.Select(p => p.Dot(axis)).ToArray();
        metrics["heightRangeMm"] = R(heights.Max() - heights.Min());
        metrics["steepestClimbDeg"] = R(steepest);

        metrics["offSurfaceMaxMm"] = R(loop.Max(p =>
        {
            var hit = bodyIndex.ClosestPoint(p);
            return hit.HasValue ? hit.Value.Distance : double.NaN;
        }));

        // Where it sits across its wall: 0.5 is the middle, 0 and 1 the two creases.
        double[]? bias = null;
        if (bands.Count > 0)
        {
            var band = bands.OrderBy(b => loop.Where((_, i) => i % Math.Max(n / 16, 1) == 0)
                .Average(p => Math.Min(PartingBand.Closest(p, b.First).Distance, PartingBand.Closest(p, b.Second).Distance)))
                .First();

            bias = new double[n];
            var toNearer = new double[n];
            for (int i = 0; i < n; i++)
            {
                double dA = PartingBand.Closest(loop[i], band.First).Distance;
                double dB = PartingBand.Closest(loop[i], band.Second).Distance;
                bias[i] = dA + dB < 1e-9 ? 0.5 : dA / (dA + dB);
                toNearer[i] = Math.Min(dA, dB);
            }

            var off = bias.Select(b => Math.Abs(b - 0.5)).ToArray();
            metrics["offCentreMedian"] = R(Median(off));
            metrics["offCentreMax"] = R(off.Max());
            metrics["pctBeyondQuarter"] = R(100.0 * off.Count(o => o > 0.25) / n);
            metrics["creaseToLineOverWall"] = R(toNearer.Average() / wall);
            metrics["bandSpanMm"] = R(band.Span);
        }

        // Doubling back, seen along the axis: bearings about the centroid that go backwards, and the
        // footprint crossing itself.
        metrics["turnsAboutAxis"] = R(Turning(loop, u, v) / Math.Tau);
        metrics["backwardSteps"] = BackwardSteps(loop, u, v);
        metrics["footprintCrossings"] = FootprintCrossings(loop, u, v);

        return (metrics, turning, bias);
    }

    /// <summary>
    /// The same body's line traced again five ways - straight after, from a second opening of the file,
    /// from a copy saved and reopened, and with the body turned rigidly - each as how far it lands from
    /// the first once turned back.
    /// </summary>
    private Dictionary<string, object?> Consistency(
        string path, MouldMesh mould, MeshRecord record, BodyMesh body, PartingLine line)
    {
        var result = new Dictionary<string, object?>();

        result["runTwice"] = Describe(_sut.GeneratePartingLineFromBody(body, LineRecipe), line, p => p);

        var (reopened, _) = Open(path);
        result["openedAgain"] = Describe(
            _sut.GeneratePartingLineFromBody(_sut.GetBodyMesh(reopened).Value, LineRecipe), line, p => p);

        var temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
        try
        {
            string saved = Path.Combine(temp.FullName, "scorecard.3mf");
            var exported = new ExportMesh(_engine).Execute(mould.Mesh, record, saved);
            if (exported.IsFailure)
            {
                result["savedAndReopened"] = "export failed: " + exported.Error.Description;
            }
            else
            {
                var (again, _) = Open(saved);
                result["savedAndReopened"] = Describe(
                    _sut.GeneratePartingLineFromBody(_sut.GetBodyMesh(again).Value, LineRecipe), line, p => p);
            }
        }
        finally
        {
            temp.Delete(recursive: true);
        }

        foreach (double tilt in TiltsDeg)
        {
            var turned = _engine.CreateMesh(
                [.. body.Mesh.Vertices.Select(p => RotateAboutX(p, tilt))], body.Mesh.Triangles, body.Mesh.Metadata).Value;
            result[$"rotated{tilt:F0}"] = Describe(
                _sut.GeneratePartingLineFromBody(BodyMesh.Create(turned).Value, LineRecipe), line,
                p => RotateAboutX(p, -tilt));
        }

        return result;
    }

    private static Dictionary<string, object?> Describe(
        BasicResults.Result<PartingLine> traced, PartingLine reference, Func<Vector3, Vector3> back)
    {
        if (traced.IsFailure) return new Dictionary<string, object?> { ["error"] = traced.Error.Description };

        var loops = traced.Value.Loops.Select(l => (IReadOnlyList<Vector3>)l.Select(back).ToList()).ToList();
        var forward = Distances(loops, reference.Loops);
        var reverse = Distances(reference.Loops, loops);
        var all = forward.Concat(reverse).OrderBy(d => d).ToArray();

        return new Dictionary<string, object?>
        {
            ["medianMm"] = R(all[all.Length / 2]),
            ["maxMm"] = R(all[^1]),
            ["sameLoops"] = loops.Count == reference.Loops.Count,
        };
    }

    /// <summary>From every point of one line to the nearest point of the other, measured to its
    /// segments rather than its samples so two samplings of one curve read as zero.</summary>
    private static List<double> Distances(
        IReadOnlyList<IReadOnlyList<Vector3>> from, IReadOnlyList<IReadOnlyList<Vector3>> to)
    {
        var result = new List<double>();
        foreach (var loop in from)
            foreach (var p in loop)
            {
                double best = double.MaxValue;
                foreach (var other in to)
                    for (int i = 0; i < other.Count; i++)
                        best = Math.Min(best, SegmentDistance(p, other[i], other[(i + 1) % other.Count]));
                result.Add(best);
            }
        return result;
    }

    // ==================================================================== one sweep

    private void ScoreSweep(
        PartingMeshSweep sweep, MouldMesh mould, BodyMesh body, PartingLine line,
        global::GeometryEngine.Core.Geometry.ISpatialIndex bodyIndex,
        global::GeometryEngine.Core.Geometry.ISpatialIndex mouldIndex,
        Dictionary<string, object?> result, List<Tile> tiles, string? directory, string id)
    {
        var resolved = PartingMeshFeature.ResolveAxis(line, MeshRecipe(sweep)).Value;
        var axis = resolved.Axis.Normalize();
        var (u, v) = Frame(axis);

        var clock = Stopwatch.StartNew();
        var contour = _sut.GenerateOuterContour(mould, resolved);
        BasicResults.Result<IMesh> built = contour.IsFailure
            ? contour.Error
            : _sut.GenerateFlangeSurface(line, contour.Value, resolved, body);
        result["surfaceMs"] = clock.ElapsedMilliseconds;

        if (built.IsFailure)
        {
            result["outcome"] = "flange failed: " + built.Error.Description;
            _out.WriteLine($"  {sweep}: flange FAILED - {built.Error.Description}");
            return;
        }

        var surface = built.Value;
        var flange = new Flange(surface, axis, bodyIndex, mouldIndex);
        var linePoints = line.Loops.SelectMany(l => l).ToArray();
        var lineTangents = line.Loops.SelectMany(l => Enumerable.Range(0, l.Count)
            .Select(i => l[(i + 1) % l.Count] - l[(i - 1 + l.Count) % l.Count])).ToArray();

        // ---------------------------------------------------------------- flatness
        var whole = Enumerable.Range(0, flange.Faces).Where(f => flange.Area[f] > 0).ToList();
        var mating = whole.Where(f => flange.Mating[f]).ToList();
        result["slopeWhole"] = Slopes(flange, whole);
        result["slopeMating"] = Slopes(flange, mating);

        // ---------------------------------------------------------------- single-valued
        int surfaceCrossings = _engine.Evaluators.CountSelfIntersections(surface).Value;
        double againstMating = mating.Where(f => flange.Facing[f] < -AgainstCos).Sum(f => flange.Area[f]);
        double matingArea = mating.Sum(f => flange.Area[f]);
        var doubledMating = Doubled(flange, mating, u, v, out var doubledFaces);
        var inMould = whole.Where(f => flange.InMould[f]).ToList();
        var doubledInMould = Doubled(flange, inMould, u, v, out _);

        result["singleValued"] = new Dictionary<string, object?>
        {
            ["surfaceSelfIntersections"] = surfaceCrossings,
            ["matingAreaMm2"] = R(matingArea),
            ["againstAxisMm2"] = R(againstMating),
            ["againstAxisPct"] = R(100.0 * againstMating / Math.Max(matingArea, 1e-9)),
            ["doubledOverMm2"] = R(doubledMating.Doubled * GridMm * GridMm),
            ["doubledOverPct"] = R(100.0 * doubledMating.Doubled / Math.Max(doubledMating.Covered, 1)),
            ["doubledOverInMouldMm2"] = R(doubledInMould.Doubled * GridMm * GridMm),
            ["passes"] = againstMating < 1.0 && doubledMating.Doubled * GridMm * GridMm < 1.0,
        };

        // ---------------------------------------------------------------- smoothness
        result["dihedral"] = Dihedrals(flange, mating, linePoints, lineTangents, axis);

        // ---------------------------------------------------------------- steep regions and why
        result["steep"] = SteepRegions(flange, inMould, linePoints, lineTangents, axis);

        // ---------------------------------------------------------------- seal
        var seal = _sut.InspectFlangeSeal(surface, body, line, resolved);
        result["seal"] = seal.IsFailure
            ? "unknown: " + seal.Error.Description
            : (object)new Dictionary<string, object?>
            {
                ["rimPoints"] = seal.Value.Count,
                ["breached"] = seal.Value.Count(p => !p.IsSealed),
                ["worstOutsideMm"] = R(seal.Value.Count == 0 ? 0.0 : Math.Max(0.0, seal.Value.Max(p => p.SignedDistance))),
                // The inspection finds the rim by the line's footprint, and the bisector has no rim of
                // that kind - see PartingSplitViewModel, which does not show it for that sweep either.
                ["applies"] = sweep != PartingMeshSweep.SkinBisector,
            };

        // ---------------------------------------------------------------- cutter
        clock.Restart();
        var cutterResult = _sut.ExtrudeFlange(surface, resolved);
        if (cutterResult.IsFailure)
        {
            result["outcome"] = "cutter failed: " + cutterResult.Error.Description;
            return;
        }

        var cutter = cutterResult.Value;
        var cutterTopology = _engine.Evaluators.ValidateTopology(cutter).Value;
        int cutterCrossings = _engine.Evaluators.CountSelfIntersections(cutter).Value;
        result["cutter"] = new Dictionary<string, object?>
        {
            ["watertight"] = cutterTopology.IsWatertight,
            ["manifold"] = cutterTopology.IsManifold,
            ["selfIntersections"] = cutterCrossings,
            ["triangles"] = cutter.TriangleCount,
        };

        // ---------------------------------------------------------------- the cut, as the app commits it
        var first = _engine.Booleans.Subtract(mould.Mesh, cutter);
        if (first.IsFailure)
        {
            result["outcome"] = "subtract failed: " + first.Error.Description;
            return;
        }

        IReadOnlyList<IMesh> components = _engine.Evaluators.SeparateComponents(first.Value).Value;
        int specksFirst = components.Count(PartingPieces.IsSpeck);
        int firstComponents = components.Count;

        double mouldVolume = PartingPieces.Volume(mould.Mesh);
        var kerf = _engine.Booleans.Intersect(mould.Mesh, cutter);
        double kerfVolume = kerf.IsSuccess ? PartingPieces.Volume(kerf.Value) : double.NaN;
        double firstSum = components.Sum(PartingPieces.Volume);

        // CutMould's own correction, run only where it would run: specks beside two real pieces.
        int specksFinal = specksFirst;
        if (specksFirst > 0 && PartingPieces.Substantial(components).Count >= 2)
        {
            var cleared = _sut.CutClear(mould, surface, body, resolved);
            if (cleared.IsSuccess)
            {
                components = cleared.Value.Components;
                specksFinal = components.Count(PartingPieces.IsSpeck);
            }
        }

        var pieces = PartingPieces.Substantial(components);
        result["cutMs"] = clock.ElapsedMilliseconds;

        var pieceTopology = pieces.Select(p => _engine.Evaluators.ValidateTopology(p).Value).ToList();
        result["pieces"] = new Dictionary<string, object?>
        {
            ["componentsFirstCut"] = firstComponents,
            ["specksFirstCut"] = specksFirst,
            ["specksAfterClearing"] = specksFinal,
            ["substantial"] = pieces.Count,
            ["sharesPct"] = pieces.Select(p => R(100.0 * PartingPieces.Volume(p) / mouldVolume)).ToArray(),
            ["watertight"] = pieceTopology.Select(t => t.IsWatertight).ToArray(),
            ["manifold"] = pieceTopology.Select(t => t.IsManifold).ToArray(),
            ["selfIntersections"] = pieces.Select(p => _engine.Evaluators.CountSelfIntersections(p).Value).ToArray(),
            // What each of the app's two routes does with this cut. Apply goes through CutMould, which
            // refuses fewer than two pieces; Next goes through SplitMould, which does not look.
            ["applyPath"] = pieces.Count >= 2 ? "returns" : "fails: SplitProducedSinglePiece",
            ["previewPath"] = firstComponents == 1
                ? "returns the uncut mould as both halves"
                : specksFirst > 0 ? "returns, specks kept in the second half" : "returns",
        };

        result["volume"] = new Dictionary<string, object?>
        {
            ["mouldMm3"] = R(mouldVolume),
            ["kerfMm3"] = R(kerfVolume),
            ["piecesMm3"] = R(firstSum),
            ["errorMm3"] = R(mouldVolume - kerfVolume - firstSum),
            ["errorPpm"] = R(1e6 * (mouldVolume - kerfVolume - firstSum) / mouldVolume),
        };

        // ---------------------------------------------------------------- cavity left alone
        result["unchanged"] = Unchanged(mould.Mesh, cutter, components, bodyIndex, mouldIndex);

        // ---------------------------------------------------------------- pull apart
        IMesh? rest = null, overlap = null;
        double pulledAlong = 1.0;
        result["pullApart"] = pieces.Count >= 2
            ? PullApart(pieces, axis, out rest, out overlap, out pulledAlong)
            : new Dictionary<string, object?> { ["passes"] = false, ["reason"] = "fewer than two pieces" };

        // ---------------------------------------------------------------- any other way out
        // The axis is fitted to the line, and nothing says it is the best direction to part along. If
        // every mating face can be seen from some other direction, the flange is sound and the axis is
        // what wants changing; if none can, the surface itself hooks.
        result["bestPull"] = BestPull(flange, mating, axis);

        // ---------------------------------------------------------------- what Apply really returns
        // Everything above follows the stages the view shows. This is the one call Apply Split makes,
        // from the recipe alone, so what it says is what a user is handed.
        var applied = _sut.CutMould(mould, LineRecipe, MeshRecipe(sweep));
        result["applied"] = applied.IsFailure
            ? new Dictionary<string, object?> { ["succeeded"] = false, ["error"] = applied.Error.Description }
            : new Dictionary<string, object?>
            {
                ["succeeded"] = true,
                ["componentSharesPct"] = _engine.Evaluators.SeparateComponents(applied.Value).Value
                    .Select(c => R(100.0 * PartingPieces.Volume(c) / mouldVolume))
                    .OrderByDescending(share => share).ToArray(),
            };

        var sv = (Dictionary<string, object?>)result["singleValued"]!;
        var pa = (Dictionary<string, object?>)result["pullApart"]!;
        var sm = (Dictionary<string, object?>)result["slopeMating"]!;
        _out.WriteLine(
            $"  {sweep,-13} slope mating med {Get(sm, "medianDeg")} p90 {Get(sm, "p90Deg")} max {Get(sm, "maxDeg")} past45 {Get(sm, "pctPast45")}% | " +
            $"against {sv["againstAxisMm2"]} mm2 doubled {sv["doubledOverMm2"]} mm2 | cutter x{cutterCrossings} | " +
            $"pieces {pieces.Count} specks {specksFirst}->{specksFinal} | pull {(pa.TryGetValue("worstMm3", out var w) ? w : pa["reason"])}");

        if (string.IsNullOrWhiteSpace(directory)) return;

        Rgb Colour(int f) =>
            flange.Area[f] <= 0 || !flange.InMould[f] ? Dim
            : flange.InBody[f] ? SealStrip
            : flange.Facing[f] < -AgainstCos || doubledFaces[f] ? Fold
            : Ramp(flange.Slope[f]);

        string note = $"{Get(sm, "medianDeg")}/{Get(sm, "p90Deg")}/{Get(sm, "maxDeg")} deg";
        foreach (var view in new[] { new View("down the axis", -axis), new View("up the axis", axis), Views.OrbitA, new View("side", u) })
        {
            var camera = Camera.Fit(mould.Mesh, view, TileSize, TileSize);
            var raster = MeshRasterizer.Render(surface, camera, TileSize, TileSize, Options, Colour);
            foreach (var loop in line.Loops) MeshRasterizer.DrawPolyline(raster, camera, loop, true, LineColour, Options);
            tiles.Add(new Tile($"{sweep}: {view.Name}", raster, note));
            note = "";
        }

        if (pieces.Count > 0)
        {
            var exploded = Explode(pieces, axis, 25.0);
            var camera = Camera.Fit(exploded.Mesh, Views.OrbitA, TileSize, TileSize);
            tiles.Add(new Tile($"{sweep}: cut",
                MeshRasterizer.Render(exploded.Mesh, camera, TileSize, TileSize, Options,
                    face => MeshRasterizer.ContourColour(exploded.PieceOf[face] + 1)),
                $"{pieces.Count} piece(s), specks {specksFinal}"));
        }
        else
        {
            tiles.Add(new Tile($"{sweep}: cut", new Raster(TileSize, TileSize, Options.Background), "no pieces"));
        }

        // Where the halves foul each other when pulled: the piece left behind, seen from the side the
        // other one leaves by, with the worst overlap found drawn over it.
        if (rest is not null)
        {
            var from = new View("from the pulled side", axis * -pulledAlong);
            var camera = Camera.Fit(mould.Mesh, from, TileSize, TileSize);
            var raster = MeshRasterizer.Render(rest, camera, TileSize, TileSize, Options);
            if (overlap is not null)
            {
                var fouling = Options with { OccludedAlpha = 0.85 };
                var triangles = overlap.Triangles;
                var vertices = overlap.Vertices;
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                    MeshRasterizer.DrawPolyline(raster, camera,
                        [vertices[triangles[t]], vertices[triangles[t + 1]], vertices[triangles[t + 2]]], true, Fold, fouling);
            }

            foreach (var loop in line.Loops) MeshRasterizer.DrawPolyline(raster, camera, loop, true, LineColour, Options);
            tiles.Add(new Tile($"{sweep}: fouling when pulled", raster, $"worst {Get(pa, "worstMm3")} mm3"));
        }
        else
        {
            tiles.Add(new Tile($"{sweep}: fouling when pulled", new Raster(TileSize, TileSize, Options.Background), "not pulled"));
        }

        // A sweep that fails either separability gate gets a sheet of its own, large enough to see
        // where: the flange with its turned-over faces marked, and the piece left behind with the
        // overlap on it, each from the pulled side and from an orbit.
        if (sv["passes"] is true && pa["passes"] is true) return;

        const int large = 760;
        var close = new List<Tile>();
        foreach (var view in new[] { new View("from the pulled side", axis * -pulledAlong), Views.OrbitA, Views.OrbitB })
        {
            var camera = Camera.Fit(mould.Mesh, view, large, large);
            var raster = MeshRasterizer.Render(surface, camera, large, large, Options, Colour);
            foreach (var loop in line.Loops) MeshRasterizer.DrawPolyline(raster, camera, loop, true, LineColour, Options);
            close.Add(new Tile($"flange, {view.Name}", raster,
                $"against the axis {sv["againstAxisMm2"]} mm2, doubled over {sv["doubledOverMm2"]} mm2"));
        }

        if (rest is not null)
            foreach (var view in new[] { new View("from the pulled side", axis * -pulledAlong), Views.OrbitA, Views.OrbitB })
            {
                var camera = Camera.Fit(mould.Mesh, view, large, large);
                var raster = MeshRasterizer.Render(rest, camera, large, large, Options);
                if (overlap is not null)
                {
                    var fouling = Options with { OccludedAlpha = 0.85 };
                    for (int t = 0; t + 2 < overlap.Triangles.Length; t += 3)
                        MeshRasterizer.DrawPolyline(raster, camera,
                            [overlap.Vertices[overlap.Triangles[t]], overlap.Vertices[overlap.Triangles[t + 1]], overlap.Vertices[overlap.Triangles[t + 2]]],
                            true, Fold, fouling);
                }

                foreach (var loop in line.Loops) MeshRasterizer.DrawPolyline(raster, camera, loop, true, LineColour, Options);
                close.Add(new Tile($"half left behind, {view.Name}", raster, $"overlap when pulled {Get(pa, "worstMm3")} mm3"));
            }

        ContactSheet.Save(Path.Combine(directory, $"{id}-{sweep}-separability.png"), close, 3, large, Options.Background,
            $"{id} / {sweep}: where the halves do not part along the axis",
            [("flat", Flat), ("45 deg", Warm), ("past 45 deg", Steep), ("turned over / doubled over / overlap when pulled", Fold),
             ("inside the body", SealStrip), ("parting line", LineColour)]);
    }

    /// <summary>
    /// The direction, among a few thousand spread over the hemisphere round the axis, that the least
    /// mating area faces away from - and how much still does. Zero means the flange can be seen whole
    /// from somewhere, which is necessary for the halves to part that way though not sufficient.
    /// </summary>
    private static Dictionary<string, object?> BestPull(Flange flange, List<int> mating, Vector3 axis)
    {
        const int samples = 4000;
        var against = new double[samples];
        var directions = new Vector3[samples];
        double golden = Math.PI * (3.0 - Math.Sqrt(5.0));
        var (u, v) = Frame(axis);

        Parallel.For(0, samples, i =>
        {
            // A spiral over the hemisphere: even coverage without a grid's poles.
            double along = 1.0 - ((i + 0.5) / samples);
            double across = Math.Sqrt(1.0 - (along * along));
            var direction = (axis * along) + (u * (across * Math.Cos(golden * i))) + (v * (across * Math.Sin(golden * i)));
            directions[i] = direction;

            double total = 0.0;
            foreach (int f in mating)
                if (flange.Oriented[f].Dot(direction) < -AgainstCos) total += flange.Area[f];
            against[i] = total;
        });

        int best = 0;
        for (int i = 1; i < samples; i++)
            if (against[i] < against[best]) best = i;

        return new Dictionary<string, object?>
        {
            ["againstMm2"] = R1(against[best]),
            ["tiltFromAxisDeg"] = R1(Math.Acos(Math.Clamp(directions[best].Dot(axis), -1.0, 1.0)) * 180.0 / Math.PI),
        };
    }

    /// <summary>The flange a face at a time: area, which way it faces, and where it lies.</summary>
    private sealed class Flange
    {
        public IMesh Mesh { get; }
        public int Faces { get; }
        public double[] Area { get; }
        public Vector3[] Normal { get; }
        public Vector3[] Centre { get; }

        /// <summary>Normal against the axis, signed.</summary>
        public double[] Cos { get; }

        /// <summary>Degrees off the parting plane, as FlangeWaviness measures it: sign ignored.</summary>
        public double[] Slope { get; }

        public bool[] InMould { get; }
        public bool[] InBody { get; }

        /// <summary>Outside the body and inside the mould: the part the two halves meet on.</summary>
        public bool[] Mating { get; }

        /// <summary>
        /// Normal against the axis, turned so that positive is the way most of the face's own sheet
        /// faces. Negative is a face turned over against its neighbours.
        /// </summary>
        public double[] Facing { get; }

        /// <summary>The face's normal, turned the way most of its sheet faces along the axis.</summary>
        public Vector3[] Oriented { get; }

        public Flange(
            IMesh mesh, Vector3 axis,
            global::GeometryEngine.Core.Geometry.ISpatialIndex body,
            global::GeometryEngine.Core.Geometry.ISpatialIndex mould)
        {
            Mesh = mesh;
            var vs = mesh.Vertices;
            var ts = mesh.Triangles;
            Faces = ts.Length / 3;
            Area = new double[Faces];
            Normal = new Vector3[Faces];
            Centre = new Vector3[Faces];
            Cos = new double[Faces];
            Slope = new double[Faces];
            InMould = new bool[Faces];
            InBody = new bool[Faces];
            Mating = new bool[Faces];

            var centres = ImmutableArray.CreateBuilder<Vector3>(Faces);
            for (int f = 0; f < Faces; f++)
            {
                var a = vs[ts[3 * f]];
                var b = vs[ts[(3 * f) + 1]];
                var c = vs[ts[(3 * f) + 2]];
                Centre[f] = (a + b + c) / 3.0;
                centres.Add(Centre[f]);

                var cross = (b - a).Cross(c - a);
                double length = cross.Length;
                if (length < 1e-12) continue;

                Area[f] = length * 0.5;
                Normal[f] = cross / length;
                Cos[f] = Normal[f].Dot(axis);
                Slope[f] = Math.Acos(Math.Clamp(Math.Abs(Cos[f]), 0.0, 1.0)) * 180.0 / Math.PI;
            }

            var all = centres.MoveToImmutable();
            var toBody = body.SignedDistances(all);
            var toMould = mould.SignedDistances(all);
            for (int f = 0; f < Faces; f++)
            {
                InMould[f] = toMould[f] < 0.0;
                InBody[f] = toBody[f] <= 0.0;
                Mating[f] = InMould[f] && !InBody[f] && Area[f] > 0.0;
            }

            // Which way is "with the axis" is settled a sheet at a time. A flange with a hole patch is
            // several sheets laid side by side, and nothing winds them the same way round - so read
            // against one sign for the whole mesh, a patch wound the other way is all "against the
            // axis" while lying perfectly flat. Within one sheet the winding is continuous, and there a
            // face that disagrees with the rest really has turned over.
            var sheet = Enumerable.Range(0, Faces).ToArray();
            int Root(int x)
            {
                while (sheet[x] != x) x = sheet[x] = sheet[sheet[x]];
                return x;
            }

            var firstOn = new Dictionary<long, int>(Faces * 2);
            for (int f = 0; f < Faces; f++)
                for (int k = 0; k < 3; k++)
                {
                    int a = ts[(3 * f) + k], b = ts[(3 * f) + ((k + 1) % 3)];
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (firstOn.TryGetValue(key, out int other)) sheet[Root(f)] = Root(other);
                    else firstOn[key] = f;
                }

            var facing = new double[Faces];
            for (int f = 0; f < Faces; f++)
                if (Mating[f]) facing[Root(f)] += Area[f] * Cos[f];

            Facing = new double[Faces];
            Oriented = new Vector3[Faces];
            for (int f = 0; f < Faces; f++)
            {
                double sign = facing[Root(f)] >= 0.0 ? 1.0 : -1.0;
                Facing[f] = sign * Cos[f];
                Oriented[f] = Normal[f] * sign;
            }
        }
    }

    private static Dictionary<string, object?> Slopes(Flange flange, List<int> faces)
    {
        var sorted = faces.Select(f => (Deg: flange.Slope[f], Area: flange.Area[f])).OrderBy(x => x.Deg).ToList();
        double total = sorted.Sum(x => x.Area);
        if (total <= 0.0) return new Dictionary<string, object?> { ["areaMm2"] = 0.0 };

        double Percentile(double share)
        {
            double running = 0.0;
            foreach (var (deg, area) in sorted)
            {
                running += area;
                if (running >= total * share) return deg;
            }
            return sorted[^1].Deg;
        }

        return new Dictionary<string, object?>
        {
            ["areaMm2"] = R(total),
            ["medianDeg"] = R1(Percentile(0.5)),
            ["p90Deg"] = R1(Percentile(0.9)),
            ["maxDeg"] = R1(sorted[^1].Deg),
            ["pctPast45"] = R1(100.0 * sorted.Where(x => x.Deg > SteepDeg).Sum(x => x.Area) / total),
        };
    }

    /// <summary>
    /// How much of the flange a line parallel to the axis meets more than once. The faces are sampled
    /// onto a grid in the axis plane, each cell keeping the lowest and highest sheet over it; a cell
    /// whose two differ by more than <see cref="MultiValuedMm"/> has the surface over it twice.
    /// </summary>
    private static (int Covered, int Doubled) Doubled(
        Flange flange, List<int> faces, Vector3 u, Vector3 v, out bool[] doubledFaces)
    {
        doubledFaces = new bool[flange.Faces];
        if (faces.Count == 0) return (0, 0);

        var vs = flange.Mesh.Vertices;
        var ts = flange.Mesh.Triangles;
        var axis = u.Cross(v);

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (int f in faces)
            for (int k = 0; k < 3; k++)
            {
                var p = vs[ts[(3 * f) + k]];
                minX = Math.Min(minX, p.Dot(u)); maxX = Math.Max(maxX, p.Dot(u));
                minY = Math.Min(minY, p.Dot(v)); maxY = Math.Max(maxY, p.Dot(v));
            }

        int nx = (int)Math.Ceiling((maxX - minX) / GridMm) + 1;
        int ny = (int)Math.Ceiling((maxY - minY) / GridMm) + 1;
        var low = new float[nx * ny];
        var high = new float[nx * ny];
        Array.Fill(low, float.PositiveInfinity);
        Array.Fill(high, float.NegativeInfinity);

        void Sample(int f, Action<int, double> visit)
        {
            // A face edge-on to the axis covers no cell; it is a wall, not a second sheet.
            if (Math.Abs(flange.Cos[f]) < AgainstCos) return;

            var a = vs[ts[3 * f]];
            var b = vs[ts[(3 * f) + 1]];
            var c = vs[ts[(3 * f) + 2]];
            double ax = a.Dot(u), ay = a.Dot(v), bx = b.Dot(u), by = b.Dot(v), cx = c.Dot(u), cy = c.Dot(v);
            double area2 = ((bx - ax) * (cy - ay)) - ((by - ay) * (cx - ax));
            if (Math.Abs(area2) < 1e-12) return;

            int x0 = Math.Max(0, (int)Math.Floor((Math.Min(ax, Math.Min(bx, cx)) - minX) / GridMm));
            int x1 = Math.Min(nx - 1, (int)Math.Ceiling((Math.Max(ax, Math.Max(bx, cx)) - minX) / GridMm));
            int y0 = Math.Max(0, (int)Math.Floor((Math.Min(ay, Math.Min(by, cy)) - minY) / GridMm));
            int y1 = Math.Min(ny - 1, (int)Math.Ceiling((Math.Max(ay, Math.Max(by, cy)) - minY) / GridMm));

            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    double px = minX + (x * GridMm), py = minY + (y * GridMm);
                    double w0 = (((bx - px) * (cy - py)) - ((by - py) * (cx - px))) / area2;
                    double w1 = (((cx - px) * (ay - py)) - ((cy - py) * (ax - px))) / area2;
                    double w2 = 1.0 - w0 - w1;
                    if (w0 < 0.0 || w1 < 0.0 || w2 < 0.0) continue;

                    visit((y * nx) + x, (w0 * a.Dot(axis)) + (w1 * b.Dot(axis)) + (w2 * c.Dot(axis)));
                }
        }

        foreach (int f in faces)
            Sample(f, (cell, height) =>
            {
                low[cell] = Math.Min(low[cell], (float)height);
                high[cell] = Math.Max(high[cell], (float)height);
            });

        int covered = 0, doubled = 0;
        for (int i = 0; i < low.Length; i++)
        {
            if (float.IsPositiveInfinity(low[i])) continue;
            covered++;
            if (high[i] - low[i] > MultiValuedMm) doubled++;
        }

        var flags = doubledFaces;
        foreach (int f in faces)
            Sample(f, (cell, _) => { if (high[cell] - low[cell] > MultiValuedMm) flags[f] = true; });

        return (covered, doubled);
    }

    /// <summary>
    /// The angle between neighbouring mating faces, in two families. An edge that runs out from the line
    /// has its two faces side by side round the loop, so its angle is ripple; an edge that runs along
    /// the loop has one face further out than the other, so its angle is the surface bending as it
    /// goes outward. Weighted by edge length, so a swarm of slivers does not outvote the face they sit on.
    /// </summary>
    private static Dictionary<string, object?> Dihedrals(
        Flange flange, List<int> faces, Vector3[] linePoints, Vector3[] lineTangents, Vector3 axis)
    {
        var ts = flange.Mesh.Triangles;
        var vs = flange.Mesh.Vertices;
        var include = new bool[flange.Faces];
        foreach (int f in faces) include[f] = true;

        var first = new Dictionary<long, int>(faces.Count * 2);
        var radial = new List<(double Deg, double Length)>();
        var around = new List<(double Deg, double Length)>();

        foreach (int f in faces)
            for (int k = 0; k < 3; k++)
            {
                int a = ts[(3 * f) + k], b = ts[(3 * f) + ((k + 1) % 3)];
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (!first.TryGetValue(key, out int other))
                {
                    first[key] = f;
                    continue;
                }

                var edge = vs[b] - vs[a];
                double length = edge.Length;
                if (length < 1e-9) continue;

                double deg = Math.Acos(Math.Clamp(flange.Normal[f].Dot(flange.Normal[other]), -1.0, 1.0)) * 180.0 / Math.PI;

                var mid = (vs[a] + vs[b]) * 0.5;
                int nearest = Nearest(mid, linePoints);
                var along = Flatten(lineTangents[nearest], axis);
                var flat = Flatten(edge, axis);
                bool runsAlongLoop = along.Length > 1e-9 && flat.Length > 1e-9
                    && Math.Abs(along.Normalize().Dot(flat.Normalize())) >= Math.Sqrt(0.5);

                (runsAlongLoop ? around : radial).Add((deg, length));
            }

        return new Dictionary<string, object?>
        {
            ["rippleAcrossRadialEdges"] = Weighted(radial),
            ["bendAcrossLoopEdges"] = Weighted(around),
        };

        static Dictionary<string, object?> Weighted(List<(double Deg, double Length)> edges)
        {
            if (edges.Count == 0) return new Dictionary<string, object?> { ["edges"] = 0 };

            edges.Sort((x, y) => x.Deg.CompareTo(y.Deg));
            double total = edges.Sum(e => e.Length);

            double Percentile(double share)
            {
                double running = 0.0;
                foreach (var (deg, length) in edges)
                {
                    running += length;
                    if (running >= total * share) return deg;
                }
                return edges[^1].Deg;
            }

            return new Dictionary<string, object?>
            {
                ["edges"] = edges.Count,
                ["medianDeg"] = R1(Percentile(0.5)),
                ["p99Deg"] = R1(Percentile(0.99)),
                ["maxDeg"] = R1(edges[^1].Deg),
            };
        }
    }

    /// <summary>
    /// Every patch of the flange inside the mould that is steeper than the print limit, with the two
    /// things that can make a steep face unavoidable measured beside it: how much of the patch is the
    /// seal strip inside the body, and how steeply the line itself climbs where the patch meets it. A
    /// surface that contains a curve is at least as steep as the curve, so a line climbing at 60
    /// degrees puts a 60-degree face beside it whatever the sweep does.
    /// </summary>
    private static Dictionary<string, object?> SteepRegions(
        Flange flange, List<int> faces, Vector3[] linePoints, Vector3[] lineTangents, Vector3 axis)
    {
        var ts = flange.Mesh.Triangles;
        var steep = new bool[flange.Faces];
        foreach (int f in faces) steep[f] = flange.Slope[f] > SteepDeg;

        var edgeFaces = new Dictionary<long, List<int>>();
        foreach (int f in faces)
        {
            if (!steep[f]) continue;
            for (int k = 0; k < 3; k++)
            {
                int a = ts[(3 * f) + k], b = ts[(3 * f) + ((k + 1) % 3)];
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (!edgeFaces.TryGetValue(key, out var list)) edgeFaces[key] = list = new List<int>(2);
                list.Add(f);
            }
        }

        var climb = lineTangents
            .Select(t => t.Length < 1e-9 ? 0.0 : Math.Asin(Math.Min(1.0, Math.Abs(t.Normalize().Dot(axis)))) * 180.0 / Math.PI)
            .ToArray();

        // Decided a face at a time and only then grouped, so one steep stretch of line cannot excuse a
        // patch that runs on round the loop past where the line has levelled off.
        var nearestOf = new int[flange.Faces];
        var causeOf = new string?[flange.Faces];
        foreach (int f in faces)
        {
            if (!steep[f]) continue;
            nearestOf[f] = Nearest(flange.Centre[f], linePoints);
            causeOf[f] = flange.InBody[f] ? "seal"
                : climb[nearestOf[f]] >= SteepDeg ? "line climb"
                : "neither";
        }

        var seen = new bool[flange.Faces];
        var regions = new List<(double Area, double MeanDeg, double MaxDeg, double LineClimb, double ToLine, Vector3 At, string Cause)>();
        var stack = new Stack<int>();

        foreach (int seed in faces)
        {
            if (!steep[seed] || seen[seed]) continue;

            double area = 0, weighted = 0, max = 0, lineClimb = 0, toLine = double.MaxValue;
            var at = Vector3.Zero;
            seen[seed] = true;
            stack.Push(seed);
            while (stack.Count > 0)
            {
                int f = stack.Pop();
                area += flange.Area[f];
                weighted += flange.Area[f] * flange.Slope[f];
                max = Math.Max(max, flange.Slope[f]);
                at += flange.Centre[f] * flange.Area[f];
                lineClimb = Math.Max(lineClimb, climb[nearestOf[f]]);
                toLine = Math.Min(toLine, flange.Centre[f].DistanceTo(linePoints[nearestOf[f]]));

                for (int k = 0; k < 3; k++)
                {
                    int a = ts[(3 * f) + k], b = ts[(3 * f) + ((k + 1) % 3)];
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    foreach (int g in edgeFaces[key])
                    {
                        if (seen[g] || causeOf[g] != causeOf[seed]) continue;
                        seen[g] = true;
                        stack.Push(g);
                    }
                }
            }

            regions.Add((area, weighted / area, max, lineClimb, toLine, at / area, causeOf[seed]!));
        }

        regions.Sort((x, y) => y.Area.CompareTo(x.Area));

        return new Dictionary<string, object?>
        {
            ["regions"] = regions.Count,
            ["areaByCauseMm2"] = regions.GroupBy(r => r.Cause).ToDictionary(g => g.Key, g => (object?)R1(g.Sum(r => r.Area))),
            ["largest"] = regions.Take(6).Select(r => new Dictionary<string, object?>
            {
                ["areaMm2"] = R1(r.Area),
                ["cause"] = r.Cause,
                ["meanDeg"] = R1(r.MeanDeg),
                ["maxDeg"] = R1(r.MaxDeg),
                ["lineClimbDeg"] = R1(r.LineClimb),
                ["nearestToLineMm"] = R1(r.ToLine),
                ["at"] = new[] { R1(r.At.X), R1(r.At.Y), R1(r.At.Z) },
            }).ToArray(),
        };
    }

    /// <summary>
    /// Whether the cut left the mould's own surface where it was. Asked both ways, away from the
    /// cutter: every mould vertex should still be on a piece, and every piece vertex should be on the
    /// mould. The cavity - mould vertices lying on the body - is reported on its own, since that is the
    /// surface the bolus is cast against.
    /// </summary>
    private Dictionary<string, object?> Unchanged(
        IMesh mould, IMesh cutter, IReadOnlyList<IMesh> components,
        global::GeometryEngine.Core.Geometry.ISpatialIndex bodyIndex,
        global::GeometryEngine.Core.Geometry.ISpatialIndex mouldIndex)
    {
        using var cutterIndex = _engine.Spatial.BuildIndex(cutter).Value;
        var joined = Join(components);
        using var piecesIndex = _engine.Spatial.BuildIndex(joined).Value;

        var mouldVertices = mould.Vertices;
        var toCutter = cutterIndex.SignedDistances(mouldVertices);
        var toBody = bodyIndex.SignedDistances(mouldVertices);
        var toPieces = piecesIndex.SignedDistances(mouldVertices);

        double cavityWorst = 0, mouldWorst = 0;
        int cavityMoved = 0, cavityChecked = 0, mouldMoved = 0;
        for (int i = 0; i < mouldVertices.Length; i++)
        {
            if (Math.Abs(toCutter[i]) < AwayFromCutMm) continue;

            double off = Math.Abs(toPieces[i]);
            mouldWorst = Math.Max(mouldWorst, off);
            if (off > UnchangedMm) mouldMoved++;

            if (Math.Abs(toBody[i]) > 0.05) continue;
            cavityChecked++;
            cavityWorst = Math.Max(cavityWorst, off);
            if (off > UnchangedMm) cavityMoved++;
        }

        var pieceVertices = joined.Vertices;
        var pieceToCutter = cutterIndex.SignedDistances(pieceVertices);
        var pieceToMould = mouldIndex.SignedDistances(pieceVertices);
        double addedWorst = 0;
        int added = 0;
        for (int i = 0; i < pieceVertices.Length; i++)
        {
            if (Math.Abs(pieceToCutter[i]) < AwayFromCutMm) continue;

            double off = Math.Abs(pieceToMould[i]);
            addedWorst = Math.Max(addedWorst, off);
            if (off > UnchangedMm) added++;
        }

        return new Dictionary<string, object?>
        {
            ["cavityVerticesChecked"] = cavityChecked,
            ["cavityMoved"] = cavityMoved,
            ["cavityWorstMm"] = R(cavityWorst),
            ["mouldVerticesMoved"] = mouldMoved,
            ["mouldWorstMm"] = R(mouldWorst),
            ["pieceVerticesOffMould"] = added,
            ["pieceWorstMm"] = R(addedWorst),
            ["passes"] = cavityMoved == 0 && mouldMoved == 0 && added == 0,
        };
    }

    /// <summary>
    /// Slides the biggest piece away from the rest along the axis and intersects them at each step. Two
    /// halves that part cleanly never overlap at any distance; a flange that loops back leaves one
    /// hooked under the other, and they overlap as soon as it moves. Which way is "away" is not known
    /// from the pieces, so both are tried at one distance and the one that overlaps less is followed.
    /// </summary>
    private Dictionary<string, object?> PullApart(
        IReadOnlyList<IMesh> pieces, Vector3 axis, out IMesh rest, out IMesh? worstOverlap, out double pulledAlong)
    {
        var moving = pieces[0];
        var others = pieces.Count == 2 ? pieces[1] : Join(pieces.Skip(1).ToList());
        rest = others;

        // Kept only once a direction is chosen, so neither trial below is taken for the worst overlap.
        bool keep = false;
        IMesh? worstMesh = null;
        double worstVolume = 0.0;

        double Overlap(double signedDistance)
        {
            var moved = _engine.Transforms.Translate(moving, axis * signedDistance);
            if (moved.IsFailure) return double.NaN;
            var overlap = _engine.Booleans.Intersect(moved.Value, others);
            if (overlap.IsFailure) return double.NaN;
            if (overlap.Value.IsEmpty) return 0.0;

            double volume = PartingPieces.Volume(overlap.Value);
            if (keep && volume > worstVolume)
            {
                worstVolume = volume;

                // Slid back by the pull, so it lies on the piece left behind where the fouling starts.
                var back = _engine.Transforms.Translate(overlap.Value, axis * -signedDistance);
                worstMesh = back.IsSuccess ? back.Value : overlap.Value;
            }
            return volume;
        }

        double up = Overlap(1.0), down = Overlap(-1.0);
        double direction = double.IsNaN(down) || up <= down ? 1.0 : -1.0;
        keep = true;

        var volumes = new Dictionary<string, object?>();
        double worst = 0.0;
        bool measured = true;
        foreach (double offset in PullOffsetsMm)
        {
            double volume = Overlap(direction * offset);
            volumes[$"{offset:0.#}mm"] = R(volume);
            if (double.IsNaN(volume)) measured = false;
            else worst = Math.Max(worst, volume);
        }

        worstOverlap = worstMesh;
        pulledAlong = direction;

        return new Dictionary<string, object?>
        {
            ["direction"] = direction > 0 ? "+axis" : "-axis",
            ["otherWayAt1mmMm3"] = R(direction > 0 ? down : up),
            ["overlapMm3"] = volumes,
            ["worstMm3"] = R(worst),
            ["measured"] = measured,
            ["passes"] = measured && worst < 1.0,
        };
    }

    // ==================================================================== pictures

    private const int TileSize = 440;
    private static readonly RenderOptions Options = RenderOptions.Default;
    private static readonly Rgb LineColour = new(80, 220, 255);
    private static readonly Rgb Flat = new(60, 200, 110);
    private static readonly Rgb Warm = new(240, 210, 70);
    private static readonly Rgb Steep = new(235, 70, 60);
    private static readonly Rgb Fold = new(230, 60, 220);
    private static readonly Rgb SealStrip = new(110, 130, 190);
    private static readonly Rgb Dim = new(70, 72, 80);
    private static readonly Rgb CreaseA = new(255, 170, 60);
    private static readonly Rgb CreaseB = new(255, 230, 90);

    private static Rgb Ramp(double slope) =>
        slope > SteepDeg ? Steep : Rgb.Lerp(Flat, Warm, Math.Clamp(slope / SteepDeg, 0.0, 1.0));

    /// <summary>
    /// The line on its body, coloured by how sharply it turns, from four sides and close in on the two
    /// places it is worst: the sharpest turn, and the point furthest from the middle of its wall.
    /// </summary>
    private void DrawLine(
        string path, string id, IMesh body, PartingLine line, IReadOnlyList<PartingBand> bands, Vector3 axis,
        List<double[]> turnings, List<double[]?> biases)
    {
        const double fullScale = 20.0;
        var tiles = new List<Tile>();

        void Paint(Raster raster, Camera camera)
        {
            foreach (var band in bands)
            {
                MeshRasterizer.DrawPolyline(raster, camera, band.First.Points, true, CreaseA, Options);
                MeshRasterizer.DrawPolyline(raster, camera, band.Second.Points, true, CreaseB, Options);
            }

            for (int l = 0; l < line.Loops.Count; l++)
            {
                var turning = turnings[l];
                int count = turning.Length;
                MeshRasterizer.DrawPolyline(raster, camera, line.Loops[l], true,
                    i => Rgb.Lerp(LineColour, Steep, Math.Clamp(Math.Max(turning[i], turning[(i + 1) % count]) / fullScale, 0.0, 1.0)),
                    Options);
            }
        }

        foreach (var view in new[] { new View("down the axis", -axis), new View("up the axis", axis), Views.OrbitA, Views.OrbitB })
        {
            var camera = Camera.Fit(body, view, TileSize, TileSize);
            var raster = MeshRasterizer.Render(body, camera, TileSize, TileSize, Options);
            Paint(raster, camera);
            tiles.Add(new Tile(view.Name, raster));
        }

        // The worst places, each seen along the body's own normal there would be ideal; down the axis
        // and from an orbit is what shows a kink on a wall of any tilt without choosing per body.
        int worstLoop = Enumerable.Range(0, turnings.Count).OrderByDescending(l => turnings[l].Max()).First();
        int worstTurn = Array.IndexOf(turnings[worstLoop], turnings[worstLoop].Max());
        var sharpest = line.Loops[worstLoop][worstTurn];
        foreach (var view in new[] { new View("down the axis", -axis), Views.OrbitA })
        {
            var camera = Camera.Focus(body, view, sharpest, 14.0, TileSize, TileSize);
            var raster = MeshRasterizer.Render(body, camera, TileSize, TileSize, Options);
            Paint(raster, camera);
            MeshRasterizer.DrawMarker(raster, camera, sharpest, Fold, Options, 5);
            tiles.Add(new Tile($"sharpest turn, {view.Name}", raster, $"{turnings[worstLoop][worstTurn]:F1} deg"));
        }

        var offCentre = Enumerable.Range(0, biases.Count)
            .Where(l => biases[l] is not null)
            .SelectMany(l => biases[l]!.Select((b, i) => (Loop: l, Index: i, Off: Math.Abs(b - 0.5))))
            .OrderByDescending(x => x.Off)
            .FirstOrDefault();
        if (biases.Any(b => b is not null))
        {
            var furthest = line.Loops[offCentre.Loop][offCentre.Index];
            foreach (var view in new[] { new View("down the axis", -axis), Views.OrbitA })
            {
                var camera = Camera.Focus(body, view, furthest, 14.0, TileSize, TileSize);
                var raster = MeshRasterizer.Render(body, camera, TileSize, TileSize, Options);
                Paint(raster, camera);
                MeshRasterizer.DrawMarker(raster, camera, furthest, Fold, Options, 5);
                tiles.Add(new Tile($"furthest off centre, {view.Name}", raster, $"{offCentre.Off + 0.5:F2} across"));
            }
        }

        ContactSheet.Save(path, tiles, 4, TileSize, Options.Background,
            $"{id}: parting line (redder = sharper turn, full red at {fullScale:F0} deg)",
            [("parting line", LineColour), ("sharp turn", Steep), ("rim crease", CreaseA), ("other crease", CreaseB), ("marked point", Fold)]);
    }

    /// <summary>The pieces as one mesh, each pushed away from the others' middle along the axis.</summary>
    private (IMesh Mesh, int[] PieceOf) Explode(IReadOnlyList<IMesh> pieces, Vector3 axis, double gap)
    {
        double middle = pieces.Average(p => p.Vertices.Average(x => x.Dot(axis)));
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var pieceOf = new List<int>();

        for (int k = 0; k < pieces.Count; k++)
        {
            var piece = pieces[k];
            double height = piece.Vertices.Average(x => x.Dot(axis));
            var shift = pieces.Count == 1 ? Vector3.Zero : axis * (height >= middle ? gap : -gap);
            int start = vertices.Count;

            vertices.AddRange(piece.Vertices.Select(x => x + shift));
            triangles.AddRange(piece.Triangles.Select(t => t + start));
            pieceOf.AddRange(Enumerable.Repeat(k, piece.Triangles.Length / 3));
        }

        return (_engine.CreateMesh([.. vertices], [.. triangles], pieces[0].Metadata).Value, pieceOf.ToArray());
    }

    // ==================================================================== helpers

    private (MouldMesh Mould, MeshRecord Record) Open(string path)
    {
        var workspace = new ImportMesh(_engine).Execute(Workspace.CreateEmpty(), path).Value;
        var mesh = workspace.GetActiveMesh().Value;
        var record = workspace.GetActiveRecord().Value;
        return (MouldMesh.Create(mesh, record).Value, record);
    }

    private IMesh Join(IReadOnlyList<IMesh> pieces)
    {
        if (pieces.Count == 1) return pieces[0];

        var vertices = ImmutableArray.CreateBuilder<Vector3>(pieces.Sum(p => p.VertexCount));
        var triangles = ImmutableArray.CreateBuilder<int>(pieces.Sum(p => p.Triangles.Length));
        foreach (var piece in pieces)
        {
            int offset = vertices.Count;
            vertices.AddRange(piece.Vertices);
            foreach (int corner in piece.Triangles) triangles.Add(corner + offset);
        }

        return _engine.CreateMesh(vertices.MoveToImmutable(), triangles.MoveToImmutable(), pieces[0].Metadata).Value;
    }

    private static Vector3 RotateAboutX(Vector3 p, double degrees)
    {
        double a = degrees * Math.PI / 180.0, c = Math.Cos(a), s = Math.Sin(a);
        return new Vector3(p.X, (p.Y * c) - (p.Z * s), (p.Y * s) + (p.Z * c));
    }

    private static int Nearest(Vector3 point, Vector3[] points)
    {
        int nearest = 0;
        double best = double.MaxValue;
        for (int i = 0; i < points.Length; i++)
        {
            double d = point.DistanceSquared(points[i]);
            if (d >= best) continue;
            best = d;
            nearest = i;
        }
        return nearest;
    }

    private static Vector3 Flatten(Vector3 vector, Vector3 axis) => vector - (axis * vector.Dot(axis));

    private static double Angle(Vector3 a, Vector3 b) =>
        Math.Acos(Math.Clamp(a.Normalize().Dot(b.Normalize()), -1.0, 1.0)) * 180.0 / Math.PI;

    private static double SegmentDistance(Vector3 c, Vector3 p, Vector3 q)
    {
        var d = q - p;
        double length = d.LengthSquared;
        double t = length < 1e-12 ? 0.0 : Math.Clamp((c - p).Dot(d) / length, 0.0, 1.0);
        return c.DistanceTo(p + (d * t));
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

    /// <summary>How many times the loop's footprint along the axis crosses itself.</summary>
    private static int FootprintCrossings(IReadOnlyList<Vector3> loop, Vector3 u, Vector3 v)
    {
        int n = loop.Count;
        var x = loop.Select(p => p.Dot(u)).ToArray();
        var y = loop.Select(p => p.Dot(v)).ToArray();
        int crossings = 0;

        for (int i = 0; i < n; i++)
            for (int j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1) continue;   // neighbours across the seam

                int i2 = (i + 1) % n, j2 = (j + 1) % n;
                double d1 = Side(x[i], y[i], x[i2], y[i2], x[j], y[j]);
                double d2 = Side(x[i], y[i], x[i2], y[i2], x[j2], y[j2]);
                double d3 = Side(x[j], y[j], x[j2], y[j2], x[i], y[i]);
                double d4 = Side(x[j], y[j], x[j2], y[j2], x[i2], y[i2]);
                if (d1 * d2 < 0 && d3 * d4 < 0) crossings++;
            }

        return crossings;

        static double Side(double ax, double ay, double bx, double by, double px, double py) =>
            ((bx - ax) * (py - ay)) - ((by - ay) * (px - ax));
    }

    private static double Median(double[] values)
    {
        if (values.Length == 0) return 0.0;
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }

    private static object? Get(Dictionary<string, object?> values, string key) =>
        values.TryGetValue(key, out var value) ? value : "-";

    private static double R(double value) => double.IsFinite(value) ? Math.Round(value, 4) : value;

    private static double R1(double value) => double.IsFinite(value) ? Math.Round(value, 1) : value;

    private static string Asset(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "files")))
            dir = dir.Parent;

        return Path.Combine(dir!.FullName, "tests", "files", "3mf", name);
    }
}
