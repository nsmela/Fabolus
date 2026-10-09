using Fabolus.Core.Geometry;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// Rebuilds the less certain of a rim's two creases from the more certain one, wherever the wall
/// between them reads thicker than it should.
///
/// <para>
/// The two creases have been treated as equally trustworthy and they are not. A crease is a fold in the
/// surface and a fold has a size; where the body rounds off the fold goes faint and the curve drawn
/// through it is a guess. Measured on the sample set, that is exactly what a thick wall turns out to
/// be: on <c>larynx-large</c> 13 of the 14 over-wide stations have one crease at a third of its usual
/// strength while the other holds steady, and on <c>scalp</c> all 5 do. So the wall is not thick there;
/// one of its two edges has wandered off.
/// </para>
///
/// <para>
/// Where that happens the confident crease is kept and the faint one is replaced by a point the
/// expected wall thickness away from it, in the direction the faint one was already heading. Elsewhere
/// both are left exactly as they were.
/// </para>
/// </summary>
internal static class CreaseRepair
{
    public static (RidgeContour First, RidgeContour Second, int Repaired) Repair(
        RidgeContour first, RidgeContour second, CreaseCertainty.FoldIndex folds,
        ISurfaceProjector? projector, double excess = 1.25, double lopsided = 1.5)
    {
        var stations = new List<(Vector3 A, Vector3 B, double Width, double StrengthA, double StrengthB)>();

        foreach (var point in first.Points)
        {
            var opposite = Closest(point, second);
            stations.Add((point, opposite, point.DistanceTo(opposite),
                folds.Strength(point), folds.Strength(opposite)));
        }

        if (stations.Count == 0) return (first, second, 0);

        var widths = stations.Select(s => s.Width).OrderBy(v => v).ToArray();
        double expected = widths[widths.Length / 2];

        var repairedFirst = new List<Vector3>(stations.Count);
        var repairedSecond = new List<Vector3>(stations.Count);
        int repaired = 0;

        foreach (var s in stations)
        {
            var a = s.A;
            var b = s.B;

            bool tooWide = s.Width > expected * excess;
            bool oneIsFaint = Math.Max(s.StrengthA, s.StrengthB)
                > lopsided * Math.Min(s.StrengthA, s.StrengthB);

            if (tooWide && oneIsFaint && s.Width > 1e-4)
            {
                // The confident one stays where it is; the faint one is pulled back along the line
                // joining them until the wall is the thickness the rest of the rim says it should be.
                if (s.StrengthA >= s.StrengthB) b = a + ((b - a).Normalize() * expected);
                else a = b + ((a - b).Normalize() * expected);

                if (projector is not null)
                {
                    a = projector.Project(a);
                    b = projector.Project(b);
                }

                repaired++;
            }

            repairedFirst.Add(a);
            repairedSecond.Add(b);
        }

        return (
            first with { Points = repairedFirst },
            second with { Points = repairedSecond },
            repaired);
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
