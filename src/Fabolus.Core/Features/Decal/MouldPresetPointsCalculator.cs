using System.Numerics;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.Decal;

/// <summary>
/// Calculates preset anchor points along the outer contour of a mould mesh at mid-height:
/// - 4 cardinal points (Front, Back, Left, Right)
/// - Up to 2 points of maximum curvature (Curve 1, Curve 2)
/// </summary>
public static class MouldPresetPointsCalculator
{
    private const float RaycastOffsetDistance = 100f;
    private const float UsableHeightFraction = 0.8f;
    private const float CharacterAspectSafetyFactor = 1.15f;
    private const float CardinalOverlapDistanceThreshold = 5.0f;
    private const float DefaultCapHeight = 6.0f;
    private const int CurveSamples = 72;

    public static IReadOnlyList<DecalPresetPoint> Calculate(IGeometryEngine engine, IMesh mouldMesh)
    {
        if (mouldMesh is null)
            return Array.Empty<DecalPresetPoint>();

        var statsResult = engine.Evaluators.GetStatistics(mouldMesh);
        if (statsResult.IsFailure)
            return Array.Empty<DecalPresetPoint>();

        // The mould's own index, shared with the decal preview that is about to query it too.
        var indexResult = engine.Spatial.IndexFor(mouldMesh);
        if (indexResult.IsFailure)
            return Array.Empty<DecalPresetPoint>();
        var index = indexResult.Value;

        var s = statsResult.Value;
        float zMid = (float)(s.BoundsMin.Z + s.BoundsMax.Z) * 0.5f;
        float xCenter = (float)(s.BoundsMin.X + s.BoundsMax.X) * 0.5f;
        float yCenter = (float)(s.BoundsMin.Y + s.BoundsMax.Y) * 0.5f;
        float minX = (float)s.BoundsMin.X;
        float maxX = (float)s.BoundsMax.X;
        float minY = (float)s.BoundsMin.Y;
        float maxY = (float)s.BoundsMax.Y;
        float mouldHeight = (float)(s.BoundsMax.Z - s.BoundsMin.Z);
        float mouldWidth = (float)(s.BoundsMax.X - s.BoundsMin.X);

        var presets = new List<DecalPresetPoint>(6);

        // 1. Front (-Y direction) - horizontal orientation
        var frontRayOrigin = new Vector3(xCenter, minY - RaycastOffsetDistance, zMid);
        var frontRayDir = new Vector3(0f, 1f, 0f);
        var frontHit = index.Raycast(frontRayOrigin, GeometryEngine.Core.Geometry.Primitives.Direction.From(frontRayDir).Value);
        if (frontHit.HasValue)
        {
            presets.Add(new DecalPresetPoint("Front", frontHit.Value.Point, frontHit.Value.Normal, 0f, mouldWidth, EmbossTarget.Mould));
        }
        else
        {
            presets.Add(new DecalPresetPoint("Front", new Vector3(xCenter, minY, zMid), new Vector3(0f, -1f, 0f), 0f, mouldWidth, EmbossTarget.Mould));
        }

        // 2. Back (+Y direction) - horizontal orientation
        var backRayOrigin = new Vector3(xCenter, maxY + RaycastOffsetDistance, zMid);
        var backRayDir = new Vector3(0f, -1f, 0f);
        var backHit = index.Raycast(backRayOrigin, GeometryEngine.Core.Geometry.Primitives.Direction.From(backRayDir).Value);
        if (backHit.HasValue)
        {
            presets.Add(new DecalPresetPoint("Back", backHit.Value.Point, backHit.Value.Normal, 0f, mouldWidth, EmbossTarget.Mould));
        }
        else
        {
            presets.Add(new DecalPresetPoint("Back", new Vector3(xCenter, maxY, zMid), new Vector3(0f, 1f, 0f), 0f, mouldWidth, EmbossTarget.Mould));
        }

        // 3. Left (-X direction)
        var leftRayOrigin = new Vector3(minX - RaycastOffsetDistance, yCenter, zMid);
        var leftRayDir = new Vector3(1f, 0f, 0f);
        var leftHit = index.Raycast(leftRayOrigin, GeometryEngine.Core.Geometry.Primitives.Direction.From(leftRayDir).Value);
        if (leftHit.HasValue)
        {
            presets.Add(new DecalPresetPoint("Left", leftHit.Value.Point, leftHit.Value.Normal, 90f, mouldHeight, EmbossTarget.Mould));
        }
        else
        {
            presets.Add(new DecalPresetPoint("Left", new Vector3(minX, yCenter, zMid), new Vector3(-1f, 0f, 0f), 90f, mouldHeight, EmbossTarget.Mould));
        }

        // 4. Right (+X direction)
        var rightRayOrigin = new Vector3(maxX + RaycastOffsetDistance, yCenter, zMid);
        var rightRayDir = new Vector3(-1f, 0f, 0f);
        var rightHit = index.Raycast(rightRayOrigin, GeometryEngine.Core.Geometry.Primitives.Direction.From(rightRayDir).Value);
        if (rightHit.HasValue)
        {
            presets.Add(new DecalPresetPoint("Right", rightHit.Value.Point, rightHit.Value.Normal, 90f, mouldHeight, EmbossTarget.Mould));
        }
        else
        {
            presets.Add(new DecalPresetPoint("Right", new Vector3(maxX, yCenter, zMid), new Vector3(1f, 0f, 0f), 90f, mouldHeight, EmbossTarget.Mould));
        }

        // 5 & 6. Analyze 2D contour to find strong curves that don't overlap cardinals
        CalculateCurvePresets(engine, mouldMesh, index, s, mouldHeight, presets, out var curve1, out var curve2);
        if (curve1 is not null) presets.Add(curve1);
        if (curve2 is not null) presets.Add(curve2);

        return presets;
    }

    /// <summary>
    /// Calculates the suggested text cap height (mm) based on total mould height, mould thickness, and character count.
    /// </summary>
    public static float CalculateSuggestedCapHeight(float mouldHeight, int charCount, float mouldThickness = 0f, float maxCapHeight = 10.0f, float minCapHeight = 3.0f)
    {
        float effectiveHeight = mouldHeight - mouldThickness;
        if (effectiveHeight <= 0f) return DefaultCapHeight;

        int n = Math.Max(1, charCount);
        float usableHeight = effectiveHeight * UsableHeightFraction;
        float calculated = usableHeight / (n * CharacterAspectSafetyFactor);
        return Math.Clamp(MathF.Round(calculated, 1), minCapHeight, maxCapHeight);
    }

    private static void CalculateCurvePresets(
        IGeometryEngine engine,
        IMesh mouldMesh,
        GeometryEngine.Core.Geometry.ISpatialIndex index,
        MeshStatistics stats,
        float mouldHeight,
        IReadOnlyList<DecalPresetPoint> cardinalPoints,
        out DecalPresetPoint? curve1,
        out DecalPresetPoint? curve2)
    {
        curve1 = null;
        curve2 = null;

        float zMid = (float)(stats.BoundsMin.Z + stats.BoundsMax.Z) * 0.5f;

        // The mould's outside wall at mid-height is the largest outline of the slice there. A mould
        // is hollow, so the slice also carries the bolus cavity - as a hole, which is no place for
        // a label and is left out by taking the outline alone.
        var slice = engine.Polygons.Slice(mouldMesh, zMid);
        if (slice.IsFailure || slice.Value.IsEmpty)
            return;

        var outline = slice.Value.MaxBy(polygon => polygon.Area)!.Outer;

        // Evenly spaced along the wall, so a long straight side and a tight corner are sampled
        // alike and the turn between neighbours measures the wall's curvature, not its distance
        // from the middle.
        var points = new List<Vector3>(CurveSamples);
        var normals = new List<Vector3>(CurveSamples);
        foreach (var sample in SampleEvenly(outline, CurveSamples))
        {
            var point = new Vector3(sample.X, sample.Y, zMid);
            var surface = index.ClosestPoint(point);
            if (!surface.HasValue)
                continue;

            points.Add(point);
            normals.Add(surface.Value.Normal);
        }

        if (points.Count < 8)
            return;

        int n = points.Count;
        var curvatures = new float[n];

        for (int i = 0; i < n; i++)
        {
            int prev = (i - 1 + n) % n;
            int next = (i + 1) % n;

            var inEdge = new Vector2(points[i].X - points[prev].X, points[i].Y - points[prev].Y);
            var outEdge = new Vector2(points[next].X - points[i].X, points[next].Y - points[i].Y);

            // Coincident samples would normalise to NaN and poison this sample and its neighbours
            // through the smoothing pass below; treat them as no turn at all.
            if (inEdge.LengthSquared < 1e-12f || outEdge.LengthSquared < 1e-12f)
            {
                curvatures[i] = 0f;
                continue;
            }

            float dot = (float)Math.Clamp(inEdge.Normalize().Dot(outEdge.Normalize()), -1.0, 1.0);
            curvatures[i] = 1f - dot;
        }

        // Smooth curvatures
        var smoothed = new float[n];
        for (int i = 0; i < n; i++)
        {
            int prev = (i - 1 + n) % n;
            int next = (i + 1) % n;
            smoothed[i] = (curvatures[prev] + curvatures[i] * 2f + curvatures[next]) * 0.25f;
        }

        // Find candidates that do not overlap cardinal points
        bool IsFarFromCardinals(Vector3 pt)
        {
            foreach (var card in cardinalPoints)
            {
                if (pt.DistanceTo(card.Position) < CardinalOverlapDistanceThreshold)
                    return false;
            }
            return true;
        }

        int bestIdx1 = -1;
        float maxCurv1 = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            if (smoothed[i] > maxCurv1 && IsFarFromCardinals(points[i]))
            {
                maxCurv1 = smoothed[i];
                bestIdx1 = i;
            }
        }

        if (bestIdx1 < 0) return;

        curve1 = new DecalPresetPoint("Curve 1", points[bestIdx1], normals[bestIdx1], 90f, mouldHeight, EmbossTarget.Mould);

        int minSep = Math.Max(3, n / 6);
        int bestIdx2 = -1;
        float maxCurv2 = float.MinValue;

        for (int i = 0; i < n; i++)
        {
            int dist = Math.Min(Math.Abs(i - bestIdx1), n - Math.Abs(i - bestIdx1));
            if (dist >= minSep && smoothed[i] > maxCurv2 && IsFarFromCardinals(points[i]))
            {
                maxCurv2 = smoothed[i];
                bestIdx2 = i;
            }
        }

        if (bestIdx2 >= 0)
        {
            curve2 = new DecalPresetPoint("Curve 2", points[bestIdx2], normals[bestIdx2], 90f, mouldHeight, EmbossTarget.Mould);
        }
    }

    /// <summary><paramref name="count"/> points spaced evenly by distance around a closed ring.</summary>
    private static List<Vector2> SampleEvenly(IReadOnlyList<Vector2> ring, int count)
    {
        var lengths = new double[ring.Count];
        double perimeter = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            lengths[i] = ring[i].DistanceTo(ring[(i + 1) % ring.Count]);
            perimeter += lengths[i];
        }

        var samples = new List<Vector2>(count);
        if (perimeter <= 0)
            return samples;

        int edge = 0;
        double edgeStart = 0;
        for (int s = 0; s < count; s++)
        {
            double along = perimeter * s / count;
            while (edge < ring.Count - 1 && edgeStart + lengths[edge] < along)
            {
                edgeStart += lengths[edge];
                edge++;
            }

            double t = lengths[edge] > 0 ? (along - edgeStart) / lengths[edge] : 0;
            samples.Add(ring[edge].LerpTo(ring[(edge + 1) % ring.Count], Math.Clamp(t, 0, 1)));
        }

        return samples;
    }
}
