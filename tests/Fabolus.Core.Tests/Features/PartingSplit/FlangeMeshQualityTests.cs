using System.Numerics;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// Guards the post-triangulation remesh in <see cref="GeometryMeshLib.PartingTools.GenerateWavefrontFlangeMesh"/>.
/// The raw wavefront triangulation stitches concentric contours of very different vertex densities,
/// so it comes out almost entirely as near-degenerate slivers (observed on chin.3mf: 92% of faces
/// below 15 deg, median min-angle ~1 deg). The remesh must rebuild it into well-shaped triangles.
/// </summary>
[Collection("GeometryEngine collection")]
public class FlangeMeshQualityTests
{
    private readonly IGeometryEngine _engine;

    public FlangeMeshQualityTests(GeometryEngineFixture fixture) => _engine = fixture.Engine;

    /// <summary>A wavy closed loop in the XZ plane (pull = +Y), with mild Y undulation.</summary>
    private static List<Vector3> WavyLoop()
    {
        const int n = 160;
        var pts = new List<Vector3>(n);
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            float r = 40f + 6f * (float)Math.Sin(5 * a);      // lobed silhouette -> sharp curvature
            float y = 3f * (float)Math.Sin(3 * a);            // undulation along the pull axis
            pts.Add(new Vector3(r * (float)Math.Cos(a), y, r * (float)Math.Sin(a)));
        }
        return pts;
    }

    private static (double median, double min, double sliverFraction) MinAngleStats(IMesh mesh)
    {
        var v = mesh.Vertices;
        var t = mesh.Triangles;
        var mins = new List<double>(t.Length / 3);
        for (int i = 0; i + 2 < t.Length; i += 3)
        {
            var a = v[t[i]]; var b = v[t[i + 1]]; var c = v[t[i + 2]];
            mins.Add(Math.Min(Angle(a, b, c), Math.Min(Angle(b, c, a), Angle(c, a, b))));
        }
        mins.Sort();
        int slivers = mins.Count(x => x < 15);
        return (mins[mins.Count / 2], mins[0], (double)slivers / mins.Count);

        static double Angle(Vector3 p, Vector3 q, Vector3 r)
        {
            var u = q - p; var w = r - p;
            if (u.Length() < 1e-6f || w.Length() < 1e-6f) return 0;
            double d = Math.Clamp(Vector3.Dot(Vector3.Normalize(u), Vector3.Normalize(w)), -1, 1);
            return Math.Acos(d) * 180.0 / Math.PI;
        }
    }

    /// <summary>Per-face slope from horizontal (deg): median, and the fraction steeper than 45 deg.</summary>
    private static (double median, double steepFraction) SlopeStats(IMesh mesh)
    {
        var v = mesh.Vertices;
        var t = mesh.Triangles;
        var slopes = new List<double>(t.Length / 3);
        for (int i = 0; i + 2 < t.Length; i += 3)
        {
            var n = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
            if (n.Length() < 1e-9f) continue;
            double ny = Math.Abs(Vector3.Dot(Vector3.Normalize(n), Vector3.UnitY));
            slopes.Add(Math.Acos(Math.Clamp(ny, 0, 1)) * 180.0 / Math.PI);
        }
        slopes.Sort();
        int steep = slopes.Count(x => x > 45);
        return (slopes[slopes.Count / 2], (double)steep / slopes.Count);
    }

    [Fact]
    public void WavefrontFlange_IsWellTriangulated_NotSlivers()
    {
        var loop = WavyLoop();
        var box = new List<Vector2>
        {
            new(-70, -70), new(-70, 70), new(70, 70), new(70, -70),
        };

        var result = _engine.PartingTools.GenerateWavefrontFlangeMesh(loop, box, Vector3.UnitY, stepDistanceMm: 7.5f);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : "");

        var (median, min, sliverFraction) = MinAngleStats(result.Value);

        // The remesh should yield near-equilateral triangles across the whole ribbon; the overhang
        // relaxation leaves only a small fraction of thinner triangles behind.
        median.Should().BeGreaterThan(25, "the remeshed flange should be near-equilateral, not slivers");
        sliverFraction.Should().BeLessThan(0.05, "the flange should be overwhelmingly well-shaped triangles");
    }

    [Fact]
    public void ExtrudeFlange_ThickensSurfaceIntoWatertightSlab()
    {
        var box = new List<Vector2> { new(-70, -70), new(-70, 70), new(70, 70), new(70, -70) };
        var surface = _engine.PartingTools
            .GenerateWavefrontFlangeMesh(WavyLoop(), box, Vector3.UnitY, stepDistanceMm: 7.5f).Value;

        const float depth = 0.2f;
        var solid = _engine.PartingTools.ExtrudeFlange(surface, Vector3.UnitY, depth);
        solid.IsSuccess.Should().BeTrue(solid.IsFailure ? solid.Error.Description : "");

        // Two sheets: the solid has exactly double the surface's vertices.
        solid.Value.Vertices.Length.Should().Be(surface.Vertices.Length * 2);

        // The extruded slab must be a printable, closed solid.
        var topo = _engine.Evaluators.ValidateTopology(solid.Value);
        topo.IsSuccess.Should().BeTrue(topo.IsFailure ? topo.Error.Description : "");
        topo.Value.IsWatertight.Should().BeTrue("the walled two-sheet extrusion should seal into a solid");

        // Its extent along the pull axis grows by exactly the depth (surface offset +/- depth/2).
        var before = _engine.Evaluators.GetStatistics(surface).Value;
        var after = _engine.Evaluators.GetStatistics(solid.Value).Value;
        ((after.MaxY - after.MinY) - (before.MaxY - before.MinY)).Should().BeApproximately(depth, 1e-3);
    }

    [Fact]
    public void WavefrontFlange_IsMostlyPrintable_LowSlope()
    {
        var loop = WavyLoop();
        var box = new List<Vector2>
        {
            new(-70, -70), new(-70, 70), new(70, 70), new(70, -70),
        };

        var result = _engine.PartingTools.GenerateWavefrontFlangeMesh(loop, box, Vector3.UnitY, stepDistanceMm: 7.5f);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : "");

        var (median, steepFraction) = SlopeStats(result.Value);

        // The overhang-relaxation pass caps the flange a few degrees under the 45-degree limit, so the
        // body lands well under it - only a tiny fraction of faces (irreducible ones hard against a
        // plunging parting edge) may remain steep.
        median.Should().BeLessThan(30, "the flange should be a gentle ramp, not steep walls");
        steepFraction.Should().BeLessThan(0.05, "the overhang cap should push nearly every face under the 45-degree limit");
    }
}
