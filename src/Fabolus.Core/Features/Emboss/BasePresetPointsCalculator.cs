using System;
using System.Collections.Generic;
using System.Numerics;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.Emboss;

/// <summary>
/// Calculates preset anchor points on a base mesh (bolus / anatomy model):
/// - Top (apex center, facing +Z, horizontal 0°)
/// - Front (center front at mid-height, facing -Y, horizontal 0°)
/// - Back (center back at mid-height, facing +Y, horizontal 0°)
/// </summary>
public static class BasePresetPointsCalculator
{
    public static IReadOnlyList<DecalPresetPoint> Calculate(IGeometryEngine engine, IMesh baseMesh)
    {
        if (baseMesh == null)
            return Array.Empty<DecalPresetPoint>();

        var statsResult = engine.Evaluators.GetStatistics(baseMesh);
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
        float maxZ = (float)s.MaxZ;
        float baseWidth = (float)(s.MaxX - s.MinX);

        var vertices = baseMesh.Vertices ?? Array.Empty<Vector3>();
        var triangles = baseMesh.Triangles ?? Array.Empty<int>();

        var presets = new List<DecalPresetPoint>(3);

        // 1. Top (raycast down from +Z at XY center) - horizontal orientation
        var topRayOrigin = new Vector3(xCenter, yCenter, maxZ + 100f);
        var topRayDir = new Vector3(0f, 0f, -1f);
        if (Raycast(topRayOrigin, topRayDir, vertices, triangles, out var topHit, out var topNorm))
        {
            presets.Add(new DecalPresetPoint("Top", topHit, topNorm, 0f, baseWidth, EmbossTarget.Base));
        }
        else
        {
            presets.Add(new DecalPresetPoint("Top", new Vector3(xCenter, yCenter, maxZ), Vector3.UnitZ, 0f, baseWidth, EmbossTarget.Base));
        }

        // 2. Front (-Y direction at mid-height) - horizontal orientation
        var frontRayOrigin = new Vector3(xCenter, minY - 100f, zMid);
        var frontRayDir = new Vector3(0f, 1f, 0f);
        if (Raycast(frontRayOrigin, frontRayDir, vertices, triangles, out var frontHit, out var frontNorm))
        {
            presets.Add(new DecalPresetPoint("Front", frontHit, frontNorm, 0f, baseWidth, EmbossTarget.Base));
        }
        else
        {
            presets.Add(new DecalPresetPoint("Front", new Vector3(xCenter, minY, zMid), new Vector3(0f, -1f, 0f), 0f, baseWidth, EmbossTarget.Base));
        }

        // 3. Back (+Y direction at mid-height) - horizontal orientation
        var backRayOrigin = new Vector3(xCenter, maxY + 100f, zMid);
        var backRayDir = new Vector3(0f, -1f, 0f);
        if (Raycast(backRayOrigin, backRayDir, vertices, triangles, out var backHit, out var backNorm))
        {
            presets.Add(new DecalPresetPoint("Back", backHit, backNorm, 0f, baseWidth, EmbossTarget.Base));
        }
        else
        {
            presets.Add(new DecalPresetPoint("Back", new Vector3(xCenter, maxY, zMid), new Vector3(0f, 1f, 0f), 0f, baseWidth, EmbossTarget.Base));
        }

        return presets;
    }

    private static bool Raycast(Vector3 rayOrigin, Vector3 rayDir, Vector3[] vertices, int[] triangles, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        hitPoint = Vector3.Zero;
        hitNormal = -rayDir;
        float minT = float.MaxValue;
        bool found = false;
        Vector3 bestNormal = -rayDir;

        const float eps = 1e-7f;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            var v0 = vertices[triangles[i]];
            var v1 = vertices[triangles[i + 1]];
            var v2 = vertices[triangles[i + 2]];

            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var h = Vector3.Cross(rayDir, edge2);
            float a = Vector3.Dot(edge1, h);

            if (a > -eps && a < eps)
                continue;

            float f = 1.0f / a;
            var s = rayOrigin - v0;
            float u = f * Vector3.Dot(s, h);

            const float tol = 1e-4f;

            if (u < -tol || u > 1.0f + tol)
                continue;

            var q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(rayDir, q);

            if (v < -tol || u + v > 1.0f + tol)
                continue;

            float t = f * Vector3.Dot(edge2, q);

            if (t > eps && t < minT)
            {
                minT = t;
                found = true;

                var triCross = Vector3.Cross(edge1, edge2);
                if (triCross.LengthSquared() > 1e-8f)
                {
                    bestNormal = Vector3.Normalize(triCross);
                    if (Vector3.Dot(bestNormal, rayDir) > 0f)
                    {
                        bestNormal = -bestNormal;
                    }
                }
                else
                {
                    bestNormal = -rayDir;
                }
            }
        }

        if (found)
        {
            hitPoint = rayOrigin + minT * rayDir;
            hitNormal = bestNormal;
            return true;
        }

        return false;
    }
}
