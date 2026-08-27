using System.Numerics;
using Fabolus.Core.Geometry;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// Guards the needle-spike removal in <see cref="PartingLineSmoother"/>. The marching-triangles
/// isoline occasionally produces a sub-millimetre cluster of points that doubles back on itself,
/// reading as a sharp spike in an otherwise smooth curve (observed on chin.3mf as a 168 degree
/// reversal that survived plain Taubin smoothing). The smoother's resample+despike pre-pass must
/// erase it.
/// </summary>
public class PartingLineSmootherTests
{
    /// <summary>A clean circle in the XZ plane, plus an injected needle spike.</summary>
    private static List<Vector3> CircleWithNeedle()
    {
        const int n = 120;
        const float radius = 40f;
        var pts = new List<Vector3>(n + 4);
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            pts.Add(new Vector3((float)(radius * Math.Cos(a)), 0f, (float)(radius * Math.Sin(a))));
        }

        // Inject a needle at ~1/4 of the way round: a tiny back-and-forth wobble spanning < 1mm,
        // exactly the shape the isoline walk emits and that Taubin smoothing cannot flatten.
        int at = n / 4;
        var baseP = pts[at];
        pts.Insert(at + 1, baseP + new Vector3(0.35f, 0f, 0.30f));
        pts.Insert(at + 2, baseP + new Vector3(-0.20f, 0f, -0.15f));
        pts.Insert(at + 3, baseP + new Vector3(0.15f, 0f, 0.10f));
        return pts;
    }

    private static double MaxTurnAngleDeg(IReadOnlyList<Vector3> loop)
    {
        int n = loop.Count;
        double worst = 0;
        for (int i = 0; i < n; i++)
        {
            var p = loop[(i - 1 + n) % n];
            var c = loop[i];
            var q = loop[(i + 1) % n];
            var a = new Vector2(c.X - p.X, c.Z - p.Z);
            var b = new Vector2(q.X - c.X, q.Z - c.Z);
            if (a.Length() < 1e-6f || b.Length() < 1e-6f) continue;
            double dot = Math.Clamp(Vector2.Dot(Vector2.Normalize(a), Vector2.Normalize(b)), -1, 1);
            worst = Math.Max(worst, Math.Acos(dot) * 180.0 / Math.PI);
        }
        return worst;
    }

    [Fact]
    public void Smooth_RemovesNeedleSpike_ThatPlainTaubinLeaves()
    {
        var line = new PartingLine(new[] { CircleWithNeedle() });

        // Sanity: the injected loop really does contain a near-reversal spike.
        MaxTurnAngleDeg(line.Loops[0]).Should().BeGreaterThan(90);

        // The default pipeline (resample + despike + Taubin) erases it.
        var smoothed = PartingLineSmoother.Smooth(line, PartingLineSmoothingOptions.Default);
        MaxTurnAngleDeg(smoothed.Loops[0]).Should().BeLessThan(45,
            "resample+despike must erase the needle before it reaches the flange");
    }

    [Fact]
    public void Smooth_None_LeavesLoopUntouched()
    {
        var loop = CircleWithNeedle();
        var line = new PartingLine(new[] { loop });

        var result = PartingLineSmoother.Smooth(line, PartingLineSmoothingOptions.None);

        result.Loops[0].Count.Should().Be(loop.Count, "None must be a true identity (no resample, no smoothing)");
    }

    /// <summary>
    /// A circle carrying an inward peninsula that re-crosses the stretch it left from - the shape the
    /// isoline produces where the silhouette plunges (the jaw corners of chin.3mf). Seen along the
    /// pull direction it is a hook the flange would have to offset outward from on both sides at once.
    /// </summary>
    private static List<Vector3> CircleWithFootprintHook()
    {
        const int n = 160;
        const float radius = 40f;
        var pts = new List<Vector3>(n + 4);
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            pts.Add(new Vector3((float)(radius * Math.Cos(a)), 0f, (float)(radius * Math.Sin(a))));
        }

        pts.InsertRange(n / 3 + 1, new[]
        {
            new Vector3(28f, -4f, 22f),
            new Vector3(14f, -6f, 30f),
            new Vector3(20f, -5f, 38f),
            new Vector3(34f, -3f, 26f),
        });
        return pts;
    }

    /// <summary>Proper crossings of the loop's XZ projection; adjacent segments are skipped.</summary>
    private static int FootprintSelfIntersections(IReadOnlyList<Vector3> loop)
    {
        int n = loop.Count;
        var flat = new Vector2[n];
        for (int i = 0; i < n; i++) flat[i] = new Vector2(loop[i].X, loop[i].Z);

        int count = 0;
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1) continue;
                if (Crosses(flat[i], flat[(i + 1) % n], flat[j], flat[(j + 1) % n])) count++;
            }
        }
        return count;

        static bool Crosses(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1)
        {
            float d1 = Cross(b1 - b0, a0 - b0), d2 = Cross(b1 - b0, a1 - b0);
            float d3 = Cross(a1 - a0, b0 - a0), d4 = Cross(a1 - a0, b1 - a0);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
                && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));

            static float Cross(Vector2 p, Vector2 q) => (p.X * q.Y) - (p.Y * q.X);
        }
    }

    [Fact]
    public void Smooth_RemovesFootprintSelfIntersection()
    {
        var line = new PartingLine(new[] { CircleWithFootprintHook() });

        // Sanity: the injected peninsula really does cross itself in the footprint.
        FootprintSelfIntersections(line.Loops[0]).Should().BeGreaterThan(0);

        var smoothed = PartingLineSmoother.Smooth(line, PartingLineSmoothingOptions.Default);

        FootprintSelfIntersections(smoothed.Loops[0]).Should().Be(0,
            "relaxation can only soften a crossing - de-looping has to cut it out");
    }
}
