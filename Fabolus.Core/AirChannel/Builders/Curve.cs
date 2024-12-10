using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.AirChannel.Builders;
public static class Curve {

    public static List<Vector3d> AddBend(Vector3d origin, double startAngle, double tubeRadius, double radiusOffset) {
        const double segments = 16;

        var arc = new Arc2d(Vector2d.Zero, tubeRadius + radiusOffset, startAngle, 0.0);

        var points = new List<Vector3d>();
        double span = (double)(1 / segments);
        for(int i = 1; i <= segments; i++) {
            var point = arc.SampleT(span * i);
            points.Add(new Vector3d(
                origin.x + point.x,
                origin.y + point.x,
                origin.z + point.y));
        }

        return points;
    }
}
