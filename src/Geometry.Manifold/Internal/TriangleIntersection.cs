using System.Numerics;

namespace GeometryManifold.Internal;

/// <summary>
/// Exact triangle-triangle overlap, standing in for MeshLib's SelfIntersections.getFaces. The
/// mesh-repair and validation features report a count of self-intersecting faces, and Manifold
/// offers nothing equivalent - its booleans resolve intersections rather than finding them.
/// </summary>
internal static class TriangleIntersection
{
    /// <summary>
    /// Vertices this close to a plane count as on it. Loose enough that two triangles meeting
    /// exactly along a shared edge are not reported as crossing it.
    /// </summary>
    private const float PlaneTolerance = 1e-6f;

    /// <summary>
    /// True when the two triangles pass through each other. Triangles that merely touch - sharing
    /// an edge or a vertex, or lying in the same plane - are not intersections.
    /// </summary>
    /// <remarks>
    /// Moller's interval test: project both triangles onto the line where their planes meet and
    /// see whether the resulting intervals overlap. Coplanar pairs are deliberately ignored; the
    /// callers only care about surfaces that actually pierce one another.
    /// </remarks>
    public static bool Intersects(
        Vector3 a0, Vector3 a1, Vector3 a2,
        Vector3 b0, Vector3 b1, Vector3 b2)
    {
        var normalA = Vector3.Cross(a1 - a0, a2 - a0);
        if (normalA.LengthSquared() < 1e-20f) return false; // Degenerate: nothing to pierce.
        float offsetA = -Vector3.Dot(normalA, a0);

        float db0 = Vector3.Dot(normalA, b0) + offsetA;
        float db1 = Vector3.Dot(normalA, b1) + offsetA;
        float db2 = Vector3.Dot(normalA, b2) + offsetA;

        float scaleA = normalA.Length();
        if (MathF.Abs(db0) < PlaneTolerance * scaleA) db0 = 0;
        if (MathF.Abs(db1) < PlaneTolerance * scaleA) db1 = 0;
        if (MathF.Abs(db2) < PlaneTolerance * scaleA) db2 = 0;

        if ((db0 > 0 && db1 > 0 && db2 > 0) || (db0 < 0 && db1 < 0 && db2 < 0)) return false;
        if (db0 == 0 && db1 == 0 && db2 == 0) return false; // Coplanar.

        var normalB = Vector3.Cross(b1 - b0, b2 - b0);
        if (normalB.LengthSquared() < 1e-20f) return false;
        float offsetB = -Vector3.Dot(normalB, b0);

        float da0 = Vector3.Dot(normalB, a0) + offsetB;
        float da1 = Vector3.Dot(normalB, a1) + offsetB;
        float da2 = Vector3.Dot(normalB, a2) + offsetB;

        float scaleB = normalB.Length();
        if (MathF.Abs(da0) < PlaneTolerance * scaleB) da0 = 0;
        if (MathF.Abs(da1) < PlaneTolerance * scaleB) da1 = 0;
        if (MathF.Abs(da2) < PlaneTolerance * scaleB) da2 = 0;

        if ((da0 > 0 && da1 > 0 && da2 > 0) || (da0 < 0 && da1 < 0 && da2 < 0)) return false;

        // Project onto the dominant axis of the line where the two planes meet.
        var direction = Vector3.Cross(normalA, normalB);
        float x = MathF.Abs(direction.X);
        float y = MathF.Abs(direction.Y);
        float z = MathF.Abs(direction.Z);
        int axis = x > y ? (x > z ? 0 : 2) : (y > z ? 1 : 2);

        if (!Interval(Axis(a0, axis), Axis(a1, axis), Axis(a2, axis), da0, da1, da2, out float aMin, out float aMax)) return false;
        if (!Interval(Axis(b0, axis), Axis(b1, axis), Axis(b2, axis), db0, db1, db2, out float bMin, out float bMax)) return false;

        // Touching endpoints are a shared boundary, not a crossing.
        return aMax > bMin + PlaneTolerance && bMax > aMin + PlaneTolerance;
    }

    private static float Axis(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    /// <summary>
    /// The span of the triangle's projection between the two vertices that straddle the other
    /// plane. Returns false when the triangle only touches that plane.
    /// </summary>
    private static bool Interval(float p0, float p1, float p2, float d0, float d1, float d2, out float min, out float max)
    {
        min = 0;
        max = 0;

        // Reorder so the odd vertex out - the one alone on its side - is first.
        if (d0 * d1 > 0) { (p0, p2) = (p2, p0); (d0, d2) = (d2, d0); }
        else if (d0 * d2 > 0) { (p0, p1) = (p1, p0); (d0, d1) = (d1, d0); }
        else if (d1 * d2 > 0 || d0 != 0) { /* d0 is already the odd one out. */ }
        else if (d1 != 0) { (p0, p1) = (p1, p0); (d0, d1) = (d1, d0); }
        else if (d2 != 0) { (p0, p2) = (p2, p0); (d0, d2) = (d2, d0); }
        else return false;

        float denominator1 = d0 - d1;
        float denominator2 = d0 - d2;
        if (MathF.Abs(denominator1) < 1e-20f || MathF.Abs(denominator2) < 1e-20f) return false;

        float t1 = p0 + (p1 - p0) * d0 / denominator1;
        float t2 = p0 + (p2 - p0) * d0 / denominator2;

        min = MathF.Min(t1, t2);
        max = MathF.Max(t1, t2);
        return true;
    }
}
