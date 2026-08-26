using System.Numerics;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.Emboss;

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

    public static IReadOnlyList<DecalPresetPoint> Calculate(IGeometryEngine engine, IMesh mouldMesh)
    {
        if (mouldMesh is null)
            return Array.Empty<DecalPresetPoint>();

        var statsResult = engine.Evaluators.GetStatistics(mouldMesh);
        if (statsResult.IsFailure)
            return Array.Empty<DecalPresetPoint>();

        var s = statsResult.Value;
        float zMid = (float)(s.MinZ + s.MaxZ) * 0.5f;
        float xCenter = (float)(s.MinX + s.MaxX) * 0.5f;
        float yCenter = (float)(s.MinY + s.MaxY) * 0.5f;
        float minX = (float)s.MinX;
        float maxX = (float)s.MaxX;
        float minY = (float)s.MinY;
        float maxY = (float)s.MaxY;
        float mouldHeight = (float)(s.MaxZ - s.MinZ);
        float mouldWidth = (float)(s.MaxX - s.MinX);

        var presets = new List<DecalPresetPoint>(6);

        // 1. Front (-Y direction) - horizontal orientation
        var frontRayOrigin = new Vector3(xCenter, minY - RaycastOffsetDistance, zMid);
        var frontRayDir = new Vector3(0f, 1f, 0f);
        var frontHit = engine.Evaluators?.Raycast(mouldMesh, frontRayOrigin, frontRayDir);
        if (frontHit is not null && frontHit.IsSuccess)
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
        var backHit = engine.Evaluators?.Raycast(mouldMesh, backRayOrigin, backRayDir);
        if (backHit is not null && backHit.IsSuccess)
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
        var leftHit = engine.Evaluators?.Raycast(mouldMesh, leftRayOrigin, leftRayDir);
        if (leftHit is not null && leftHit.IsSuccess)
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
        var rightHit = engine.Evaluators?.Raycast(mouldMesh, rightRayOrigin, rightRayDir);
        if (rightHit is not null && rightHit.IsSuccess)
        {
            presets.Add(new DecalPresetPoint("Right", rightHit.Value.Point, rightHit.Value.Normal, 90f, mouldHeight, EmbossTarget.Mould));
        }
        else
        {
            presets.Add(new DecalPresetPoint("Right", new Vector3(maxX, yCenter, zMid), new Vector3(1f, 0f, 0f), 90f, mouldHeight, EmbossTarget.Mould));
        }

        // 5 & 6. Analyze 2D contour to find strong curves that don't overlap cardinals
        CalculateCurvePresets(engine, mouldMesh, s, mouldHeight, presets, out var curve1, out var curve2);
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
        MeshStatistics stats,
        float mouldHeight,
        IReadOnlyList<DecalPresetPoint> cardinalPoints,
        out DecalPresetPoint? curve1,
        out DecalPresetPoint? curve2)
    {
        curve1 = null;
        curve2 = null;

        float zMid = (float)(stats.MinZ + stats.MaxZ) * 0.5f;
        float xCenter = (float)(stats.MinX + stats.MaxX) * 0.5f;
        float yCenter = (float)(stats.MinY + stats.MaxY) * 0.5f;
        float minX = (float)stats.MinX;
        float maxX = (float)stats.MaxX;
        float minY = (float)stats.MinY;
        float maxY = (float)stats.MaxY;

        const int samples = 72; // 5-degree increments
        var points = new List<Vector3>(samples);
        var normals = new List<Vector3>(samples);

        float radius = MathF.Max(maxX - minX, maxY - minY) * 0.5f + RaycastOffsetDistance;

        for (int i = 0; i < samples; i++)
        {
            float angle = i * (MathF.PI * 2f / samples);
            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);

            var rayOrigin = new Vector3(xCenter + cos * radius, yCenter + sin * radius, zMid);
            var rayDir = new Vector3(-cos, -sin, 0f);

            var hitResult = engine.Evaluators.Raycast(mouldMesh, rayOrigin, rayDir);
            if (hitResult.IsSuccess)
            {
                points.Add(hitResult.Value.Point);
                normals.Add(hitResult.Value.Normal);
            }
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
            if (inEdge.LengthSquared() < 1e-12f || outEdge.LengthSquared() < 1e-12f)
            {
                curvatures[i] = 0f;
                continue;
            }

            float dot = Math.Clamp(Vector2.Dot(Vector2.Normalize(inEdge), Vector2.Normalize(outEdge)), -1f, 1f);
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
                if (Vector3.Distance(pt, card.Position) < CardinalOverlapDistanceThreshold)
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
}
