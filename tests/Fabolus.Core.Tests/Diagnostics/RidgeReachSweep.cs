using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// How far from the pinch the relaxed run's extra edges have to reach before the pocket is repaired,
/// and which downstream test is the one that refuses it short of that.
///
/// <para>
/// Four second passes confined to the pinch all produced the same band, and two facts about the
/// relaxed run explain that only if one of them is the cause: the edges it admits at the pinch are
/// chords across a ridge that is already there (<see cref="RidgeAdmissionDiff"/>), and the pockets it
/// reclaims border neither shell surface (<see cref="RidgePocketRegions"/>). Either the local edges
/// never seal the pocket into a region at all, or they seal it and the region is then thrown out - and
/// the two want completely different fixes.
/// </para>
///
/// <para>
/// So the fill and the classification are replayed over the strict run's edges plus the relaxed run's,
/// admitted within a growing radius of the pinch. Every threshold stays where it is and only reach
/// varies, which is what makes the step a face falls out at readable. The <c>all</c> row is the check:
/// it should land within a few faces of the real relaxed run.
/// </para>
///
/// <para>Prints rather than asserts; the numbers are the finding.</para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class RidgeReachSweep
{
    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;
    private readonly ITestOutputHelper _log;

    public RidgeReachSweep(GeometryEngineFixture assets, ITestOutputHelper log)
    {
        _assets = assets;
        _engine = assets.Engine;
        _log = log;
    }

    [Theory]
    [InlineData("standard", "3mf/test bolus standard.3mf")]
    public void HowFarTheRepairReaches(string id, string asset)
    {
        var imported = _engine.IO.Import(_assets.GetAssetPath(asset));
        var mould = MouldMesh.Create(imported.Value);
        var body = new PartingMeshFeature(_engine).GetBodyMesh(mould.Value).Value.Mesh;

        var options = RidgeDetectionOptions.Default;
        var strict = RidgeDetection.Diagnose(body, options, traceEdges: true);
        var loose = RidgeDetection.Diagnose(
            body, options with { GrowCurvature = 0.10f, GrowAngleDegrees = 15f }, traceEdges: true);

        int faceCount = strict.RidgeFaces.Length;
        var area = RidgeReplay.FaceAreas(body);
        float totalArea = area.Sum();
        float diagonal = RidgeReplay.Diagonal(body);

        var mesh = RidgeReplay.Merge(faceCount, strict, loose);
        var centroid = mesh.Centroids();

        var strictWalls = strict.Edges.Where(e => e.Final).Select(e => mesh.Index[e.Key]).ToHashSet();
        var looseWalls = loose.Edges.Where(e => e.Final).Select(e => mesh.Index[e.Key]).ToHashSet();
        var extra = looseWalls.Where(e => !strictWalls.Contains(e)).ToArray();

        var pinch = Enumerable.Range(0, faceCount)
            .Where(f => strict.BandProfile.PerFaceSuspect[f]).Select(f => centroid[f]).ToArray();
        var reach = extra.ToDictionary(e => e, e => RidgeReplay.Nearest(mesh.Edges[e].Mid, pinch));

        // The faces the relaxed run brings into the ridge: the repair, as a set of faces.
        var gained = Enumerable.Range(0, faceCount)
            .Where(f => loose.RidgeFaces[f] && !strict.RidgeFaces[f]).ToArray();

        _log.WriteLine($"=== {id} ===");
        _log.WriteLine($"{faceCount} faces, {totalArea:F0}mm2, diagonal {diagonal:F1}mm");
        _log.WriteLine($"strict walls {strictWalls.Count}, loose walls {looseWalls.Count}, " +
                       $"extra {extra.Length}, pinch {pinch.Length} faces, gained {gained.Length} faces");
        _log.WriteLine("");
        _log.WriteLine("of the gained faces, how many reach each stage:");
        _log.WriteLine("  radius   walls   sealed off   in a filled region   shaded   band   | all shaded");

        foreach (float radius in new[] { 0f, 5f, 10f, 20f, 35f, 50f, 75f, 100f, float.MaxValue })
        {
            var walls = new HashSet<int>(strictWalls);
            foreach (var (edge, distance) in reach)
                if (distance < radius) walls.Add(edge);

            var fill = RidgeReplay.Run(mesh, walls, area, totalArea, diagonal, options);

            int enclosed = gained.Count(f => fill.Region[f] != fill.First && fill.Region[f] != fill.Second);
            int filled = gained.Count(f => fill.Filled[fill.Region[f]]);
            int shaded = gained.Count(f => fill.Shaded[f]);
            int band = gained.Count(f => fill.IsBand[fill.Region[f]]);

            _log.WriteLine(
                $"  {(radius > 1e6f ? "all" : $"{radius:F0}mm"),-8} {walls.Count,6} {enclosed,12} " +
                $"{filled,20} {shaded,8} {band,6}   | {fill.Shaded.Count(s => s)}");
        }

        _log.WriteLine("");
        _log.WriteLine($"for comparison, the real runs shaded: strict {strict.RidgeFaces.Count(r => r)}, " +
                       $"loose {loose.RidgeFaces.Count(r => r)}");
    }
}
