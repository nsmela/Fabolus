using System.Numerics;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// Where the traced parting line sits inside the rim band, before and after
/// <see cref="PartingLineCentring"/>.
///
/// <para>
/// The aggregate ridge-to-seam distance already in the report cannot answer this. It takes the mean
/// over every contour point of the distance to the nearest seam, so a line lying hard against one
/// edge of the band reads the same as one running down the middle: the near edge contributes almost
/// nothing and the far edge almost a whole width, and the two average back to half a width either
/// way. Uniform drift is invisible to it by construction.
/// </para>
///
/// <para>
/// Asked from the seam instead, and per point, the question has an answer. At each seam sample the
/// distance to each of the rim's two contours gives a bias <c>dA / (dA + dB)</c> that is 0.5 when the
/// point is centred, 0 when it lies on one crease and 1 when it lies on the other - dimensionless, so
/// bodies of different wall thickness compare directly, and per point, so a local excursion cannot
/// hide inside a mean. The span <c>dA + dB</c> is reported beside it because the bias is only
/// meaningful where the band has a width to be in the middle of.
/// </para>
///
/// <para>
/// Set <c>FABOLUS_RIDGE_REPORT_DIR</c> to also write the before-and-after pair as pictures.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class PartingLineCentringSweep
{
    private const int Panel = 780;
    private const int Large = 1600;
    private static readonly Rgb Background = new(24, 26, 32);
    private static readonly Rgb Contour = new(230, 70, 180);
    private static readonly Rgb Before = new(255, 170, 60);
    private static readonly Rgb After = new(64, 224, 208);

    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;
    private readonly ITestOutputHelper _log;

    public PartingLineCentringSweep(GeometryEngineFixture assets, ITestOutputHelper log)
    {
        _assets = assets;
        _engine = assets.Engine;
        _log = log;
    }

    public static readonly (string Id, string Asset)[] Models =
    {
        ("chin", "3mf/chin.3mf"),
        ("ear", "3mf/ear.3mf"),
        ("eye", "3mf/eye.3mf"),
        ("larynx-large", "3mf/larynx_large.3mf"),
        ("larynx-small", "3mf/larynx_small.3mf"),
        ("nose", "3mf/nose.3mf"),
        ("scalp", "3mf/scalp.3mf"),
        ("standard", "3mf/test bolus standard.3mf"),
    };

    /// <summary>
    /// The bodies the parting-solid tests are actually run on. A separate set from <see cref="Models"/>
    /// and the reason a round of this went wrong: the centring was measured and looked at on the 3mf
    /// bodies while the suite judged it on these, so a body could read clean on every statistic here
    /// and still fail there with nothing in the report to say why.
    /// </summary>
    public static readonly string[] Bodies =
    {
        "chin_bolus.stl", "ear_bolus.stl", "eye_bolus.stl",
        "larynx_bolus.stl", "nose_bolus.stl", "scalp_bolus.stl",
    };

    [Fact]
    public void HowFarOffCentreTheLineSitsOnTheStlBodies()
    {
        string? directory = Environment.GetEnvironmentVariable("FABOLUS_RIDGE_REPORT_DIR");

        foreach (string file in Bodies)
        {
            // Already a body surface, so no mould is built and no metadata is needed - which is why
            // these never appear in the 3mf sweep.
            Measure(Path.GetFileNameWithoutExtension(file), _assets.LoadStl(file), directory);
        }
    }

    [Fact]
    public void HowFarOffCentreTheLineSits()
    {
        string? directory = Environment.GetEnvironmentVariable("FABOLUS_RIDGE_REPORT_DIR");

        foreach (var (id, asset) in Models)
        {
            var imported = _engine.IO.Import(_assets.GetAssetPath(asset));
            var mould = MouldMesh.Create(imported.Value);
            Measure(id, new PartingMeshFeature(_engine).GetBodyMesh(mould.Value).Value.Mesh, directory);
        }
    }

    private void Measure(string id, IMesh body, string? directory)
    {
        {

            var thickness = _engine.Evaluators.MeasureWallThickness(body, WallThicknessOptions.Default);
            if (thickness.IsFailure)
            {
                _log.WriteLine($"{id}: no thickness - {thickness.Error.Description}");
                return;
            }

            var projector = _engine.PartingTools.CreateSurfaceProjector(body);
            var surface = projector.IsSuccess ? projector.Value : null;

            var traced = ThicknessParting.Trace(
                body, thickness.Value, ThicknessPartingOptions.Default, surface);

            if (traced.IsFailure)
            {
                _log.WriteLine($"{id}: no seam - {traced.Error.Description}");
                return;
            }

            float wall = thickness.Value.Statistics.Median;
            var contours = RidgeDetection.FindRidgeContours(body, RidgeDetectionOptions.Default)
                .Where(c => c.IsClosed).ToList();
            var rims = PartingStrategy.Rims(contours, wall);

            // Only the wall rims. A single ridge is the line already and a merged rim cannot say which
            // contour bounds which side, so neither has a band to be centred in.
            var walls = rims.Where(r => r.Kind == PartingRimKind.Wall).ToList();
            var bands = walls
                .Select(r => new PartingBand(contours[r.ContourIndices[0]], contours[r.ContourIndices[1]]))
                .ToList();

            var centred = PartingLineCentring.Centre(
                traced.Value, bands, PartingLineCentringOptions.Default, surface);

            _log.WriteLine($"{id}  wall {wall:F2} mm, {rims.Count} rim(s), " +
                           $"{walls.Count} wall rim(s), {traced.Value.Loops.Count} seam loop(s)");

            foreach (var rim in rims)
            {
                if (rim.Kind != PartingRimKind.Wall)
                {
                    _log.WriteLine($"  rim {rim.Id}: {rim.Kind} - no band to be centred in, skipped");
                    continue;
                }

                var band = bands[walls.IndexOf(rim)];
                _log.WriteLine($"  rim {rim.Id}: band {band.Span:F2} mm ({band.Span / wall:F2} x wall)");
                Report("    before", Sample(traced.Value, band, bands));
                Report("    after ", Sample(centred, band, bands));
            }

            _log.WriteLine($"  line moved: {Moved(traced.Value, centred)}");

            // The same question asked of the shaded region instead of the contours. These are two
            // different bands: the contours are the crease curves the centring aims between, while the
            // scene shades RidgeSurfaces.Faces, and the user judges the line against what is shaded. If
            // the face region is not symmetric about the contour midline then a line perfectly centred
            // between the contours sits visibly off centre in the blue, and the eye is right.
            var region = RidgeDetection.FindRidge(body, RidgeDetectionOptions.Default);

            // How wide the shaded strip is, from its own area spread along its own length, against how
            // far apart the creases run. A strip of constant width has area equal to width times
            // length, so the two numbers describe the same band only if they agree - and if the shaded
            // one is the wider, then a line centred between the creases sits off centre in the blue
            // however right the arithmetic was.
            float bandArea = 0f;
            var vertices = body.Vertices;
            var triangles = body.Triangles;
            var shaded = region.Band.Length == region.Faces.Length ? region.Band : region.Faces;
            for (int f = 0; f < shaded.Length; f++)
            {
                if (!shaded[f]) continue;
                var a = vertices[triangles[f * 3]];
                var b = vertices[triangles[(f * 3) + 1]];
                var c = vertices[triangles[(f * 3) + 2]];
                bandArea += Vector3.Cross(b - a, c - a).Length() * 0.5f;
            }

            float length = bands.Count > 0
                ? bands.Sum(band => Perimeter(band.First.Points))
                : 0f;

            _log.WriteLine($"  enclosed holes: {EnclosedHoles(body, shaded)}");

            // Where the band and the line sit on a signed coordinate across the wall: 0 on the first
            // crease, 1 on the second, and outside that range beyond them. The bias reported above
            // cannot say this. It is built from two chord distances, so a face just inside a crease and
            // one well beyond it both read a small distance to that crease and a large one to the far
            // one - which is to say the measure folds at the creases and calls both sides the same. If
            // the band turns out to straddle the creases unevenly, its own middle is not 0.5 and a line
            // sitting exactly at 0.5 is off centre in the thing being shaded.
            if (bands.Count > 0)
            {
                _log.WriteLine($"  band faces across the wall: {AcrossBand(body, shaded, bands[0])}");
                _log.WriteLine($"  line across the wall      : {AcrossLine(centred, bands[0])}");
            }

            if (length > 1e-3f)
                _log.WriteLine(
                    $"  shaded region: area {bandArea:F0} mm^2 over {length:F0} mm of crease " +
                    $"= {bandArea / length:F2} mm wide, against crease spacing " +
                    $"{bands[0].Span:F2} mm  ({bandArea / length / bands[0].Span:F2} x)");

            // Centring is only worth having if the line is still usable afterwards. A flange is swept
            // along this loop, so a pass that improved its position while pinching it against itself,
            // bunching its samples or lifting it off the body would have made it worse for the one
            // thing it is for.
            // Three lines, not two. The centring and the kink smoothing both move the line, and
            // reporting only the pair traced-versus-final attributes whatever changed to whichever of
            // them happens to be under discussion - which is exactly the mistake that had the smoothing
            // blamed for a rise in chin's turn statistics that the centring had caused.
            var centredOnly = PartingLineCentring.Centre(
                traced.Value, bands, PartingLineCentringOptions.Default with { OutlierPasses = 0 },
                surface);

            _log.WriteLine($"  integrity traced  : {Integrity(traced.Value, surface)}");
            _log.WriteLine($"  integrity centred : {Integrity(centredOnly, surface)}");
            _log.WriteLine($"  integrity smoothed: {Integrity(centred, surface)}");

            // The scene draws the body's surface normal at each point of the line as an arrow, and
            // that overlay is what the user actually looks at. Normals that disagree sharply from one
            // sample to the next read as a violent zigzag along the rim however smooth the line itself
            // is - so the line can be correct and the picture still say it is broken. Worth measuring
            // separately for the same reason: the flange is swept along these normals, so where they
            // alternate the surface built on them does too.
            _log.WriteLine($"  normals before: {Normals(body, traced.Value)}");
            _log.WriteLine($"  normals after : {Normals(body, centred)}");

            if (directory is not null && bands.Count > 0)
            {
                Draw(Path.Combine(directory, id), id, body, contours, traced.Value, centred);
                DrawScene(Path.Combine(directory, id), id, body, shaded, contours, traced.Value, centred);
            }
        }
    }

    // ---------------------------------------------------------------- measurement

    private sealed record Sampled(float[] Bias, float[] Span, float[] Offset)
    {
        public int Count => Bias.Length;
    }

    /// <summary>
    /// Every point of the line that runs along this band, with how far across it each one sits.
    /// </summary>
    private static Sampled Sample(PartingLine line, PartingBand band, IReadOnlyList<PartingBand> all)
    {
        var bias = new List<float>();
        var span = new List<float>();
        var offset = new List<float>();

        foreach (var loop in line.Loops)
            foreach (var point in loop)
            {
                // Measured against the band the point actually runs along, so a body with two rims
                // judges each against its own wall rather than against whichever is nearest overall.
                if (!ReferenceEquals(Nearest(point, all), band)) continue;

                float toFirst = Closest(point, band.First);
                float toSecond = Closest(point, band.Second);
                float width = toFirst + toSecond;
                if (width < 1e-4f) continue;

                bias.Add(toFirst / width);
                span.Add(width);
                offset.Add((toFirst - toSecond) * 0.5f);
            }

        return new Sampled(bias.ToArray(), span.ToArray(), offset.ToArray());
    }

    private void Report(string label, Sampled s)
    {
        if (s.Count == 0)
        {
            _log.WriteLine($"{label}: no samples along this band");
            return;
        }

        int soft = s.Bias.Count(t => t < 0.4f || t > 0.6f);
        int hard = s.Bias.Count(t => t < 0.25f || t > 0.75f);

        // Where the two failures coincide. A line that only wanders where the span has also collapsed
        // is one whose creases cannot say where the middle is there, which is a place to leave alone
        // rather than a place to correct.
        var wandering = Enumerable.Range(0, s.Count).Where(i => MathF.Abs(s.Bias[i] - 0.5f) > 0.25f).ToArray();
        string pinch = wandering.Length == 0
            ? ""
            : $", span there {Median(wandering.Select(i => s.Span[i]).ToArray()) / Median(s.Span):F2} x median";

        _log.WriteLine(
            $"{label}: n {s.Count}  bias mean {s.Bias.Average():F3} median {Median(s.Bias):F3} " +
            $"p5 {Percentile(s.Bias, 0.05f):F3} p95 {Percentile(s.Bias, 0.95f):F3} " +
            $"min {s.Bias.Min():F3} max {s.Bias.Max():F3}");
        _log.WriteLine(
            $"{label}  offset median {Median(s.Offset):+0.00;-0.00} mm  |max| {s.Offset.Max(MathF.Abs):F2} mm  " +
            $"outside 0.40-0.60 {(float)soft / s.Count:P1}  outside 0.25-0.75 {(float)hard / s.Count:P1}{pinch}");
    }

    /// <summary>How far the pass actually shifted the line, which is what says it did nothing where it should not.</summary>
    private static string Moved(PartingLine before, PartingLine after)
    {
        var steps = new List<float>();
        for (int loop = 0; loop < before.Loops.Count && loop < after.Loops.Count; loop++)
            for (int i = 0; i < before.Loops[loop].Count && i < after.Loops[loop].Count; i++)
                steps.Add(Vector3.Distance(before.Loops[loop][i], after.Loops[loop][i]));

        if (steps.Count == 0) return "nothing to compare";

        var moves = steps.ToArray();
        int still = moves.Count(m => m < 0.05f);
        return $"median {Median(moves):F2} mm, p95 {Percentile(moves, 0.95f):F2} mm, " +
               $"max {moves.Max():F2} mm, unmoved {(float)still / moves.Length:P1}";
    }

    /// <summary>
    /// Closest approach between two parts of the same loop that are not neighbours along it, how even
    /// the sample spacing is, and how far the loop sits off the body.
    ///
    /// <para>
    /// A true self-intersection is measure-zero and sampling will essentially never find one; a near
    /// touch is the measurable form of the same defect and is what wrecks a sweep. Spacing matters for
    /// the same reason - the trace resamples to an even arc length precisely so the sweep terminates -
    /// and the distance off the surface says whether the projection kept up with the moves.
    /// </para>
    /// </summary>
    private static string Integrity(PartingLine line, ISurfaceProjector? projector)
    {
        float clearance = float.PositiveInfinity;
        float spacingSpread = 0f;
        float offSurface = 0f;
        var turns = new List<float>();
        float perimeter = 0f;
        int samples = 0;

        foreach (var loop in line.Loops)
        {
            int n = loop.Count;
            if (n < 16) continue;

            const int Skip = 6;
            for (int i = 0; i < n; i++)
                for (int j = i + Skip; j < n; j++)
                {
                    if (n - (j - i) < Skip) continue;
                    clearance = MathF.Min(clearance, Vector3.Distance(loop[i], loop[j]));
                }

            var steps = new float[n];
            for (int i = 0; i < n; i++) steps[i] = Vector3.Distance(loop[i], loop[(i + 1) % n]);

            perimeter += steps.Sum();
            samples += n;
            float median = Median(steps);
            if (median > 1e-6f) spacingSpread = MathF.Max(spacingSpread, steps.Max() / median);

            // How sharply the line turns at each sample. This is what a contact sheet cannot show: a
            // sawtooth of a millimetre amplitude is a couple of pixels at tile scale and invisible,
            // while here it reads as a turn approaching 180 degrees at every second point.
            for (int i = 0; i < n; i++)
            {
                var incoming = loop[i] - loop[(i - 1 + n) % n];
                var outgoing = loop[(i + 1) % n] - loop[i];
                if (incoming.Length() < 1e-6f || outgoing.Length() < 1e-6f) continue;

                float turn = MathF.Acos(Math.Clamp(
                    Vector3.Dot(Vector3.Normalize(incoming), Vector3.Normalize(outgoing)), -1f, 1f))
                    * 180f / MathF.PI;

                turns.Add(turn);
            }

            if (projector is not null)
                foreach (var point in loop)
                    offSurface = MathF.Max(offSurface, Vector3.Distance(point, projector.Project(point)));
        }

        var bends = turns.ToArray();
        string kinks = bends.Length == 0
            ? "no turns"
            : $"turn median {Median(bends):F0}deg p95 {Percentile(bends, 0.95f):F0}deg " +
              $"max {bends.Max():F0}deg, over 60deg {(float)bends.Count(t => t > 60f) / bends.Length:P1}";

        return $"self-clearance {clearance:F2} mm, longest step {spacingSpread:F1} x median, " +
               $"perimeter {perimeter:F0} mm over {samples} pts (spacing {(samples > 0 ? perimeter / samples : 0f):F2} mm), " +
               $"off surface {offSurface:F3} mm, {kinks}";
    }

    /// <summary>
    /// How much the sampled surface normal swings from one point of the line to the next - the
    /// quantity the magenta overlay in the Parting Split scene is a picture of.
    /// </summary>
    private string Normals(IMesh body, PartingLine line)
    {
        var swings = new List<float>();

        foreach (var loop in line.Loops)
        {
            var sampled = _engine.PartingTools.SampleSurfaceNormals(body, loop);
            if (sampled.IsFailure) return $"unavailable - {sampled.Error.Description}";

            var normals = sampled.Value;
            for (int i = 0; i < normals.Count; i++)
            {
                var a = normals[i];
                var b = normals[(i + 1) % normals.Count];
                if (a.LengthSquared() < 1e-12f || b.LengthSquared() < 1e-12f) continue;

                swings.Add(MathF.Acos(Math.Clamp(
                    Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f)) * 180f / MathF.PI);
            }
        }

        if (swings.Count == 0) return "no normals";

        var swing = swings.ToArray();
        return $"swing median {Median(swing):F0}deg p95 {Percentile(swing, 0.95f):F0}deg " +
               $"max {swing.Max():F0}deg, over 90deg {(float)swing.Count(s => s > 90f) / swing.Length:P1}";
    }

    /// <summary>
    /// Where the line sits across the shaded band, measured against the band's own two edges.
    ///
    /// <para>
    /// The strip of band faces is bounded on either side by the surface it divides, so the faces that
    /// touch a non-band neighbour fall into two connected rings - one per side. Distance to each ring
    /// gives the same 0-to-1 reading as the contour bias, but taken against the region the scene
    /// actually paints.
    /// </para>
    /// </summary>
    private static string RegionBias(IMesh body, bool[] band, PartingLine line)
    {
        if (band.Length != body.Triangles.Length / 3) return "region not measured on this mesh";

        var vertices = body.Vertices;
        var triangles = body.Triangles;
        int faces = band.Length;

        var edges = new Dictionary<(int, int), List<int>>(faces * 2);
        for (int f = 0; f < faces; f++)
            for (int e = 0; e < 3; e++)
            {
                int a = triangles[(f * 3) + e];
                int b = triangles[(f * 3) + ((e + 1) % 3)];
                var key = a < b ? (a, b) : (b, a);
                if (!edges.TryGetValue(key, out var list)) edges[key] = list = new List<int>(2);
                list.Add(f);
            }

        var neighbours = new List<int>[faces];
        for (int f = 0; f < faces; f++) neighbours[f] = new List<int>(3);
        foreach (var shared in edges.Values)
            for (int i = 0; i < shared.Count; i++)
                for (int j = 0; j < shared.Count; j++)
                    if (i != j) neighbours[shared[i]].Add(shared[j]);

        var centroid = new Vector3[faces];
        for (int f = 0; f < faces; f++)
            centroid[f] = (vertices[triangles[f * 3]]
                + vertices[triangles[(f * 3) + 1]]
                + vertices[triangles[(f * 3) + 2]]) / 3f;

        // Band faces with a neighbour outside the band: the strip's two edges.
        var rim = new bool[faces];
        for (int f = 0; f < faces; f++)
            if (band[f] && neighbours[f].Any(n => !band[n])) rim[f] = true;

        // Split them into rings. A simple strip gives two; the largest two are the sides.
        var owner = new int[faces];
        Array.Fill(owner, -1);
        var sizes = new List<List<int>>();
        for (int seed = 0; seed < faces; seed++)
        {
            if (!rim[seed] || owner[seed] >= 0) continue;

            int id = sizes.Count;
            var members = new List<int>();
            var stack = new Stack<int>();
            owner[seed] = id;
            stack.Push(seed);

            while (stack.Count > 0)
            {
                int f = stack.Pop();
                members.Add(f);
                foreach (int n in neighbours[f])
                {
                    if (!rim[n] || owner[n] >= 0) continue;
                    owner[n] = id;
                    stack.Push(n);
                }
            }
            sizes.Add(members);
        }

        if (sizes.Count < 2) return $"only {sizes.Count} band edge(s) found - cannot read a middle";

        var ordered = sizes.OrderByDescending(m => m.Count).ToList();
        var first = ordered[0].Select(f => centroid[f]).ToArray();
        var second = ordered[1].Select(f => centroid[f]).ToArray();

        var bias = new List<float>();
        foreach (var loop in line.Loops)
            foreach (var point in loop)
            {
                float da = first.Min(c => Vector3.Distance(point, c));
                float db = second.Min(c => Vector3.Distance(point, c));
                if (da + db < 1e-4f) continue;
                bias.Add(da / (da + db));
            }

        if (bias.Count == 0) return "no samples";

        var values = bias.ToArray();
        int off = values.Count(t => t < 0.4f || t > 0.6f);
        return $"{sizes.Count} edge ring(s), n {values.Length}  " +
               $"mean {values.Average():F3} median {Median(values):F3} " +
               $"p5 {Percentile(values, 0.05f):F3} p95 {Percentile(values, 0.95f):F3}  " +
               $"outside 0.40-0.60 {(float)off / values.Length:P1}";
    }

    /// <summary>
    /// Faces outside the band whose every edge-neighbour is inside it, filled repeatedly until a pass
    /// changes nothing - the closing proposed for the shading, measured rather than applied.
    ///
    /// <para>
    /// Worth measuring first because the answer decides whether it is worth having. A handful of
    /// enclosed faces is a speckled picture and nothing more; a large number would mean the band mask
    /// is full of holes, which would say something about the fill pass that produced it rather than
    /// about the shading.
    /// </para>
    /// </summary>
    private static string EnclosedHoles(IMesh body, bool[] band)
    {
        var triangles = body.Triangles;
        var vertices = body.Vertices;
        int faces = triangles.Length / 3;
        if (band.Length != faces) return "band not measured on this mesh";

        var edges = new Dictionary<(int, int), List<int>>(faces * 2);
        for (int f = 0; f < faces; f++)
            for (int e = 0; e < 3; e++)
            {
                int a = triangles[(f * 3) + e];
                int b = triangles[(f * 3) + ((e + 1) % 3)];
                var key = a < b ? (a, b) : (b, a);
                if (!edges.TryGetValue(key, out var list)) edges[key] = list = new List<int>(2);
                list.Add(f);
            }

        var neighbours = new List<int>[faces];
        for (int f = 0; f < faces; f++) neighbours[f] = new List<int>(3);
        foreach (var shared in edges.Values)
            for (int i = 0; i < shared.Count; i++)
                for (int j = 0; j < shared.Count; j++)
                    if (i != j) neighbours[shared[i]].Add(shared[j]);

        var filled = (bool[])band.Clone();
        int total = 0, passes = 0;
        float area = 0f;

        while (true)
        {
            var adding = new List<int>();
            for (int f = 0; f < faces; f++)
            {
                if (filled[f] || neighbours[f].Count == 0) continue;
                if (neighbours[f].All(n => filled[n])) adding.Add(f);
            }

            if (adding.Count == 0) break;

            foreach (int f in adding)
            {
                filled[f] = true;
                var a = vertices[triangles[f * 3]];
                var b = vertices[triangles[(f * 3) + 1]];
                var c = vertices[triangles[(f * 3) + 2]];
                area += Vector3.Cross(b - a, c - a).Length() * 0.5f;
            }

            total += adding.Count;
            passes++;
        }

        int bandFaces = band.Count(b => b);
        return total == 0
            ? "none - no face outside the band is fully enclosed by it"
            : $"{total} face(s) over {passes} pass(es), {area:F1} mm^2, " +
              $"{(float)total / MathF.Max(bandFaces, 1):P2} of the band's face count";
    }

    /// <summary>
    /// Signed position of a point across the wall: 0 on the first crease, 1 on the second, negative
    /// beyond the first and above 1 beyond the second.
    ///
    /// <para>
    /// Taken by projecting onto the segment joining the two nearest crease points, which is the local
    /// across-band direction. Unlike a ratio of distances this does not fold at the creases, so it can
    /// say which side of one a point has strayed to and by how far.
    /// </para>
    /// </summary>
    private static float Across(Vector3 point, PartingBand band)
    {
        var first = PartingBand.Closest(point, band.First).Point;
        var second = PartingBand.Closest(point, band.Second).Point;

        var axis = second - first;
        float length = axis.LengthSquared();
        return length < 1e-9f ? 0.5f : Vector3.Dot(point - first, axis) / length;
    }

    private static string AcrossBand(IMesh body, bool[] band, PartingBand pair)
    {
        var vertices = body.Vertices;
        var triangles = body.Triangles;
        var values = new List<float>();

        for (int f = 0; f < band.Length; f++)
        {
            if (!band[f]) continue;
            var centre = (vertices[triangles[f * 3]]
                + vertices[triangles[(f * 3) + 1]]
                + vertices[triangles[(f * 3) + 2]]) / 3f;
            values.Add(Across(centre, pair));
        }

        if (values.Count == 0) return "no band faces";

        var t = values.ToArray();
        return $"n {t.Length}  p5 {Percentile(t, 0.05f):+0.00;-0.00}  median {Median(t):+0.00;-0.00}  " +
               $"p95 {Percentile(t, 0.95f):+0.00;-0.00}  " +
               $"midpoint of p5..p95 {(Percentile(t, 0.05f) + Percentile(t, 0.95f)) * 0.5f:+0.00;-0.00}";
    }

    private static string AcrossLine(PartingLine line, PartingBand pair)
    {
        var values = line.Loops.SelectMany(loop => loop).Select(p => Across(p, pair)).ToArray();
        if (values.Length == 0) return "no samples";

        return $"n {values.Length}  p5 {Percentile(values, 0.05f):+0.00;-0.00}  " +
               $"median {Median(values):+0.00;-0.00}  p95 {Percentile(values, 0.95f):+0.00;-0.00}";
    }

    private static PartingBand Nearest(Vector3 point, IReadOnlyList<PartingBand> bands)
    {
        var best = bands[0];
        float bestDistance = float.MaxValue;

        foreach (var band in bands)
        {
            float distance = MathF.Min(Closest(point, band.First), Closest(point, band.Second));
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = band;
        }

        return best;
    }

    private static float Closest(Vector3 from, RidgeContour contour)
    {
        var points = contour.Points;
        int spans = contour.IsClosed ? points.Count : points.Count - 1;

        float best = float.MaxValue;
        for (int i = 0; i < spans; i++)
        {
            var a = points[i];
            var ab = points[(i + 1) % points.Count] - a;
            float lengthSquared = ab.LengthSquared();
            float t = lengthSquared < 1e-12f
                ? 0f
                : Math.Clamp(Vector3.Dot(from - a, ab) / lengthSquared, 0f, 1f);
            best = MathF.Min(best, Vector3.Distance(from, a + (ab * t)));
        }
        return best;
    }

    // ---------------------------------------------------------------- picture

    /// <summary>
    /// Both lines over the ridge, on one sheet. Drawn together rather than as two sheets because the
    /// question is where one sits relative to the other, and that is not a thing two pictures can be
    /// flicked between to answer.
    /// </summary>
    private static void Draw(
        string directory, string model, IMesh body, IReadOnlyList<RidgeContour> contours,
        PartingLine before, PartingLine after)
    {
        Directory.CreateDirectory(directory);
        var options = RenderOptions.Default;

        // Four views to a sheet rather than eight. The tile has to be big enough to show a defect the
        // size of the thing being corrected - a millimetre on a hundred-millimetre body - and at eight
        // to a sheet each tile is small enough to hide exactly that, which is how a rectangular jog in
        // the line survived a whole round of inspection.
        Sheet(Path.Combine(directory, "sheet-views-a.png"), $"{model} — front, back, left, right",
            new[] { Views.Front, Views.Back, Views.Left, Views.Right });
        Sheet(Path.Combine(directory, "sheet-views-b.png"), $"{model} — top, bottom, orbit a, orbit b",
            new[] { Views.Top, Views.Bottom, Views.OrbitA, Views.OrbitB });

        void Sheet(string path, string title, IReadOnlyList<View> views)
        {
            var tiles = new List<Tile>();
            foreach (var view in views)
            {
                var camera = Camera.Fit(body, view, Panel, Panel);
                var image = MeshRasterizer.Render(body, camera, Panel, Panel, options);

                // Ridge first, so the lines being judged sit on top of it rather than under it.
                foreach (var contour in contours)
                    MeshRasterizer.DrawPolyline(
                        image, camera, contour.Points, contour.IsClosed, Contour, options);

                foreach (var loop in before.Loops)
                    MeshRasterizer.DrawPolyline(image, camera, loop, true, Before, options);
                foreach (var loop in after.Loops)
                    MeshRasterizer.DrawPolyline(image, camera, loop, true, After, options);

                tiles.Add(new Tile(view.Name, image));
            }

            ContactSheet.Save(path, tiles, 2, Panel, Background, title,
                new[] { ("ridge contour", Contour), ("traced", Before), ("centred", After) });
        }

        // The two lines on separate images as well as together. Drawing them over one another hides a
        // defect a second way: the eye reads the pair as one thick line and stops asking what shape
        // either of them is.
        foreach (var view in new[] { Views.Front, Views.OrbitA })
        {
            var camera = Camera.Fit(body, view, Large, Large);

            var traced = MeshRasterizer.Render(body, camera, Large, Large, options);
            foreach (var loop in before.Loops)
                MeshRasterizer.DrawPolyline(traced, camera, loop, true, Before, options);
            traced.Save(Path.Combine(directory, $"line-traced-{view.Name}.png"));

            var moved = MeshRasterizer.Render(body, camera, Large, Large, options);
            foreach (var loop in after.Loops)
                MeshRasterizer.DrawPolyline(moved, camera, loop, true, After, options);
            moved.Save(Path.Combine(directory, $"line-centred-{view.Name}.png"));
        }
    }

    /// <summary>
    /// What the Parting Split scene actually paints: the ridge region shaded, the crease contours over
    /// it, and the parting line in the scene's own yellow.
    ///
    /// <para>
    /// Reproduced rather than trusted to the numbers, because the two are answering different
    /// questions. The bias statistics measure the line against the crease contours; the scene shades
    /// <c>RidgeSurfaces.Faces</c>. Those are the same band only if the shaded faces are symmetric about
    /// the creases, which nothing has checked - so this puts the two on one image where a disagreement
    /// cannot hide.
    /// </para>
    /// </summary>
    private static void DrawScene(
        string directory, string model, IMesh body, bool[] band,
        IReadOnlyList<RidgeContour> contours, PartingLine traced, PartingLine line)
    {
        Directory.CreateDirectory(directory);
        var options = RenderOptions.Default;

        // The scene's own colours, so the picture and the screenshot are directly comparable.
        var region = new Rgb(94, 110, 171);
        var plain = new Rgb(190, 193, 200);
        var crease = new Rgb(198, 76, 255);
        var partingLine = new Rgb(255, 235, 60);

        Rgb FaceColour(int face) => band.Length == body.Triangles.Length / 3 && band[face] ? region : plain;

        // Hidden runs of every curve are dropped rather than dimmed. The rasterizer draws them at 30%
        // by default, which is right when the question is whether a rim closes all the way round - and
        // wrong for this one, because the far side of the loop then shows through the body and reads as
        // a second line beside the near one. Every picture in this investigation carried that ghost,
        // and it is why the line appeared to sit somewhere it does not.
        var solid = options with { DrawOccludedLines = false };

        Sheet("sheet-scene-a.png", $"{model} — front, back, left, right",
            new[] { Views.Front, Views.Back, Views.Left, Views.Right });
        Sheet("sheet-scene-b.png", $"{model} — top, bottom, orbit a, orbit b",
            new[] { Views.Top, Views.Bottom, Views.OrbitA, Views.OrbitB });

        void Sheet(string name, string title, IReadOnlyList<View> views)
        {
            var tiles = new List<Tile>();
            foreach (var view in views)
            {
                var camera = Camera.Fit(body, view, Panel, Panel);
                var image = MeshRasterizer.Render(body, camera, Panel, Panel, solid, FaceColour);

                foreach (var contour in contours)
                    MeshRasterizer.DrawPolyline(
                        image, camera, contour.Points, contour.IsClosed, crease, solid);

                foreach (var loop in traced.Loops)
                    MeshRasterizer.DrawPolyline(image, camera, loop, true, Before, solid);
                foreach (var loop in line.Loops)
                    MeshRasterizer.DrawPolyline(image, camera, loop, true, partingLine, solid);

                tiles.Add(new Tile(view.Name, image));
            }

            ContactSheet.Save(
                Path.Combine(directory, name), tiles, 2, Panel, Background, title,
                new[] { ("rim wall", region), ("crease", crease), ("traced", Before),
                        ("centred + smoothed", partingLine) });
        }
    }

    // ---------------------------------------------------------------- statistics

    private static float Perimeter(IReadOnlyList<Vector3> points)
    {
        float total = 0f;
        for (int i = 0; i < points.Count; i++)
            total += Vector3.Distance(points[i], points[(i + 1) % points.Count]);
        return total;
    }

    private static float Median(float[] values)
    {
        var sorted = (float[])values.Clone();
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }

    private static float Percentile(float[] values, float fraction)
    {
        var sorted = (float[])values.Clone();
        Array.Sort(sorted);
        return sorted[Math.Clamp((int)MathF.Round(fraction * (sorted.Length - 1)), 0, sorted.Length - 1)];
    }
}
