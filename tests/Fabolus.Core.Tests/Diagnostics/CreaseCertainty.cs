using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// How strongly each crease actually creases, sampled along its length, and whether the stretches
/// where the wall reads too thick are stretches where one of the two has gone weak.
///
/// <para>
/// The band has been treated so far as a thing bounded by two equally trustworthy curves. It need not
/// be. A crease is found by a fold in the surface, and a fold has a size; where the body rounds off,
/// or the mesh is coarse, or the rim is not quite a rim, the fold is faint and the curve drawn through
/// it is a guess. If a wall reading half again too thick turns out to have one confident crease and one
/// faint one, then the thickness is not the body being thick - it is the faint crease having wandered,
/// and the fix is to stop believing it.
/// </para>
///
/// <para>
/// Measured before any of that is built, because the alternative explanation is just as likely: both
/// creases are confident and the rim really does widen there.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class CreaseCertainty
{
    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;
    private readonly ITestOutputHelper _log;

    public CreaseCertainty(GeometryEngineFixture assets, ITestOutputHelper log)
    {
        _assets = assets;
        _engine = assets.Engine;
        _log = log;
    }

    [DiagnosticFact]
    public void WhereTheWallReadsThickIsOneCreaseFaint()
    {
        foreach (var (id, asset) in PartingLineCentringSweep.Models)
        {
            var imported = SavedMoulds.Open(_engine, _assets.GetAssetPath(asset));
            var mould = SavedMoulds.Mould(imported.Value);
            var body = new PartingMeshFeature(_engine).GetBodyMesh(mould.Value).Value.Mesh;

            var thickness = WallThickness.Measure(_engine, body, WallThicknessOptions.Default);
            if (thickness.IsFailure) continue;

            double wall = thickness.Value.Statistics.Median;
            var ridge = RidgeDetection.FindRidge(body, RidgeDetectionOptions.Default);
            var contours = ridge.Contours.Where(c => c.IsClosed).ToList();
            var walls = PartingStrategy.Rims(contours, wall)
                .Where(r => r.Kind == PartingRimKind.Wall).ToList();

            if (walls.Count == 0) continue;

            var index = new FoldIndex(body, Folds(body));
            _log.WriteLine($"=== {id}   wall {wall:F2} mm");

            foreach (var rim in walls)
            {
                var first = contours[rim.ContourIndices[0]];
                var second = contours[rim.ContourIndices[1]];

                // Sampled along the first crease, because a station has to be a place on the rim and
                // one of the two curves has to supply it.
                var stations = new List<(double Width, double StrengthA, double StrengthB)>();

                foreach (var point in first.Points)
                {
                    var opposite = Closest(point, second);
                    stations.Add((
                        point.DistanceTo(opposite),
                        index.Strength(point),
                        index.Strength(opposite)));
                }

                if (stations.Count == 0) continue;

                var widths = stations.Select(s => s.Width).OrderBy(v => v).ToArray();
                double median = widths[widths.Length / 2];

                var thick = stations.Where(s => s.Width > median * 1.25).ToList();
                var normal = stations.Where(s => s.Width <= median * 1.25).ToList();

                _log.WriteLine(
                    $"  rim {rim.Id}: separation median {median:F2} mm ({median / wall:F2} x wall), " +
                    $"{thick.Count} of {stations.Count} stations over 1.25x ({(double)thick.Count / stations.Count:P1})");
                _log.WriteLine(
                    $"    crease strength overall : A {Median(stations.Select(s => s.StrengthA)):F1} deg   " +
                    $"B {Median(stations.Select(s => s.StrengthB)):F1} deg");

                if (thick.Count == 0) continue;

                double thickA = Median(thick.Select(s => s.StrengthA));
                double thickB = Median(thick.Select(s => s.StrengthB));
                double normalA = Median(normal.Select(s => s.StrengthA));
                double normalB = Median(normal.Select(s => s.StrengthB));

                _log.WriteLine(
                    $"    where thick             : A {thickA:F1} deg ({thickA / Math.Max(normalA, 1e-3):F2} x its normal)   " +
                    $"B {thickB:F1} deg ({thickB / Math.Max(normalB, 1e-3):F2} x its normal)");

                // The question in one number: at a thick station, how lopsided are the two creases?
                int lopsided = thick.Count(s =>
                    Math.Max(s.StrengthA, s.StrengthB) > 1.5 * Math.Min(s.StrengthA, s.StrengthB));
                int evenly = thick.Count - lopsided;

                _log.WriteLine(
                    $"    of the thick stations   : {lopsided} lopsided (one crease over 1.5x the other), " +
                    $"{evenly} evenly creased");
            }
        }
    }

    /// <summary>
    /// Dihedral angle at every edge, which is what "how much of a crease is this" means before any
    /// threshold is applied to it. Taken from the mesh directly rather than from the ridge pass, so it
    /// measures the body rather than the detector's opinion of it.
    /// </summary>
    internal static Dictionary<(int, int), double> Folds(IMesh mesh)
    {
        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;
        int faceCount = triangles.Length / 3;

        var normals = new Vector3[faceCount];
        for (int f = 0; f < faceCount; f++)
        {
            var a = vertices[triangles[f * 3]];
            var b = vertices[triangles[(f * 3) + 1]];
            var c = vertices[triangles[(f * 3) + 2]];

            var cross = (b - a).Cross(c - a);
            normals[f] = cross.LengthSquared < 1e-16 ? Vector3.Zero : cross.Normalize();
        }

        var pairs = new Dictionary<(int, int), List<int>>(faceCount * 2);
        for (int f = 0; f < faceCount; f++)
            for (int e = 0; e < 3; e++)
            {
                int i = triangles[(f * 3) + e];
                int j = triangles[(f * 3) + ((e + 1) % 3)];
                var key = i < j ? (i, j) : (j, i);
                if (!pairs.TryGetValue(key, out var list)) pairs[key] = list = new List<int>(2);
                list.Add(f);
            }

        var folds = new Dictionary<(int, int), double>(pairs.Count);
        foreach (var (key, shared) in pairs)
        {
            if (shared.Count != 2) continue;
            var a = normals[shared[0]];
            var b = normals[shared[1]];
            if (a == Vector3.Zero || b == Vector3.Zero) continue;

            folds[key] = Math.Acos(Math.Clamp(a.Dot(b), -1.0, 1.0)) * 180.0 / Math.PI;
        }

        return folds;
    }

    /// <summary>
    /// How sharply the body folds beside a point on a crease: the largest dihedral angle among the
    /// edges within one edge-length of it.
    ///
    /// <para>
    /// The largest rather than the nearest, because a crease curve is smoothed and resampled off the
    /// mesh edges it came from - so the nearest edge is as often one running <em>along</em> the fold,
    /// where the surface is flat, as one crossing it. Taking the largest nearby asks "is there a fold
    /// here", which is the question.
    /// </para>
    /// </summary>
    internal sealed class FoldIndex
    {
        private readonly Dictionary<(int, int, int), List<int>> _cells = new();
        private readonly Vector3[] _midpoint;
        private readonly double[] _angle;
        private readonly double _cell;

        internal FoldIndex(IMesh mesh, Dictionary<(int, int), double> folds)
        {
            var vertices = mesh.Vertices;
            _midpoint = new Vector3[folds.Count];
            _angle = new double[folds.Count];

            double total = 0d;
            int at = 0;
            foreach (var (key, angle) in folds)
            {
                _midpoint[at] = (vertices[key.Item1] + vertices[key.Item2]) * 0.5;
                _angle[at] = angle;
                total += vertices[key.Item1].DistanceTo(vertices[key.Item2]);
                at++;
            }

            _cell = folds.Count == 0 ? 1.0 : Math.Max((double)(total / folds.Count), 1e-4);

            for (int i = 0; i < _midpoint.Length; i++)
            {
                var key = Cell(_midpoint[i]);
                if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<int>(8);
                list.Add(i);
            }
        }

        private (int, int, int) Cell(Vector3 p) => (
            (int)Math.Floor(p.X / _cell), (int)Math.Floor(p.Y / _cell), (int)Math.Floor(p.Z / _cell));

        public double Strength(Vector3 point)
        {
            var (cx, cy, cz) = Cell(point);
            double best = 0.0;
            double reach = _cell * _cell;

            for (int x = cx - 1; x <= cx + 1; x++)
                for (int y = cy - 1; y <= cy + 1; y++)
                    for (int z = cz - 1; z <= cz + 1; z++)
                    {
                        if (!_cells.TryGetValue((x, y, z), out var list)) continue;
                        foreach (int i in list)
                            if (_midpoint[i].DistanceSquared(point) <= reach)
                                best = Math.Max(best, _angle[i]);
                    }

            return best;
        }
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.ToArray();
        if (sorted.Length == 0) return 0.0;
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }

    private static Vector3 Closest(Vector3 from, RidgeContour contour)
    {
        var points = contour.Points;
        int spans = contour.IsClosed ? points.Count : points.Count - 1;

        var best = from;
        double bestDistance = double.MaxValue;

        for (int i = 0; i < spans; i++)
        {
            var a = points[i];
            var ab = points[(i + 1) % points.Count] - a;
            double lengthSquared = ab.LengthSquared;
            double t = lengthSquared < 1e-12
                ? 0.0
                : Math.Clamp((from - a).Dot(ab) / lengthSquared, 0.0, 1.0);

            var on = a + (ab * t);
            double distance = from.DistanceTo(on);
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = on;
        }

        return best;
    }
}
