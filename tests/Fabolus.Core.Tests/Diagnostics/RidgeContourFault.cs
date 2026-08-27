using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// Whether the band's shortfall test is safe enough to build a repair on, and if so which of the two
/// boundaries it says has moved.
///
/// <para>
/// A repair that moves a contour has to be gated on something that never fires when the contour is
/// right, because the cost of a false positive is dragging a correct rim off the crease it was
/// tracing. The shortfall test is the candidate: it is known to fire exactly at the pocket on
/// <c>standard</c> and to stay silent on the five simple bodies. What it has never been shown against
/// is the two larynxes, and those are the hard cases - their bands genuinely halve in width, which is
/// the very shape the test looks for. If it cannot tell a taper from a collapse there, it is not a
/// gate and nothing should be built on it.
/// </para>
///
/// <para>
/// Attribution comes free once it holds. The width at a face is its distance to one surface plus its
/// distance to the other, and a band pinched from one side has one of those at its usual value and the
/// other at nearly nothing - so comparing each against its own local median names the boundary that
/// rode in, without any appeal to which contour is which.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class RidgeContourFault
{
    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;
    private readonly ITestOutputHelper _log;

    public RidgeContourFault(GeometryEngineFixture assets, ITestOutputHelper log)
    {
        _assets = assets;
        _engine = assets.Engine;
        _log = log;
    }

    [Fact]
    public void IsTheShortfallTestAGate()
    {
        var models = new (string Id, string Asset)[]
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

        _log.WriteLine("  model          band faces   suspect   suspect %   median mm   worst mm   " +
                       "side A   side B   both   deficit p50/p90 mm");
        var ratios = new List<string>();

        foreach (var (id, asset) in models)
        {
            var imported = _engine.IO.Import(_assets.GetAssetPath(asset));
            var mould = MouldMesh.Create(imported.Value);
            var body = new PartingMeshFeature(_engine).GetBodyMesh(mould.Value).Value.Mesh;

            var run = RidgeDetection.Diagnose(body, RidgeDetectionOptions.Default, traceEdges: true);
            var profile = run.BandProfile;
            if (!profile.Available)
            {
                _log.WriteLine($"  {id,-13}  no band profile");
                continue;
            }

            int faceCount = run.RidgeFaces.Length;
            var mesh = RidgeReplay.Merge(faceCount, run);

            // Each side's own local expectation, gathered the same way the width's is: the median over
            // the band within a few band widths. Comparing a side against its own median is what lets a
            // taper - where both sides shrink together - read differently from a collapse.
            var expectedFirst = LocalMedian(mesh, profile.PerFaceToFirst, profile.PerFaceWidth,
                profile.MedianWidth * 4f);
            var expectedSecond = LocalMedian(mesh, profile.PerFaceToSecond, profile.PerFaceWidth,
                profile.MedianWidth * 4f);

            int sideA = 0, sideB = 0, both = 0;
            var deficits = new List<float>();

            for (int f = 0; f < faceCount; f++)
            {
                if (!profile.PerFaceSuspect[f]) continue;

                float shortA = expectedFirst[f] - profile.PerFaceToFirst[f];
                float shortB = expectedSecond[f] - profile.PerFaceToSecond[f];
                deficits.Add(profile.PerFaceExpected[f] - profile.PerFaceWidth[f]);

                // "Both" means the band thinned from each side at once, which is what a taper looks
                // like and what a collapse does not.
                bool a = shortA > 0.25f * expectedFirst[f];
                bool b = shortB > 0.25f * expectedSecond[f];
                if (a && b) both++;
                else if (a) sideA++;
                else if (b) sideB++;
            }

            deficits.Sort();
            var widths = Enumerable.Range(0, faceCount)
                .Where(f => !float.IsPositiveInfinity(profile.PerFaceWidth[f]))
                .Select(f => profile.PerFaceWidth[f]).ToList();

            _log.WriteLine(
                $"  {id,-13} {profile.BandFaces,11} {profile.SuspectFaces,9} " +
                $"{profile.SuspectAreaFraction,10:P1} {profile.MedianWidth,11:F2} " +
                $"{(widths.Count == 0 ? 0f : widths.Min()),10:F2} " +
                $"{sideA,8} {sideB,8} {both,6}   " +
                $"{(deficits.Count == 0 ? "-" : $"{deficits[deficits.Count / 2]:F1} / {deficits[(int)(deficits.Count * 0.9f)]:F1}")}");

            ratios.Add(AgainstThickness(id, body, run, mesh, profile));
        }

        // The discriminator the shortfall test lacks. A band that narrows because the shell tapers
        // narrows in step with the wall, so width over thickness stays near one; a band that narrows
        // because a boundary rode up over wall that is still there does not.
        _log.WriteLine("");
        _log.WriteLine("-- band width against the wall the band is measuring, p10/p50/p90 --");
        _log.WriteLine("  model          suspect faces          whole band            how the suspect faces cluster");
        foreach (string row in ratios) _log.WriteLine(row);
    }

    private string AgainstThickness(
        string id, IMesh body, RidgeDiagnosis run, RidgeTopology mesh, RidgeBandProfileReport profile)
    {
        var measured = _engine.Evaluators.MeasureWallThickness(body, WallThicknessOptions.Default);
        if (measured.IsFailure) return $"  {id,-13}  thickness unavailable: {measured.Error.Description}";

        var thickness = measured.Value;
        var suspect = new List<float>();
        var whole = new List<float>();

        for (int f = 0; f < mesh.FaceCount; f++)
        {
            if (float.IsPositiveInfinity(profile.PerFaceWidth[f])) continue;

            float wall = LocalThickness(mesh, thickness, f);
            if (float.IsPositiveInfinity(wall) || wall < 1e-3f) continue;

            float ratio = profile.PerFaceWidth[f] / wall;
            whole.Add(ratio);
            if (profile.PerFaceSuspect[f]) suspect.Add(ratio);
        }

        suspect.Sort();
        whole.Sort();

        // The clusters the suspect faces fall into. A boundary that has ridden up over a stretch of
        // wall marks a patch; a jagged rim whose sawtooth teeth each measure narrow at the tip marks
        // specks. Both come out as the same share of the band, so the count is the thing that sees the
        // difference and the fraction never can.
        var clusters = Clusters(mesh, profile.PerFaceSuspect);
        clusters.Sort((a, b) => b.CompareTo(a));

        string shape = clusters.Count == 0
            ? "-"
            : $"{clusters.Count,4} clusters, largest {clusters[0],4}, " +
              $"median {clusters[clusters.Count / 2],3}, " +
              $"{clusters.Count(c => c >= 10),3} of 10+ faces";

        return $"  {id,-13}  {Spread(suspect),-20}  {Spread(whole),-20}  {shape}";

        static string Spread(List<float> values) => values.Count == 0
            ? "-"
            : $"{values[(int)(values.Count * 0.1f)]:F2} / {values[values.Count / 2]:F2} / " +
              $"{values[(int)(values.Count * 0.9f)]:F2}";
    }

    /// <summary>Sizes of the connected groups a face mask falls into.</summary>
    private static List<int> Clusters(RidgeTopology mesh, bool[] mask)
    {
        var seen = new bool[mesh.FaceCount];
        var sizes = new List<int>();
        var stack = new Stack<int>();

        for (int f = 0; f < mesh.FaceCount; f++)
        {
            if (seen[f] || !mask[f]) continue;

            int size = 0;
            seen[f] = true;
            stack.Push(f);

            while (stack.Count > 0)
            {
                int face = stack.Pop();
                size++;

                foreach (int e in mesh.FaceEdges[face])
                {
                    int across = mesh.Across(e, face);
                    if (across < 0 || seen[across] || !mask[across]) continue;

                    seen[across] = true;
                    stack.Push(across);
                }
            }

            sizes.Add(size);
        }

        return sizes;
    }

    /// <summary>
    /// The wall near a face, from the nearest face out to a few steps that could actually be measured.
    /// A face on the crease looks along the shell rather than across it, so its own probe never exits;
    /// the faces a step or two away are on the surface either side and those read the wall.
    /// </summary>
    private static float LocalThickness(
        RidgeTopology mesh, WallThickness thickness, int start, int maxSteps = 4)
    {
        var seen = new HashSet<int> { start };
        var frontier = new List<int> { start };
        var next = new List<int>();

        for (int step = 0; step <= maxSteps; step++)
        {
            float best = float.PositiveInfinity;
            foreach (int face in frontier)
            {
                float t = thickness.PerFace[face];
                if (float.IsPositiveInfinity(t) || thickness.PartnerFace[face] < 0) continue;
                best = MathF.Min(best, t);
            }
            if (!float.IsPositiveInfinity(best)) return best;

            next.Clear();
            foreach (int face in frontier)
                foreach (int e in mesh.FaceEdges[face])
                {
                    int across = mesh.Across(e, face);
                    if (across >= 0 && seen.Add(across)) next.Add(across);
                }

            if (next.Count == 0) break;
            (frontier, next) = (next, frontier);
        }

        return float.PositiveInfinity;
    }

    /// <summary>
    /// The median of <paramref name="value"/> over the band within <paramref name="radius"/> of each
    /// band face, walked across the band itself rather than through space so the window follows the rim
    /// instead of jumping the gap to the other side of the wall.
    /// </summary>
    private static float[] LocalMedian(
        RidgeTopology mesh, float[] value, float[] width, float radius)
    {
        int faceCount = mesh.FaceCount;
        var median = new float[faceCount];
        Array.Fill(median, float.PositiveInfinity);

        // Mean edge length, for turning the walk's ring count into a distance the same way MeasureBand
        // does.
        float step = mesh.Edges.Average(e => e.Length);

        var nearby = new List<float>();
        var visited = new HashSet<int>();
        var frontier = new List<int>();
        var next = new List<int>();

        for (int f = 0; f < faceCount; f++)
        {
            if (float.IsPositiveInfinity(width[f])) continue;

            nearby.Clear();
            visited.Clear();
            frontier.Clear();
            visited.Add(f);
            frontier.Add(f);
            nearby.Add(value[f]);

            float walked = 0f;
            while (walked < radius && frontier.Count > 0)
            {
                next.Clear();
                foreach (int face in frontier)
                    foreach (int e in mesh.FaceEdges[face])
                    {
                        int across = mesh.Across(e, face);
                        if (across < 0 || float.IsPositiveInfinity(width[across])) continue;
                        if (!visited.Add(across)) continue;

                        next.Add(across);
                        nearby.Add(value[across]);
                    }

                walked += step;
                (frontier, next) = (next, frontier);
            }

            nearby.Sort();
            median[f] = nearby[nearby.Count / 2];
        }

        return median;
    }
}
