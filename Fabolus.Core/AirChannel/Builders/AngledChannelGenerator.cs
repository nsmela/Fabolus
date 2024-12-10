using g3;
using SharpDX.DirectWrite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.AirChannel.Builders;
public static class AngledChannelGenerator {
    private const int SEGMENTS = 16;
    public record AngledSettings : ChannelGenerator.Settings {
        public AngledSettings() { }
        public float BottomDiameter { get; set; } = 1.0f;
        public Vector3d Direction { get; set; } = Vector3d.AxisZ;

    }

    //ref: https://github.com/gradientspace/geometry3Sharp/blob/8f185f19a96966237ef631d97da567f380e10b6b/mesh_generators/GenCylGenerators.cs

    public static AngledSettings SetDiameters(this AngledSettings settings, double start, double top) =>
        settings = settings with { BottomDiameter = (float)start, Diameter = (float)top };

    internal static AngledSettings SetDirection(this AngledSettings settings, Vector3d normal) =>
        settings = settings with { Direction = normal };
    public static AngledSettings SetDirection(this AngledSettings settings, double x, double y, double z) =>
        settings.SetDirection(new Vector3d(x, y, z));
    public static AngledSettings SetLength(this AngledSettings settings, double length) =>
        settings = settings with { Height = (float)length };

    public static DMesh3 Build(this AngledSettings settings) {
        var mesh = new MeshEditor(new DMesh3());

        mesh.AppendMesh(AddCone(
            axis: settings.Direction, 
            origin: settings.Origin, 
            length: settings.Height, 
            radius: settings.Diameter/2));

        //create path

        //create tube
        //var tube = new TubeGenerator(curve, shape);
        // tube.Generate();
        //mesh.AppendMesh(tube.MakeDMesh());

        return mesh.Mesh;
    }

    public static List<Vector3d> AddBend(Vector3d origin, double startAngle, double tubeRadius, double radiusOffset) {
        var arc = new Arc2d(Vector2d.Zero, tubeRadius + radiusOffset, startAngle, 0.0);

        var points = new List<Vector3d>();
        double span = (double)(1 / SEGMENTS);
        for (int i = 1; i <= SEGMENTS; i++) {
            var point = arc.SampleT(span * i);
            points.Add(new Vector3d(
                origin.x + point.x,
                origin.y + point.x,
                origin.z + point.y));
        }

        return points;
    }

    private static DMesh3 AddCone(Vector3d axis, Vector3d origin, double length, double radius) {
        var cone = new OpenCylinderGenerator {
            BaseRadius = 1.0f,
            TopRadius = (float)radius,
            Height = (float)length,
        };

        cone.Generate();
        return cone.MakeDMesh();
    }
}
