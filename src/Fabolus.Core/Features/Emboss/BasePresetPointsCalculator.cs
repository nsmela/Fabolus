using System.Numerics;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.Decal;

/// <summary>
/// Calculates preset anchor points on a base mesh (bolus / anatomy model):
/// - Top (apex center, facing +Z, horizontal 0°)
/// - Front (center front at mid-height, facing -Y, horizontal 0°)
/// - Back (center back at mid-height, facing +Y, horizontal 0°)
/// </summary>
public static class BasePresetPointsCalculator
{
    private const float RaycastOffsetDistance = 100f;

    public static IReadOnlyList<DecalPresetPoint> Calculate(IGeometryEngine engine, IMesh baseMesh)
    {
        if (baseMesh is null)
            return Array.Empty<DecalPresetPoint>();

        var statsResult = engine.Evaluators.GetStatistics(baseMesh);
        if (statsResult.IsFailure)
            return Array.Empty<DecalPresetPoint>();

        var s = statsResult.Value;
        float zMid = (float)(s.MinZ + s.MaxZ) * 0.5f;
        float xCenter = (float)(s.MinX + s.MaxX) * 0.5f;
        float yCenter = (float)(s.MinY + s.MaxY) * 0.5f;
        float minY = (float)s.MinY;
        float maxY = (float)s.MaxY;
        float maxZ = (float)s.MaxZ;
        float baseWidth = (float)(s.MaxX - s.MinX);

        var presets = new List<DecalPresetPoint>(3);

        // 1. Top (raycast down from +Z at XY center) - horizontal orientation
        var topRayOrigin = new Vector3(xCenter, yCenter, maxZ + RaycastOffsetDistance);
        var topRayDir = new Vector3(0f, 0f, -1f);
        var topHitResult = engine.Evaluators?.Raycast(baseMesh, topRayOrigin, topRayDir);
        if (topHitResult is not null && topHitResult.IsSuccess)
        {
            presets.Add(new DecalPresetPoint("Top", topHitResult.Value.Point, topHitResult.Value.Normal, 0f, baseWidth, EmbossTarget.Base));
        }
        else
        {
            presets.Add(new DecalPresetPoint("Top", new Vector3(xCenter, yCenter, maxZ), Vector3.UnitZ, 0f, baseWidth, EmbossTarget.Base));
        }

        // 2. Front (-Y direction at mid-height) - horizontal orientation
        var frontRayOrigin = new Vector3(xCenter, minY - RaycastOffsetDistance, zMid);
        var frontRayDir = new Vector3(0f, 1f, 0f);
        var frontHitResult = engine.Evaluators?.Raycast(baseMesh, frontRayOrigin, frontRayDir);
        if (frontHitResult is not null && frontHitResult.IsSuccess)
        {
            presets.Add(new DecalPresetPoint("Front", frontHitResult.Value.Point, frontHitResult.Value.Normal, 0f, baseWidth, EmbossTarget.Base));
        }
        else
        {
            presets.Add(new DecalPresetPoint("Front", new Vector3(xCenter, minY, zMid), new Vector3(0f, -1f, 0f), 0f, baseWidth, EmbossTarget.Base));
        }

        // 3. Back (+Y direction at mid-height) - horizontal orientation
        var backRayOrigin = new Vector3(xCenter, maxY + RaycastOffsetDistance, zMid);
        var backRayDir = new Vector3(0f, -1f, 0f);
        var backHitResult = engine.Evaluators?.Raycast(baseMesh, backRayOrigin, backRayDir);
        if (backHitResult is not null && backHitResult.IsSuccess)
        {
            presets.Add(new DecalPresetPoint("Back", backHitResult.Value.Point, backHitResult.Value.Normal, 0f, baseWidth, EmbossTarget.Base));
        }
        else
        {
            presets.Add(new DecalPresetPoint("Back", new Vector3(xCenter, maxY, zMid), new Vector3(0f, 1f, 0f), 0f, baseWidth, EmbossTarget.Base));
        }

        return presets;
    }
}
