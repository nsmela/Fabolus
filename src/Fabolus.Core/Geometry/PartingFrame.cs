using System.Numerics;

namespace Fabolus.Core.Geometry;

/// <summary>
/// The one definition of the plane a parting operation works in: the plane through the origin whose
/// normal is the pull direction. Everything in the parting pipeline that flattens 3D geometry to 2D
/// or lifts it back - the parting line's footprint, the flange triangulation, the outer contour the
/// flange sweeps to - has to agree on that plane, or geometry built by one stage lands somewhere else
/// for the next.
///
/// <para>
/// It previously did not agree. The flange was built in a frame derived from the plane normal it was
/// handed, while the outer contour was hard-coded as a world XZ box and the caller flattened it with
/// a literal <c>(v.X, v.Z)</c>. Those happen to match for a +Y pull and silently diverge for any
/// other, which is why the pipeline only ever accepted +Y. Routing both through here is what lets the
/// pull direction be arbitrary.
/// </para>
/// </summary>
public static class PartingFrame
{
    /// <summary>
    /// Rotation mapping world +Z onto <paramref name="pullDirection"/>. Local +Z is therefore the pull
    /// axis, and local XY is the footprint plane.
    /// </summary>
    public static Quaternion RotationFromZTo(Vector3 pullDirection)
    {
        var target = Vector3.Normalize(pullDirection);
        var axis = Vector3.Cross(Vector3.UnitZ, target);
        float dot = Vector3.Dot(Vector3.UnitZ, target);

        // Antiparallel: the cross product vanishes, so any perpendicular axis will do.
        if (dot < -0.9999f)
            return Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI);
        if (dot > 0.9999f)
            return Quaternion.Identity;

        return Quaternion.Normalize(new Quaternion(axis, 1 + dot));
    }

    /// <summary>An orthonormal pair spanning the footprint plane, consistent with <see cref="RotationFromZTo"/>.</summary>
    public static (Vector3 U, Vector3 V) Basis(Vector3 pullDirection)
    {
        var rotation = RotationFromZTo(pullDirection);
        return (Vector3.Transform(Vector3.UnitX, rotation), Vector3.Transform(Vector3.UnitY, rotation));
    }

    /// <summary>Drops <paramref name="world"/> onto the footprint plane, in that plane's own coordinates.</summary>
    public static Vector2 ToPlane(Vector3 world, Vector3 pullDirection)
    {
        var local = Vector3.Transform(world, Quaternion.Inverse(RotationFromZTo(pullDirection)));
        return new Vector2(local.X, local.Y);
    }

    /// <summary>Lifts a footprint-plane point back into world space, at <paramref name="height"/> along the pull axis.</summary>
    public static Vector3 ToWorld(Vector2 plane, Vector3 pullDirection, float height = 0f) =>
        Vector3.Transform(new Vector3(plane.X, plane.Y, height), RotationFromZTo(pullDirection));

    /// <summary>How far along the pull axis <paramref name="world"/> sits.</summary>
    public static float Height(Vector3 world, Vector3 pullDirection) =>
        Vector3.Dot(world, Vector3.Normalize(pullDirection));
}
