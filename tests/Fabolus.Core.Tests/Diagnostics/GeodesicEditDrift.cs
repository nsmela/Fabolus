using System.Numerics;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Core.Tests.Diagnostics;

/// <summary>
/// Runs the sequence a user reports as losing the line: put the traced line into editable form, add a
/// few handles, then switch the retrace to the unconstrained geodesic - and measure what the line the
/// parting mesh is built from actually becomes.
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class GeodesicEditDrift
{
    private readonly IGeometryEngine _engine;
    private readonly PartingMeshFeature _sut;
    private readonly ITestOutputHelper _out;

    public GeodesicEditDrift(GeometryEngineFixture fixture, ITestOutputHelper output)
    {
        _engine = fixture.Engine;
        _sut = new PartingMeshFeature(_engine);
        _out = output;
    }

    [Theory]
    [InlineData("chin.3mf")]
    [InlineData("scalp.3mf")]
    [InlineData("nose.3mf")]
    [InlineData("larynx_large.3mf")]
    public void ReportWhatTheGeodesicRetraceDoesToTheLine(string file)
    {
        var path = Path.Combine(Assets(), "3mf", file);
        if (!File.Exists(path)) { _out.WriteLine($"{file}: absent"); return; }

        var imported = _engine.IO.Import(path);
        var mould = MouldMesh.Create(imported.Value);
        var body = BodyMesh.Create(_engine, mould.Value).Value;

        var traced = _sut.GeneratePartingLineFromThickness(body);
        if (traced.IsFailure) { _out.WriteLine($"{file}: trace failed"); return; }

        var edit = _sut.BeginPartingLineEdit(body, traced.Value);
        if (edit.IsFailure) { _out.WriteLine($"{file}: edit refused"); return; }

        var seeded = edit.Value;
        _out.WriteLine($"--- {file}");
        Report("seeded", seeded, traced.Value);

        // Add handles the way a user does: divide the middle of the first few sections.
        var withHandles = seeded;
        for (int rim = 0; rim < withHandles.Rims.Count; rim++)
        {
            var line = withHandles.Rims[rim].Line;
            for (int s = 0; s < Math.Min(3, line.Spans.Count); s++)
            {
                int at = s * 2;                       // spans grow as we insert, so step past the new one
                if (at >= line.Spans.Count) break;

                var span = line.Spans[at];
                if (span.Points.Count < 5) continue;

                var (next, anchor) = PartingLineEditor.Insert(
                    line,
                    new PartingInsertion(rim, at, span.Points.Count / 2, span.Points[span.Points.Count / 2]));

                if (anchor >= 0) line = next;
            }

            withHandles = withHandles.With(rim, line);
        }

        Report("handles added", withHandles, traced.Value);

        // Now the toggle: every span re-walked as an unconstrained geodesic.
        using var geodesic = _engine.PartingTools.CreateSurfaceGeodesic(body.Mesh).Value;

        var freed = withHandles;
        for (int rim = 0; rim < freed.Rims.Count; rim++)
            freed = freed.With(
                rim, PartingLineEditor.Retrace(freed.Rims[rim].Line, freed.Rims[rim].Graph, geodesic));

        Report("geodesic mode", freed, traced.Value);

        // The question the user is actually asking: does the flange follow the edited line, or the one
        // it started as? Measured as how near the flange's own vertices come to each line.
        Flange("seeded    ", seeded.ToPartingLine(), traced.Value, mould.Value, body);
        Flange("geodesic  ", freed.ToPartingLine(), traced.Value, mould.Value, body);

        // Aggregate length hides the shape, so look at each span: an unconstrained path between two
        // handles well apart round the rim takes the short way, straight over a shell, and that is a
        // span that has left the wall entirely rather than one that merely wanders.
        Spans("seeded  ", seeded);
        Spans("geodesic", freed);

        // The flange is swept ring by ring along the line's own points, so how evenly those are spaced
        // is not cosmetic - a run of near-coincident samples puts consecutive rings on top of each
        // other, and the sweep crosses itself there.
        Spacing("seeded  ", seeded.ToPartingLine());
        Spacing("geodesic", freed.ToPartingLine());
    }

    private void Spacing(string stage, PartingLine line)
    {
        for (int i = 0; i < line.Loops.Count; i++)
        {
            var loop = line.Loops[i];
            var steps = new List<float>(loop.Count);
            for (int k = 0; k < loop.Count; k++)
                steps.Add(Vector3.Distance(loop[k], loop[(k + 1) % loop.Count]));

            steps.Sort();
            int tiny = steps.Count(s => s < 0.05f);
            float mean = steps.Average();

            _out.WriteLine(
                $"  {stage} loop {i} spacing: min={steps[0],7:F4}  median={steps[steps.Count / 2],6:F3}" +
                $"  mean={mean,6:F3}  max={steps[^1],6:F3}mm   under 0.05mm={tiny,4}  ratio max/median=" +
                $"{(steps[steps.Count / 2] > 0 ? steps[^1] / steps[steps.Count / 2] : 0),6:F1}");
        }
    }

    private void Spans(string stage, PartingLineEdit edit)
    {
        for (int rim = 0; rim < edit.Rims.Count; rim++)
        {
            var line = edit.Rims[rim].Line;
            var band = edit.Rims[rim].Graph.Band;
            float wall = band.Span;

            int strayed = 0;
            float worstSpan = 0f;

            foreach (var span in line.Spans)
            {
                float worst = 0f;
                foreach (var p in span.Points)
                    worst = MathF.Max(worst, MathF.Min(
                        PartingBand.Closest(p, band.First).Distance,
                        PartingBand.Closest(p, band.Second).Distance));

                if (worst > wall) strayed++;
                worstSpan = MathF.Max(worstSpan, worst);
            }

            _out.WriteLine(
                $"  {stage} rim {rim}: wall={wall,5:F2}mm  spans={line.Spans.Count,3}  " +
                $"off the wall={strayed,3}  furthest={worstSpan,6:F2}mm ({worstSpan / wall,4:F1}x wall)");
        }
    }

    private void Flange(
        string stage, PartingLine used, PartingLine tracedLine, MouldMesh mould, BodyMesh body)
    {
        var resolved = PartingMeshFeature.ResolveAxis(used, PartingMeshParameters.Default);
        if (resolved.IsFailure) { _out.WriteLine($"  {stage} axis failed"); return; }

        var contour = _sut.GenerateOuterContour(mould, resolved.Value);
        if (contour.IsFailure) { _out.WriteLine($"  {stage} contour failed"); return; }

        var flange = _sut.GenerateFlangeSurface(used, contour.Value, resolved.Value, body);
        if (flange.IsFailure) { _out.WriteLine($"  {stage} flange failed - {flange.Error.Description}"); return; }

        // Mean distance from the flange's inner rim to each candidate line. The flange is built from
        // one of them, so whichever it sits on is the one it was built from.
        var topology = _engine.Evaluators.ValidateTopology(flange.Value);
        string health = topology.IsSuccess
            ? $"selfInt={topology.Value.SelfIntersectionCount,5}  degenerate={topology.Value.HasDegenerateTriangles}"
            : "topology unavailable";

        _out.WriteLine(
            $"  {stage} flange tris={flange.Value.Triangles.Length / 3,6}  " +
            $"mean gap to USED line={MeanGap(flange.Value, used),6:F3}mm  " +
            $"to TRACED line={MeanGap(flange.Value, tracedLine),6:F3}mm  {health}");
    }

    /// <summary>How near the flange's nearest tenth of vertices come to a line, averaged.</summary>
    private static float MeanGap(IMesh flange, PartingLine line)
    {
        var points = new List<Vector3>();
        foreach (var loop in line.Loops) points.AddRange(loop);
        if (points.Count == 0) return float.NaN;

        var distances = new List<float>(flange.Vertices.Length);
        foreach (var v in flange.Vertices)
        {
            float best = float.MaxValue;
            foreach (var p in points) best = MathF.Min(best, Vector3.DistanceSquared(v, p));
            distances.Add(MathF.Sqrt(best));
        }

        distances.Sort();
        int take = Math.Max(1, distances.Count / 10);
        float total = 0f;
        for (int i = 0; i < take; i++) total += distances[i];
        return total / take;
    }

    private void Report(string stage, PartingLineEdit edit, PartingLine traced)
    {
        var line = edit.ToPartingLine();

        for (int i = 0; i < line.Loops.Count; i++)
        {
            var loop = line.Loops[i];
            float length = Perimeter(loop);
            float original = i < traced.Loops.Count ? Perimeter(traced.Loops[i]) : 0f;

            // How far the line sits from the wall it is supposed to run in.
            var band = edit.Rims[Math.Min(i, edit.Rims.Count - 1)].Graph.Band;
            float worst = 0f;
            foreach (var p in loop)
            {
                float d = MathF.Min(
                    PartingBand.Closest(p, band.First).Distance,
                    PartingBand.Closest(p, band.Second).Distance);
                worst = MathF.Max(worst, d);
            }

            var axis = PartingMeshFeature.ResolveAxis(
                new PartingLine(new[] { loop }), PartingMeshParameters.Default);

            _out.WriteLine(
                $"  {stage,-14} loop {i}: pts={loop.Count,5}  len={length,8:F1}mm " +
                $"(traced {original,8:F1}mm, x{(original > 0 ? length / original : 0),5:F2})  " +
                $"furthest from wall={worst,6:F2}mm  " +
                $"axis={(axis.IsSuccess ? Fmt(axis.Value.Axis) : "FAILED")}");
        }
    }

    private static string Fmt(Vector3 v) => $"({v.X,6:F3},{v.Y,6:F3},{v.Z,6:F3})";

    private static float Perimeter(IReadOnlyList<Vector3> loop)
    {
        float total = 0f;
        for (int i = 0; i < loop.Count; i++)
            total += Vector3.Distance(loop[i], loop[(i + 1) % loop.Count]);
        return total;
    }

    private static string Assets()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "files")))
            dir = dir.Parent;

        return dir is null ? "" : Path.Combine(dir.FullName, "tests", "files");
    }
}
