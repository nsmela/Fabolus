using System;
using System.Collections.Generic;
using System.Numerics;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.Emboss;

/// <summary>
/// Calculates preset anchor points along the outer contour of a mould mesh at mid-height:
/// - 4 cardinal points (Front, Back, Left, Right)
/// - 2 points of maximum curvature (Curve 1, Curve 2)
/// </summary>
public static class MouldPresetPointsCalculator
{
    public static IReadOnlyList<DecalPresetPoint> Calculate(IGeometryEngine engine, IMesh mouldMesh)
    {
        if (mouldMesh == null)
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

        var center = new Vector3(xCenter, yCenter, zMid);
        var vertices = mouldMesh.Vertices ?? Array.Empty<Vector3>();
        var triangles = mouldMesh.Triangles ?? Array.Empty<int>();

        var presets = new List<DecalPresetPoint>(6);

        // 1. Front (-Y direction)
        var frontRayOrigin = new Vector3(xCenter, minY - 100f, zMid);
        var frontRayDir = new Vector3(0f, 1f, 0f);
        if (Raycast(frontRayOrigin, frontRayDir, vertices, triangles, out var frontHit, out var frontNorm))
        {
            presets.Add(new DecalPresetPoint("Front", frontHit, frontNorm, EmbossTarget.Mould));
        }
        else
        {
            presets.Add(new DecalPresetPoint("Front", new Vector3(xCenter, minY, zMid), new Vector3(0f, -1f, 0f), EmbossTarget.Mould));
        }

        // 2. Back (+Y direction)
        var backRayOrigin = new Vector3(xCenter, maxY + 100f, zMid);
        var backRayDir = new Vector3(0f, -1f, 0f);
        if (Raycast(backRayOrigin, backRayDir, vertices, triangles, out var backHit, out var backNorm))
        {
            presets.Add(new DecalPresetPoint("Back", backHit, backNorm, EmbossTarget.Mould));
        }
        else
        {
            presets.Add(new DecalPresetPoint("Back", new Vector3(xCenter, maxY, zMid), new Vector3(0f, 1f, 0f), EmbossTarget.Mould));
        }

        // 3. Left (-X direction)
        var leftRayOrigin = new Vector3(minX - 100f, yCenter, zMid);
        var leftRayDir = new Vector3(1f, 0f, 0f);
        if (Raycast(leftRayOrigin, leftRayDir, vertices, triangles, out var leftHit, out var leftNorm))
        {
            presets.Add(new DecalPresetPoint("Left", leftHit, leftNorm, EmbossTarget.Mould));
        }
        else
        {
            presets.Add(new DecalPresetPoint("Left", new Vector3(minX, yCenter, zMid), new Vector3(-1f, 0f, 0f), EmbossTarget.Mould));
        }

        // 4. Right (+X direction)
        var rightRayOrigin = new Vector3(maxX + 100f, yCenter, zMid);
        var rightRayDir = new Vector3(-1f, 0f, 0f);
        if (Raycast(rightRayOrigin, rightRayDir, vertices, triangles, out var rightHit, out var rightNorm))
        {
            presets.Add(new DecalPresetPoint("Right", rightHit, rightNorm, EmbossTarget.Mould));
        }
        else
        {
            presets.Add(new DecalPresetPoint("Right", new Vector3(maxX, yCenter, zMid), new Vector3(1f, 0f, 0f), EmbossTarget.Mould));
        }

        // 5 & 6. Analyze 2D contour to find two strongest curves
        CalculateCurvePresets(vertices, triangles, xCenter, yCenter, zMid, minX, maxX, minY, maxY, out var curve1, out var curve2);
        presets.Add(curve1);
        presets.Add(curve2);

        return presets;
    }

    private static void CalculateCurvePresets(
        Vector3[] vertices,
        int[] triangles,
        float xCenter,
        float yCenter,
        float zMid,
        float minX,
        float maxX,
        float minY,
        float maxY,
        out DecalPresetPoint curve1,
        out DecalPresetPoint curve2)
    {
        const int samples = 72; // 5-degree increments
        var points = new List<Vector3>(samples);
        var normals = new List<Vector3>(samples);

        float radius = MathF.Max(maxX - minX, maxY - minY) * 0.5f + 100f;

        for (int i = 0; i < samples; i++)
        {
            float angle = i * (MathF.PI * 2f / samples);
            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);

            var rayOrigin = new Vector3(xCenter + cos * radius, yCenter + sin * radius, zMid);
            var rayDir = new Vector3(-cos, -sin, 0f);

            if (Raycast(rayOrigin, rayDir, vertices, triangles, out var hit, out var norm))
            {
                points.Add(hit);
                normals.Add(norm);
            }
        }

        if (points.Count >= 8)
        {
            int n = points.Count;
            var curvatures = new float[n];

            for (int i = 0; i < n; i++)
            {
                int prev = (i - 1 + n) % n;
                int next = (i + 1) % n;

                var vIn = Vector2.Normalize(new Vector2(points[i].X - points[prev].X, points[i].Y - points[prev].Y));
                var vOut = Vector2.Normalize(new Vector2(points[next].X - points[i].X, points[next].Y - points[i].Y));

                // Curvature measure based on turning angle: 1 - dot
                float dot = Math.Clamp(Vector2.Dot(vIn, vOut), -1f, 1f);
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

            // Find first maximum (Curve 1)
            int bestIdx1 = 0;
            float maxCurv1 = -1f;
            for (int i = 0; i < n; i++)
            {
                if (smoothed[i] > maxCurv1)
                {
                    maxCurv1 = smoothed[i];
                    bestIdx1 = i;
                }
            }

            // Find second maximum (Curve 2) separated by at least 1/6th of contour
            int minSep = Math.Max(3, n / 6);
            int bestIdx2 = (bestIdx1 + n / 2) % n;
            float maxCurv2 = -1f;

            for (int i = 0; i < n; i++)
            {
                int dist = Math.Min(Math.Abs(i - bestIdx1), n - Math.Abs(i - bestIdx1));
                if (dist >= minSep && smoothed[i] > maxCurv2)
                {
                    maxCurv2 = smoothed[i];
                    bestIdx2 = i;
                }
            }

            curve1 = new DecalPresetPoint("Curve 1", points[bestIdx1], normals[bestIdx1], EmbossTarget.Mould);
            curve2 = new DecalPresetPoint("Curve 2", points[bestIdx2], normals[bestIdx2], EmbossTarget.Mould);
            return;
        }

        // Fallback for mocks/primitives where raycast returns few/no hits
        var diag1Pos = new Vector3(maxX, maxY, zMid);
        var diag1Norm = Vector3.Normalize(new Vector3(1f, 1f, 0f));
        var diag2Pos = new Vector3(minX, minY, zMid);
        var diag2Norm = Vector3.Normalize(new Vector3(-1f, -1f, 0f));

        curve1 = new DecalPresetPoint("Curve 1", diag1Pos, diag1Norm, EmbossTarget.Mould);
        curve2 = new DecalPresetPoint("Curve 2", diag2Pos, diag2Norm, EmbossTarget.Mould);
    }

    /// <summary>
    /// Möller–Trumbore ray-triangle intersection algorithm.
    /// Finds the closest positive-distance intersection along the ray.
    /// </summary>
    private static bool Raycast(
        Vector3 rayOrigin,
        Vector3 rayDir,
        Vector3[] vertices,
        int[] triangles,
        out Vector3 hitPoint,
        out Vector3 hitNormal)
    {
        hitPoint = Vector3.Zero;
        hitNormal = Vector3.UnitZ;

        if (vertices.Length == 0 || triangles.Length == 0)
            return false;

        float minT = float.MaxValue;
        bool found = false;
        Vector3 bestNormal = Vector3.UnitZ;

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
                    // Ensure normal faces against ray direction (outward)
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
