using g3;
using HelixToolkit.Wpf.SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
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

    private static Vector2d Perpendicular(Vector3d vector) {
        var angle = vector.AngleR(Vector3d.AxisZ) - (Math.PI / 2.0);
        return new Vector2d {
            x = (float)Math.Cos(angle),
            y = (float)Math.Sin(angle),
        };
    }
    
}
