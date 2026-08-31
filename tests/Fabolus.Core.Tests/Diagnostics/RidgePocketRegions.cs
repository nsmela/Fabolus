using System.Numerics;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// Asks what the surface left unshaded inside the rim actually <em>is</em>, by naming the region the
/// fill put it in.
///
/// <para>
/// <see cref="RidgeAdmissionDiff"/> established that relaxing the grow pair admits nothing at the pinch
/// but chords across a ridge that already runs through those vertices, so no edge there can be moving
/// the band's boundary. What decides the boundary instead is the fill, and the fill is global: a face
/// is band or surface according to which side of the walls the flood reached it from, which can be
/// settled hundreds of millimetres away. This separates the two things that a bare face mask cannot.
/// A pocket that is its own region is a region the fill declined, and the fill's thresholds are then
/// the thing to look at. A pocket that is part of one of the two shell surfaces was never enclosed at
/// all, and no fill threshold could have reached it.
/// </para>
///
/// <para>Prints rather than asserts; the numbers are the finding.</para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class RidgePocketRegions
{
    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;
    private readonly ITestOutputHelper _log;

    public RidgePocketRegions(GeometryEngineFixture assets, ITestOutputHelper log)
    {
        _assets = assets;
        _engine = assets.Engine;
        _log = log;
    }

    [Theory]
    [InlineData("standard", "3mf/test bolus standard.3mf")]
    public void WhereThePocketBelongs(string id, string asset)
    {
        var imported = _engine.IO.Import(_assets.GetAssetPath(asset));
        var mould = MouldMesh.Create(imported.Value);
        var body = new PartingMeshFeature(_engine).GetBodyMesh(mould.Value).Value.Mesh;

        var strictOptions = RidgeDetectionOptions.Default;
        var strict = RidgeDetection.Diagnose(body, strictOptions, traceEdges: true);
        var loose = RidgeDetection.Diagnose(
            body, strictOptions with { GrowCurvature = 0.10f, GrowAngleDegrees = 15f }, traceEdges: true);

        int faceCount = strict.RidgeFaces.Length;
        var area = FaceAreas(body);
        var centroid = Centroids(strict.Edges, faceCount);

        _log.WriteLine($"=== {id} ===");
        Describe("strict 0.20/25", strict, area);
        Describe("loose  0.10/15", loose, area);

        // The pocket, defined by the repair rather than by eye: the surface the relaxed run brings into
        // the ridge and the strict run leaves out.
        var gained = Enumerable.Range(0, faceCount)
            .Where(f => loose.RidgeFaces[f] && !strict.RidgeFaces[f]).ToList();
        var lost = Enumerable.Range(0, faceCount)
            .Where(f => strict.RidgeFaces[f] && !loose.RidgeFaces[f]).ToList();

        _log.WriteLine("");
        _log.WriteLine($"gained {gained.Count} faces / {gained.Sum(f => area[f]):F0}mm2   " +
                       $"lost {lost.Count} faces / {lost.Sum(f => area[f]):F0}mm2");

        var suspect = strict.BandProfile.PerFaceSuspect;
        var pinch = Enumerable.Range(0, faceCount).Where(f => suspect[f]).Select(f => centroid[f]).ToArray();
        if (pinch.Length == 0)
        {
            _log.WriteLine("no suspect faces in the strict run - nothing to localise against");
            return;
        }

        var distance = new float[faceCount];
        for (int f = 0; f < faceCount; f++) distance[f] = Nearest(centroid[f], pinch);

        _log.WriteLine($"pinch: {pinch.Length} suspect faces, {strict.BandProfile.SuspectArea:F0}mm2");

        // ---- what the strict run had called the gained faces ----
        foreach (float radius in new[] { 10f, 25f, float.MaxValue })
        {
            var near = gained.Where(f => distance[f] < radius).ToList();
            if (near.Count == 0) continue;

            _log.WriteLine("");
            _log.WriteLine(radius > 1e6f
                ? $"-- all {near.Count} gained faces, by the region the strict run put them in --"
                : $"-- the {near.Count} gained faces within {radius:F0}mm of the pinch, by strict region --");
            _log.WriteLine("   region        role          faces here   region total      area here   nearest");

            foreach (var group in near
                .GroupBy(f => strict.Territories.FaceRegion[f])
                .OrderByDescending(g => g.Sum(f => area[f]))
                .Take(10))
            {
                int total = strict.Territories.FaceRegion.Count(r => r == group.Key);
                _log.WriteLine(
                    $"  {group.Key,7}  {strict.Territories.Role(group.Key),-14} {group.Count(),10} " +
                    $"{total,14} {group.Sum(f => area[f]),14:F1} {group.Min(f => distance[f]),9:F1}");
            }
        }

        // ---- and what the loose run makes of the same faces ----
        _log.WriteLine("");
        _log.WriteLine("-- the same faces in the loose run --");
        _log.WriteLine("   region        role          faces here   region total      area here");
        foreach (var group in gained
            .GroupBy(f => loose.Territories.FaceRegion[f])
            .OrderByDescending(g => g.Sum(f => area[f]))
            .Take(10))
        {
            int total = loose.Territories.FaceRegion.Count(r => r == group.Key);
            _log.WriteLine(
                $"  {group.Key,7}  {loose.Territories.Role(group.Key),-14} {group.Count(),10} " +
                $"{total,14} {group.Sum(f => area[f]),14:F1}");
        }

        // ---- can the reclaimed pockets stand on their own? ----
        //
        // A region is only band if the connected group of regions it belongs to reaches both shell
        // surfaces; a region that borders one surface and nothing else is not a wall between them and
        // is never filled. So whether the pockets touch both surfaces directly says whether closing
        // one off locally could ever have been enough, or whether it is band only by virtue of regions
        // it reaches through - which a local pass, by construction, does not create.
        var neighbours = Adjacency(loose);

        _log.WriteLine("");
        _log.WriteLine("-- the loose run's reclaimed pockets: what each region borders directly --");
        _log.WriteLine("   region     faces  gained   touches A   touches B   neighbour regions");
        foreach (var group in gained
            .GroupBy(f => loose.Territories.FaceRegion[f])
            .Where(g => loose.Territories.RegionIsBand.Length > g.Key && g.Key >= 0
                        && loose.Territories.RegionIsBand[g.Key])
            .OrderByDescending(g => g.Count())
            .Take(10))
        {
            var borders = neighbours.TryGetValue(group.Key, out var set) ? set : new HashSet<int>();
            _log.WriteLine(
                $"  {group.Key,7} {loose.Territories.FaceRegion.Count(r => r == group.Key),9} " +
                $"{group.Count(),7} {borders.Contains(loose.Territories.First),11} " +
                $"{borders.Contains(loose.Territories.Second),11}   {borders.Count}");
        }

        // ---- and where the suspect band faces themselves sit ----
        _log.WriteLine("");
        _log.WriteLine("-- the strict run's suspect band faces, by region, and where they land in the loose run --");
        foreach (var group in Enumerable.Range(0, faceCount).Where(f => suspect[f])
            .GroupBy(f => strict.Territories.FaceRegion[f])
            .OrderByDescending(g => g.Count())
            .Take(8))
        {
            string looseSide = string.Join(", ", group
                .GroupBy(f => loose.Territories.Role(loose.Territories.FaceRegion[f]))
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key} {g.Count()}"));
            _log.WriteLine(
                $"  {group.Key,7}  {strict.Territories.Role(group.Key),-14} {group.Count(),6} faces " +
                $"-> loose: {looseSide}");
        }
    }

    private void Describe(string name, RidgeDiagnosis d, float[] area)
    {
        var t = d.Territories;
        if (!t.Available)
        {
            _log.WriteLine($"{name}: no territories");
            return;
        }

        int regions = t.RegionIsBand.Length;
        float total = area.Sum();
        float AreaOf(int region) => Enumerable.Range(0, area.Length)
            .Where(f => t.FaceRegion[f] == region).Sum(f => area[f]);

        _log.WriteLine(
            $"{name}: {regions} regions, {t.RegionIsBand.Count(b => b)} band, " +
            $"{t.RegionBandGroup.Where(g => g >= 0).Distinct().Count()} band groups  |  " +
            $"surface A {AreaOf(t.First) / total:P1}  surface B {AreaOf(t.Second) / total:P1}  " +
            $"ridge {d.RidgeFaces.Count(r => r)} faces / " +
            $"{Enumerable.Range(0, area.Length).Where(f => d.RidgeFaces[f]).Sum(f => area[f]):F0}mm2");
    }

    /// <summary>Which regions border which, across the final ridge edges.</summary>
    private static Dictionary<int, HashSet<int>> Adjacency(RidgeDiagnosis d)
    {
        var neighbours = new Dictionary<int, HashSet<int>>();
        foreach (var edge in d.Edges)
        {
            if (!edge.Final || edge.FaceB < 0) continue;

            int left = d.Territories.FaceRegion[edge.FaceA];
            int right = d.Territories.FaceRegion[edge.FaceB];
            if (left == right) continue;

            Link(neighbours, left, right);
            Link(neighbours, right, left);
        }
        return neighbours;

        static void Link(Dictionary<int, HashSet<int>> map, int key, int value)
        {
            if (!map.TryGetValue(key, out var set)) map[key] = set = new HashSet<int>(2);
            set.Add(value);
        }
    }

    /// <summary>
    /// Face centroids from the edge trace, so this needs no second weld of its own. The mean of a
    /// triangle's three edge midpoints is its centroid exactly.
    /// </summary>
    private static Vector3[] Centroids(IReadOnlyList<RidgeEdgeAdmission> edges, int faceCount)
    {
        var sum = new Vector3[faceCount];
        var count = new int[faceCount];
        foreach (var edge in edges)
        {
            if (edge.FaceA >= 0) { sum[edge.FaceA] += edge.Mid; count[edge.FaceA]++; }
            if (edge.FaceB >= 0) { sum[edge.FaceB] += edge.Mid; count[edge.FaceB]++; }
        }

        var centroid = new Vector3[faceCount];
        for (int f = 0; f < faceCount; f++)
            centroid[f] = count[f] > 0 ? sum[f] / count[f] : Vector3.Zero;
        return centroid;
    }

    private static float[] FaceAreas(IMesh mesh)
    {
        var area = new float[mesh.Triangles.Length / 3];
        for (int t = 0; t < area.Length; t++)
        {
            var a = mesh.Vertices[mesh.Triangles[t * 3]];
            var b = mesh.Vertices[mesh.Triangles[(t * 3) + 1]];
            var c = mesh.Vertices[mesh.Triangles[(t * 3) + 2]];
            area[t] = Vector3.Cross(b - a, c - a).Length() * 0.5f;
        }
        return area;
    }

    private static float Nearest(Vector3 point, Vector3[] targets)
    {
        float best = float.MaxValue;
        foreach (var other in targets) best = MathF.Min(best, Vector3.DistanceSquared(point, other));
        return MathF.Sqrt(best);
    }
}
