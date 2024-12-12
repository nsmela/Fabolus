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
public static class AngledChannelCurve {
    private const int SEGMENTS = 8;

    public static List<Vector3d> Curve(Vector3d origin, Vector3d direction, double radius, double offset = 1.0) {
        var dir = direction.Normalized;
        var angle = 360.0 - Vector3d.AngleD(Vector3d.AxisZ, dir);

        //or use vectors to make the arc
        var arc = new Arc2d(new Vector2d(0, radius), radius, angle, 360.0);

        var points = new List<Vector3d>();
        points.Add(origin);
        double span = (1 / (double)SEGMENTS);
        for (double i = span; i <= 1.0; i+= span) {
            var point = arc.SampleT(i);
            points.Add(new Vector3d(
                origin.x + (dir.x *  point.x),
                origin.y + (dir.y * point.x),
                origin.z + point.y));
        }

        return points;
    }

    public static List<Vector3d> Curve2(Vector3d origin, Vector3d direction, double radius, double offset = 1.0) {
        var dir = direction.Normalized;
        //set reference axis to apply the upwards rotation
        //set it by finding the angle the normal is from Vector3d.AxisX
        var xAngle = Vector3d.AxisX.AngleD(dir);
        var rotation = new Quaterniond(Vector3d.AxisZ, xAngle - 90);
        var refAxis = MeshTransforms.Rotate(Vector3d.AxisX, Vector3d.Zero, rotation);

        //apply rotation to direction to make a new vector and multiple by distance
        //add to points

        var rot = new Quaterniond(refAxis, 15.0);
        var vec = MeshTransforms.Rotate(dir, Vector3d.Zero, rot) * radius / 2;
        var points = new List<Vector3d>();
        while (Vector3d.AxisZ.AngleD(vec) > 20) {
            points.Add(origin + vec);
            vec = MeshTransforms.Rotate(vec, Vector3d.Zero, rot) * radius / 2;
        }
        return points;
    }

    /// <summary>
    /// Points are mapped onto a 2d arc and then rotated to match the input direction
    /// </summary>
    /// <param name="origin">The point on the mesh</param>
    /// <param name="normal">The normal of the point on the mesh</param>
    /// <param name="radius">The channel's diameter</param>
    /// <returns>List of Vector3d representing the path for the channel</returns>
    public static List<Vector3d> FullCurve(Vector3d origin, Vector3d normal, double tipLength, double radius) {
        var zAngleRads = Vector3d.AxisZ.AngleR(normal);

        //normally, x = Cos(angle), y = Sin(angle), but our angle is a distance from ref
        //so we switch Cos and Sin calculations
        var dir = new Vector2d {
            y = Math.Cos(zAngleRads),
            x = Math.Sin(zAngleRads)
        };
        dir = dir.Normalized;

        //creating cone points first
        var depth = 2.0;
        var points = new List<Vector2d> {
            (dir * - depth) ,
            Vector2d.Zero,
            (dir * tipLength),
        };

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

    private static double AngleDXY(Vector3d vector) {
        var axis = Vector2d.AxisX;
        var v = new Vector2d { x = vector.x, y = vector.y };

        var angle = Vector2d.AngleD(axis, v);
        if (v.y <= 0) { angle *= -1; }
        return angle;
    }

    private static List<Vector2d> Arc2(Vector2d origin, Vector2d direction, double radius) {
        var circleCentre = origin + direction.Perp * radius * -1;
        var start = origin;
        var end = circleCentre + Vector2d.AxisX * radius;

        var arc = new Arc2d(
            vCenter: origin + direction.Perp * radius,
            vStart: origin,
            vEnd: circleCentre + Vector2d.AxisX * radius);
        arc.IsReversed = true;

        var resolution = 1 / (double)8;
        var points = new List<Vector2d>();
        for (double span = 0; span <= 1.0; span += resolution) {
            points.Add(arc.SampleT(span));
        }
        return points;
    }

    private static List<Vector2d> Arc(Vector2d origin, Vector2d direction, double radius) {
        var dir = direction.Normalized;
        var start = 360- Vector2d.AxisY.AngleD(dir);
        var end = 360.0;
        var centre = Vector2d.Zero;

        var arc = new Arc2d(centre, radius, start, end);
        var p0 = arc.P0;
        var resolution = 1 / (double)4;
        var points = new List<Vector2d>();
        for (double span = resolution; span <= 1.0; span += resolution) {
            points.Add(arc.SampleT(span));
        }
        return points.Select(p => p + origin - p0).ToList();
    }

    }
