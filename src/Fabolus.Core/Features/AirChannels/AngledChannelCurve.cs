using System.Numerics;

namespace Fabolus.Core.Features.AirChannels;

/// <summary>
/// Generates a curve for the angled channel on a 2d XY plane and then converts to a XYZ 3d path.
/// </summary>
public static class AngledChannelCurve
{
    /// <summary>
    /// Points are mapped onto a 2d arc and then rotated to match the input direction
    /// </summary>
    /// <param name="curveOrigin">The point on the mesh</param>
    /// <param name="curveNormal">The normal of the point on the mesh</param>
    /// <param name="tipLength">The tip length</param>
    /// <param name="radius">The channel's diameter</param>
    /// <returns>List of Vector3 representing the path for the channel</returns>
    public static List<Vector3> Curve(Vector3 curveOrigin, Vector3 curveNormal, double tipLength, double radius)
    {
        var normal = Vector3.Normalize(curveNormal);
        var dir = Direction(normal);

        // creating cone points first
        var points = ConePath(dir, 1.5f, (float)tipLength);

        // adding the bend
        points.AddRange(Arc(points.Last(), dir, (float)radius));

        // aligning curve to origin and normal
        var angleXDeg = AngleDXY(normal);
        var angleXRad = (float)(angleXDeg * Math.PI / 180.0);

        var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angleXRad);

        var curve = points.Select(p => new Vector3(p.X, 0, p.Y)).ToList();

        // rotations
        curve = curve
            .Select(v => Vector3.Transform(v, rot) + curveOrigin)
            .ToList();

        return curve;
    }

    private static Vector2 Direction(Vector3 normal)
    {
        var zAxis = Vector3.UnitZ;
        var dot = Math.Clamp(Vector3.Dot(zAxis, normal), -1f, 1f);
        var zAngleRads = Math.Acos(dot);

        // normally, x = Cos(angle), y = Sin(angle), but our angle is a distance from ref
        // so we switch Cos and Sin calculations
        var dir = new Vector2(
            (float)Math.Sin(zAngleRads),
            (float)Math.Cos(zAngleRads)
        );

        return Vector2.Normalize(dir);
    }

    private static List<Vector2> Arc(Vector2 origin, Vector2 direction, float radius)
    {
        var dir = Vector2.Normalize(direction);
        var axisY = Vector2.UnitY;

        var dot = Math.Clamp(Vector2.Dot(axisY, dir), -1f, 1f);
        var angleRad = Math.Acos(dot);
        var angleDeg = angleRad * 180.0 / Math.PI;

        var start = 360.0 - angleDeg;
        var end = 360.0;

        var segmentAngle = 15.0;
        var anglePerSegment = angleDeg / segmentAngle;
        // avoid division by zero
        var resolution = anglePerSegment > 0 ? 1.0 / anglePerSegment : 1.0;

        var p0 = new Vector2(
            (float)(radius * Math.Cos(start * Math.PI / 180.0)),
            (float)(radius * Math.Sin(start * Math.PI / 180.0))
        );

        var points = new List<Vector2>();
        for (double span = resolution; span <= 1.0; span += resolution)
        {
            var tAngleDeg = start + (end - start) * span;
            var tAngleRad = tAngleDeg * Math.PI / 180.0;
            var p = new Vector2(
                (float)(radius * Math.Cos(tAngleRad)),
                (float)(radius * Math.Sin(tAngleRad))
            );
            points.Add(p);
        }

        return points.Select(p => p + origin - p0).ToList();
    }

    private static double AngleDXY(Vector3 vector)
    {
        var axis = Vector2.UnitX;
        var v = new Vector2(vector.X, vector.Y);

        // Handle zero vector
        if (v.LengthSquared() < 1e-8f) return 0;

        var vNorm = Vector2.Normalize(v);
        var dot = Math.Clamp(Vector2.Dot(axis, vNorm), -1f, 1f);
        var angleRad = Math.Acos(dot);
        var angleDeg = angleRad * 180.0 / Math.PI;

        if (v.Y <= 0) { angleDeg *= -1; }
        return angleDeg;
    }

    private static List<Vector2> ConePath(Vector2 direction, float depth, float tipLength) =>
        new List<Vector2>
        {
            (direction * -depth),
            Vector2.Zero,
            (direction * tipLength)
        };
}
