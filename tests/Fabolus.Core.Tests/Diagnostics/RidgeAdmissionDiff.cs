using System.Numerics;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// Diffs the edges two runs of the detector admit, and says why the stricter run refused each edge the
/// looser one kept.
///
/// <para>
/// This exists because lowering the grow pair repairs <c>standard</c>'s band completely while wrecking
/// other bodies, so the relaxation cannot be adopted - but whatever it admits is the repair, and no
/// summary number says what that is. An edge can be missing from a run for two quite different
/// reasons: it never cleared the grow level at all, or it cleared it and was thrown out with a
/// connected run that had no seed or came up short against <see
/// cref="RidgeDetectionOptions.MinLengthFraction"/>. The first wants a different grow test; the second
/// wants a different run test, and no amount of adjusting the first would ever reach it.
/// </para>
///
/// <para>
/// Prints rather than asserts, for the same reason <see cref="RidgeThresholdSensitivity"/> does: the
/// numbers are the finding.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class RidgeAdmissionDiff
{
    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;
    private readonly ITestOutputHelper _log;

    public RidgeAdmissionDiff(GeometryEngineFixture assets, ITestOutputHelper log)
    {
        _assets = assets;
        _engine = assets.Engine;
        _log = log;
    }

    [Theory]
    [InlineData("standard", "3mf/test bolus standard.3mf")]
    public void WhatTheRelaxedRunAdmits(string id, string asset)
    {
        var imported = _engine.IO.Import(_assets.GetAssetPath(asset));
        var mould = MouldMesh.Create(imported.Value);
        var body = new PartingMeshFeature(_engine).GetBodyMesh(mould.Value).Value.Mesh;

        var strictOptions = RidgeDetectionOptions.Default;
        var looseOptions = strictOptions with { GrowCurvature = 0.10f, GrowAngleDegrees = 15f };

        var strict = RidgeDetection.Diagnose(body, strictOptions, traceEdges: true);
        var loose = RidgeDetection.Diagnose(body, looseOptions, traceEdges: true);

        _log.WriteLine($"=== {id} ===");
        Describe("strict 0.20/25", strict);
        Describe("loose  0.10/15", loose);

        var strictByEdge = strict.Edges.ToDictionary(e => e.Key);
        var looseByEdge = loose.Edges.ToDictionary(e => e.Key);

        // A run only traces the edges it measured a fold across, so an edge bridging invented in one
        // run may be absent from the other's trace entirely. Absent means the run never had it, which
        // is what a missing lookup should read as rather than a crash.
        RidgeEdgeAdmission In(Dictionary<(int, int), RidgeEdgeAdmission> trace, RidgeEdgeAdmission edge) =>
            trace.TryGetValue(edge.Key, out var found)
                ? found
                : edge with { Curvature = float.NaN, AngleDegrees = float.NaN,
                    Candidate = false, Seed = false, Verdict = null, Final = false };

        var added = loose.Edges.Where(e => e.Final && !In(strictByEdge, e).Final).ToList();
        var removed = strict.Edges.Where(e => e.Final && !In(looseByEdge, e).Final).ToList();

        _log.WriteLine("");
        _log.WriteLine($"traced edges  strict {strict.Edges.Count}  loose {loose.Edges.Count}");
        _log.WriteLine($"final edges   strict {strict.Edges.Count(e => e.Final)}  " +
                       $"loose {loose.Edges.Count(e => e.Final)}");
        _log.WriteLine($"added {added.Count}   removed {removed.Count}");

        // ---- why the strict run refused each added edge ----
        _log.WriteLine("");
        _log.WriteLine("-- added edges by the strict run's reason for refusing them --");
        foreach (var group in added.GroupBy(e => Reason(In(strictByEdge, e))).OrderByDescending(g => g.Count()))
        {
            var strictSide = group.Select(e => In(strictByEdge, e)).ToList();
            _log.WriteLine(
                $"{group.Key,-28} {group.Count(),6}   " +
                $"curvature {Range(strictSide.Select(e => e.Curvature))}  " +
                $"angle {Range(strictSide.Select(e => e.AngleDegrees))}");
        }

        // Relaxing a threshold can only widen the candidate set and can only grow the run each candidate
        // lands in, so no edge the strict pass kept can fail either test in the loose one. Anything the
        // loose run drops therefore has to be a bridge it no longer needed or routed elsewhere - worth
        // confirming rather than assuming, because a removal that was not one would mean the two runs
        // are not the comparison this test believes they are.
        _log.WriteLine("");
        _log.WriteLine("-- removed edges, by what the loose run made of them --");
        foreach (var group in removed.GroupBy(e => Reason(In(looseByEdge, e))).OrderByDescending(g => g.Count()))
            _log.WriteLine($"{group.Key,-28} {group.Count(),6}   " +
                           $"strict side: candidate {group.Count(e => e.Candidate)}, " +
                           $"kept by threshold {group.Count(e => e.Verdict == RidgeRunVerdict.Kept)}");

        // ---- where they are, relative to the pinch the strict run leaves ----
        var suspect = strict.BandProfile.PerFaceSuspect;
        if (!strict.BandProfile.Available || !suspect.Any(s => s))
        {
            _log.WriteLine("");
            _log.WriteLine("no suspect faces in the strict run - nothing to localise against");
            return;
        }

        var zone = strict.Edges
            .Where(e => Touches(e, suspect))
            .Select(e => e.Mid)
            .ToArray();

        _log.WriteLine("");
        _log.WriteLine($"pinch zone: {suspect.Count(s => s)} suspect faces, {zone.Length} edges, " +
                       $"{strict.BandProfile.SuspectArea:F1}mm2 of {strict.BandProfile.BandArea:F0}mm2 band");

        // Every edge's distance to the zone, once. The sections below each want it for overlapping
        // subsets, and the zone is large enough that recomputing per query is minutes rather than
        // seconds.
        var distanceOf = new Dictionary<(int, int), float>(strict.Edges.Count);
        foreach (var edge in strict.Edges) distanceOf[edge.Key] = NearestDistance(edge.Mid, zone);
        foreach (var edge in added)
            if (!distanceOf.ContainsKey(edge.Key)) distanceOf[edge.Key] = NearestDistance(edge.Mid, zone);

        var distance = added.Select(e => distanceOf[e.Key]).ToArray();

        _log.WriteLine("");
        _log.WriteLine("-- added edges by distance from the pinch, and by reason --");
        var buckets = new (string Name, float Limit)[]
        {
            ("in the pinch", 0.001f), ("< 5mm", 5f), ("< 10mm", 10f), ("< 20mm", 20f),
            ("< 50mm", 50f), ("further", float.MaxValue),
        };

        float low = 0f;
        foreach (var (name, limit) in buckets)
        {
            var inBucket = Enumerable.Range(0, added.Count)
                .Where(i => distance[i] >= low && distance[i] < limit)
                .Select(i => In(strictByEdge, added[i]))
                .ToList();
            low = limit;
            if (inBucket.Count == 0) continue;

            string breakdown = string.Join(", ", inBucket
                .GroupBy(Reason)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key} {g.Count()}"));
            _log.WriteLine($"{name,-14} {inBucket.Count,6}   {breakdown}");
        }

        // ---- the edges in the pinch itself, one line each ----
        var local = Enumerable.Range(0, added.Count)
            .Where(i => distance[i] < 0.001f)
            .Select(i => In(strictByEdge, added[i]))
            .OrderBy(e => e.Curvature)
            .ToList();

        _log.WriteLine("");
        _log.WriteLine($"-- the {local.Count} added edges inside the pinch (strict run's numbers) --");
        foreach (var edge in local.Take(60))
            _log.WriteLine(
                $"  ({edge.A},{edge.B})  curv {edge.Curvature,7:F3}  angle {edge.AngleDegrees,7:F2}  " +
                $"candidate {edge.Candidate,-5}  seed {edge.Seed,-5}  " +
                $"verdict {edge.Verdict?.ToString() ?? "-",-9}  " +
                $"run {edge.RunEdges,5} edges {edge.RunLength,8:F1}mm");

        // ---- the runs the strict pass threw away that reach into the pinch ----
        // Grouped by the run's own shape rather than by an id, which the report does not carry. Two
        // distinct runs agreeing to the last bit on both length and edge count would merge; on a real
        // body that does not happen.
        var rejected = strict.Edges
            .Where(e => e.Candidate && e.Verdict is RidgeRunVerdict.NoSeed or RidgeRunVerdict.TooShort)
            .GroupBy(e => (e.Verdict, e.RunEdges, e.RunLength))
            .Where(g => g.Any(e => distanceOf[e.Key] < 20f))
            .OrderByDescending(g => g.Key.RunLength)
            .ToList();

        _log.WriteLine("");
        _log.WriteLine($"-- strict runs refused within 20mm of the pinch ({rejected.Count}) --");
        _log.WriteLine($"   (the length test wanted {strict.Report.Threshold.MinRunLength:F1}mm)");
        foreach (var run in rejected.Take(25))
            _log.WriteLine(
                $"  {run.Key.Verdict,-9} {run.Key.RunEdges,6} edges  {run.Key.RunLength,8:F1}mm  " +
                $"{run.Count(e => distanceOf[e.Key] < 0.001f),5} of them in the pinch  " +
                $"nearest {run.Min(e => distanceOf[e.Key]),6:F1}mm");

        // ---- how far the chains through the pinch reach ----
        //
        // A crease admitted in isolation changes nothing downstream: the fill walks round the ends of
        // an open arc, so the region structure - and with it the contour - is exactly what it was. What
        // an added edge is worth therefore depends on the whole chain it belongs to and where that
        // chain rejoins the ridge the strict run already had. That is the length any local second pass
        // would have had to cover, and it is the one number a radius-limited zone can be wrong about.
        var attachment = new HashSet<int>();
        foreach (var edge in strict.Edges)
            if (edge.Final)
            {
                attachment.Add(edge.A);
                attachment.Add(edge.B);
            }

        // How much of what the relaxation admits is new ground at all. An edge with both endpoints
        // already on the strict ridge is a chord across a network that is there, and admitting it can
        // only close a small loop; an edge with neither is the ridge reaching somewhere it was not.
        _log.WriteLine("");
        _log.WriteLine("-- added edges by how much of them was already on the strict ridge --");
        _log.WriteLine("  within      both ends   one end   neither");
        foreach (float radius in new[] { 0.001f, 5f, 10f, 20f, 50f, float.MaxValue })
        {
            var within = added.Where(e => distanceOf[e.Key] < radius).ToList();
            if (within.Count == 0) continue;

            int both = within.Count(e => attachment.Contains(e.A) && attachment.Contains(e.B));
            int neither = within.Count(e => !attachment.Contains(e.A) && !attachment.Contains(e.B));
            _log.WriteLine(
                $"  {(radius > 1e6f ? "all" : radius < 0.01f ? "pinch" : $"{radius:F0}mm"),-10} " +
                $"{both,9} {within.Count - both - neither,9} {neither,9}   of {within.Count}");
        }

        _log.WriteLine("");
        _log.WriteLine("-- connected chains of added edges, the ones reaching the pinch first --");
        _log.WriteLine("   edges  in pinch   nearest    span   extent  attach  reason breakdown");

        foreach (var chain in Components(added)
            .OrderByDescending(c => c.Count(e => distanceOf[e.Key] < 0.001f))
            .ThenByDescending(c => c.Count)
            .Take(12))
        {
            var mids = chain.Select(e => e.Mid).ToArray();
            var min = mids.Aggregate(Vector3.Min);
            var max = mids.Aggregate(Vector3.Max);
            var reach = chain.Select(e => distanceOf[e.Key]).ToArray();

            var vertices = new HashSet<int>(chain.SelectMany(e => new[] { e.A, e.B }));
            string breakdown = string.Join(", ", chain
                .GroupBy(e => Reason(In(strictByEdge, e)))
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key} {g.Count()}"));

            _log.WriteLine(
                $"  {chain.Count,6} {reach.Count(r => r < 0.001f),9} {reach.Min(),9:F1} " +
                $"{reach.Max(),7:F1} {(max - min).Length(),8:F1} " +
                $"{vertices.Count(attachment.Contains),7}  {breakdown}");
        }
    }

    /// <summary>Splits edges into groups connected through shared endpoints.</summary>
    private static List<List<RidgeEdgeAdmission>> Components(IReadOnlyList<RidgeEdgeAdmission> edges)
    {
        var incident = new Dictionary<int, List<int>>(edges.Count * 2);
        for (int i = 0; i < edges.Count; i++)
        {
            Attach(incident, edges[i].A, i);
            Attach(incident, edges[i].B, i);
        }

        var seen = new bool[edges.Count];
        var components = new List<List<RidgeEdgeAdmission>>();
        var stack = new Stack<int>();

        for (int i = 0; i < edges.Count; i++)
        {
            if (seen[i]) continue;

            var component = new List<RidgeEdgeAdmission>();
            seen[i] = true;
            stack.Push(i);

            while (stack.Count > 0)
            {
                int current = stack.Pop();
                component.Add(edges[current]);

                foreach (int endpoint in new[] { edges[current].A, edges[current].B })
                    foreach (int next in incident[endpoint])
                        if (!seen[next])
                        {
                            seen[next] = true;
                            stack.Push(next);
                        }
            }

            components.Add(component);
        }

        return components;

        static void Attach(Dictionary<int, List<int>> map, int key, int value)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<int>(4);
            list.Add(value);
        }
    }

    private void Describe(string name, RidgeDiagnosis d)
    {
        var profile = d.BandProfile;
        _log.WriteLine(
            $"{name}: {d.Contours.Count} contours ({d.Contours.Count(c => c.IsClosed)} closed)  " +
            $"band median {profile.MedianWidth:F2}mm  min {profile.Width.Min:F2}mm  " +
            $"suspect {profile.SuspectFaces} faces / {profile.SuspectAreaFraction:P1}  " +
            $"kept edges {d.Report.Threshold.KeptEdges}" +
            (d.Report.Threshold.PercolationGuardFired ? "  GUARD FIRED" : ""));
    }

    private static string Reason(RidgeEdgeAdmission strict) =>
        strict.Final ? "kept by both (unexpected)"
        : !strict.Candidate && float.IsNaN(strict.Curvature) ? "no fold (bridge only)"
        : !strict.Candidate ? "below grow"
        : strict.Verdict switch
        {
            RidgeRunVerdict.NoSeed => "grew, run had no seed",
            RidgeRunVerdict.TooShort => "grew, run too short",
            RidgeRunVerdict.Kept => "grew and kept, lost to guard",
            _ => "grew, no run recorded",
        };

    private static bool Touches(RidgeEdgeAdmission edge, bool[] suspect) =>
        (edge.FaceA >= 0 && suspect[edge.FaceA]) || (edge.FaceB >= 0 && suspect[edge.FaceB]);

    private static float NearestDistance(Vector3 point, Vector3[] zone)
    {
        float best = float.MaxValue;
        foreach (var other in zone) best = MathF.Min(best, Vector3.DistanceSquared(point, other));
        return MathF.Sqrt(best);
    }

    private static string Range(IEnumerable<float> values)
    {
        var sorted = values.Where(v => !float.IsNaN(v)).OrderBy(v => v).ToArray();
        return sorted.Length == 0
            ? "-"
            : $"{sorted[0],7:F3} .. {sorted[sorted.Length / 2],7:F3} .. {sorted[^1],7:F3}";
    }
}
