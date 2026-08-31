using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// Tries the one relaxation the reach sweep leaves standing: soft edges admitted rim-wide, but only
/// where the ridge already runs.
///
/// <para>
/// The sweep showed the repair is not local - sealing the pinch's pockets needs edges spread over the
/// whole rim, and no radius short of the whole model gets most of them. It also showed that once a
/// face is sealed off it is filled and classified as band every time, so neither the fill's size tests
/// nor the group-spanning rule is refusing anything. That leaves only the grow threshold, and lowering
/// it everywhere is what breaks the other bodies: at 0.10/15 the grow pass percolates across surface
/// relief on <c>eye</c> and <c>larynx-small</c> until the guard fires.
/// </para>
///
/// <para>
/// The distinction those two cases turn on is not how soft an edge is but where it is. Relief that
/// percolates starts away from any ridge; the edges that repair the pinch are, measurably, attached to
/// one - 743 of the 1078 the relaxed run adds share a vertex with the ridge the default run already
/// found. So this admits the lower pair only by dilation from the ridge, a step at a time, and watches
/// two things: whether the pinch's pockets seal, and whether the clean bodies start growing a ridge
/// they should not have. It is the same hysteresis the seed/grow pair already is, applied in space
/// rather than in strength.
/// </para>
///
/// <para>
/// A replay, not a detector change: nothing here is in the production path, and the point is to find
/// out whether the rule is worth putting there.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class RidgeRimGrowth
{
    private const float SoftCurvature = 0.10f;
    private const float SoftAngle = 15f;

    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;
    private readonly ITestOutputHelper _log;

    public RidgeRimGrowth(GeometryEngineFixture assets, ITestOutputHelper log)
    {
        _assets = assets;
        _engine = assets.Engine;
        _log = log;
    }

    [Fact]
    public void DilatingTheRidgeIntoSoftEdges()
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

        var options = RidgeDetectionOptions.Default;
        var loose = options with { GrowCurvature = SoftCurvature, GrowAngleDegrees = SoftAngle };

        _log.WriteLine("steps=0 is the default run replayed; 'global' is the whole 0.10/15 run for");
        _log.WriteLine("comparison. 'edge %' against the percolation guard's " +
                       $"{options.MaxRidgeEdgeFraction:P0}. 'gained' is the share of the faces the");
        _log.WriteLine("global relaxation adds that this rule also reaches.");
        _log.WriteLine("");
        _log.WriteLine("  model         steps   walls  edge %   shaded  shaded %  regions   band  gained");

        foreach (var (id, asset) in models)
        {
            var imported = _engine.IO.Import(_assets.GetAssetPath(asset));
            var mould = MouldMesh.Create(imported.Value);
            var body = new PartingMeshFeature(_engine).GetBodyMesh(mould.Value).Value.Mesh;

            var strictRun = RidgeDetection.Diagnose(body, options, traceEdges: true);
            var looseRun = RidgeDetection.Diagnose(body, loose, traceEdges: true);

            int faceCount = strictRun.RidgeFaces.Length;
            var area = RidgeReplay.FaceAreas(body);
            float totalArea = area.Sum();
            float diagonal = RidgeReplay.Diagonal(body);

            var mesh = RidgeReplay.Merge(faceCount, strictRun, looseRun);
            var walls = strictRun.Edges.Where(e => e.Final).Select(e => mesh.Index[e.Key]).ToHashSet();

            var gained = Enumerable.Range(0, faceCount)
                .Where(f => looseRun.RidgeFaces[f] && !strictRun.RidgeFaces[f]).ToArray();

            for (int steps = 0; steps <= 5; steps++)
            {
                if (steps > 0 && Dilate(mesh, walls) == 0) break;

                Report(id, $"{steps}", mesh, walls, area, totalArea, diagonal, options, gained, faceCount);
            }

            var global = looseRun.Edges.Where(e => e.Final).Select(e => mesh.Index[e.Key]).ToHashSet();
            Report(id, "global", mesh, global, area, totalArea, diagonal, options, gained, faceCount);
            _log.WriteLine("");
        }
    }

    /// <summary>
    /// One dilation step: every soft edge touching the ridge joins it. Collected before any is added,
    /// so a step spreads one ring rather than running away along a chain within the same pass.
    /// </summary>
    private static int Dilate(RidgeTopology mesh, HashSet<int> walls)
    {
        var frontier = new HashSet<int>();
        foreach (int index in walls)
        {
            frontier.Add(mesh.Edges[index].A);
            frontier.Add(mesh.Edges[index].B);
        }

        var admit = new List<int>();
        foreach (int vertex in frontier)
            foreach (int index in mesh.VertexEdges[vertex] ?? Enumerable.Empty<int>())
            {
                if (walls.Contains(index)) continue;

                var edge = mesh.Edges[index];
                if (edge.Curvature > SoftCurvature || edge.AngleDegrees > SoftAngle) admit.Add(index);
            }

        foreach (int index in admit) walls.Add(index);
        return admit.Count;
    }

    private void Report(
        string id, string steps, RidgeTopology mesh, HashSet<int> walls,
        float[] area, float totalArea, float diagonal, RidgeDetectionOptions options,
        int[] gained, int faceCount)
    {
        var fill = RidgeReplay.Run(mesh, walls, area, totalArea, diagonal, options);

        float shadedArea = 0f;
        int shaded = 0;
        for (int f = 0; f < faceCount; f++)
        {
            if (!fill.Shaded[f]) continue;
            shaded++;
            shadedArea += area[f];
        }

        int covered = gained.Count(f => fill.Shaded[f]);

        _log.WriteLine(
            $"  {id,-13} {steps,5} {walls.Count,7} {(float)walls.Count / mesh.Edges.Length,7:P1} " +
            $"{shaded,8} {shadedArea / totalArea,9:P1} {fill.RegionCount,8} " +
            $"{fill.IsBand.Count(b => b),6} " +
            $"{(gained.Length == 0 ? "-" : $"{(float)covered / gained.Length:P0}"),7}");
    }
}
