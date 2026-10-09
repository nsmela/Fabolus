using GeometryEngine.Core.Geometry.Primitives;

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
    public static Rotation RotationFromZTo(Vector3 pullDirection)
    {
        var target = pullDirection.Normalize();
        var axis = Vector3.UnitZ.Cross(target);
        double dot = Vector3.UnitZ.Dot(target);

        // Antiparallel: the cross product vanishes, so any perpendicular axis will do. X, as it always
        // was - Rotation.Between would pick Y here, which turns the footprint the other way round.
        if (dot < -0.9999)
            return Rotation.FromAxisAngle(Direction.X, Math.PI);
        if (dot > 0.9999)
            return Rotation.Identity;

        return Rotation.FromQuaternion(1 + dot, axis.X, axis.Y, axis.Z).Value;
    }

    /// <summary>An orthonormal pair spanning the footprint plane, consistent with <see cref="RotationFromZTo"/>.</summary>
    public static (Vector3 U, Vector3 V) Basis(Vector3 pullDirection)
    {
        var rotation = RotationFromZTo(pullDirection);
        return (rotation.Apply(Vector3.UnitX), rotation.Apply(Vector3.UnitY));
    }

    /// <summary>Drops <paramref name="world"/> onto the footprint plane, in that plane's own coordinates.</summary>
    public static Vector2 ToPlane(Vector3 world, Vector3 pullDirection)
    {
        var local = RotationFromZTo(pullDirection).Inverse().Apply(world);
        return new Vector2(local.X, local.Y);
    }

    /// <summary>Lifts a footprint-plane point back into world space, at <paramref name="height"/> along the pull axis.</summary>
    public static Vector3 ToWorld(Vector2 plane, Vector3 pullDirection, double height = 0) =>
        RotationFromZTo(pullDirection).Apply(new Vector3(plane.X, plane.Y, height));

    /// <summary>How far along the pull axis <paramref name="world"/> sits.</summary>
    public static double Height(Vector3 world, Vector3 pullDirection) =>
        world.Dot(pullDirection.Normalize());
}
