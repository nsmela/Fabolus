using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.AirChannel;
public static class AngledChannelCurve {
    private const int SEGMENTS = 16;

    public static List<Vector3d> Curve(Vector3d origin, Vector3d direction, double radius, double offset = 1.0) {
        var dir = direction.Normalized;
        var angle = 360.0 - Vector3d.AngleD(Vector3d.AxisZ, dir);

        var arc = new Arc2d(new Vector2d(0, radius + offset), radius + offset, angle, 360.0);

        var points = new List<Vector3d>();
        points.Add(origin);
        double span = (1 / (double)SEGMENTS);
        for (int i = 0; i <= (double)SEGMENTS; i++) {
            var point = arc.SampleT(span * i);
            points.Add(new Vector3d(
                origin.x + (dir.x * point.x),
                origin.y + (dir.y * point.x),
                origin.z + point.y));
        }

        return points;
    }
}
