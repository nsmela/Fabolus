using g3;
using HelixToolkit.Wpf.SharpDX;
using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Linq;
using System.Net.WebSockets;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.AirChannel;

/// <summary>
/// Generates a curve for the angled channel on a 2d XY plane and then converts to a XYZ 3d path
/// </summary>
public static class AngledChannelCurve {

    /// <summary>
    /// Points are mapped onto a 2d arc and then rotated to match the input direction
    /// </summary>
    /// <param name="origin">The point on the mesh</param>
    /// <param name="normal">The normal of the point on the mesh</param>
    /// <param name="radius">The channel's diameter</param>
    /// <returns>List of Vector3d representing the path for the channel</returns>
    public static List<Vector3d> Curve(Vector3d origin, Vector3d normal, double tipLength, double radius) {

        var dir = Direction(normal);

        //creating cone points first
        var points = ConePath(dir, 1.5, tipLength);

        //adding the bend
        points.AddRange(Arc(points.Last(), dir, radius));

        //aligning curve to origin and normal
        var angleX = AngleDXY(normal);

        var rot = new Quaterniond(Vector3d.AxisZ, angleX);
        var curve = points.Select(p => new Vector3d {
                x = p.x,
                y = 0,
                z = p.y
            }).ToList();
        curve = curve.Select(v => MeshTransforms.Rotate(v, Vector3d.Zero, rot)).ToList(); //rotations
        return curve.Select(v => v + origin).ToList(); //translate
    }

    private static Vector2d Direction(Vector3d normal) {
        var zAngleRads = Vector3d.AxisZ.AngleR(normal);

        //normally, x = Cos(angle), y = Sin(angle), but our angle is a distance from ref
        //so we switch Cos and Sin calculations
        var dir = new Vector2d {
            y = Math.Cos(zAngleRads),
            x = Math.Sin(zAngleRads)
        };

        return dir.Normalized;
    }

    private static List<Vector2d> Arc(Vector2d origin, Vector2d direction, double radius) {
        var dir = direction.Normalized;
        var angle = Vector2d.AxisY.AngleD(dir);
        var start = 360 - angle;
        var end = 360.0;

        var segmentAngle = 15.0;
        var anglePerSegment = angle / segmentAngle;
        var resolution = 1 / anglePerSegment;

        var arc = new Arc2d(Vector2d.Zero, radius, start, end);
        var p0 = arc.P0;
        var points = new List<Vector2d>();
        for (double span = resolution; span <= 1.0; span += resolution) {
            points.Add(arc.SampleT(span));
        }
        return points.Select(p => p + origin - p0).ToList();
    }

    private static double AngleDXY(Vector3d vector) {
        var axis = Vector2d.AxisX;
        var v = new Vector2d { x = vector.x, y = vector.y };

        var angle = Vector2d.AngleD(axis, v);
        if (v.y <= 0) { angle *= -1; }
        return angle;
    }

    private static List<Vector2d> ConePath(Vector2d direction, double depth, double tipLength) =>
        new List<Vector2d> {
            (direction * - depth) ,
            Vector2d.Zero,
            (direction * tipLength),
        };

}
