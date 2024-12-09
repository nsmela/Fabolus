﻿using g3;
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
        public double BottomDiameter { get; set; } = 1.0;
        public Vector3d Direction { get; set; } = Vector3d.AxisZ;
        public double TipLength { get; set; } = 10.0;

    }

    //ref: https://github.com/gradientspace/geometry3Sharp/blob/8f185f19a96966237ef631d97da567f380e10b6b/mesh_generators/GenCylGenerators.cs
    //Circle3d https://github.com/gradientspace/geometry3Sharp/blob/8f185f19a96966237ef631d97da567f380e10b6b/shapes3/Circle3.cs#L6
    //EdgeLoop https://github.com/gradientspace/geometry3Sharp/blob/8f185f19a96966237ef631d97da567f380e10b6b/mesh/EdgeLoop.cs#L12

    public static AngledSettings SetDiameters(this AngledSettings settings, double start, double top) =>
        settings = settings with { BottomDiameter = start, Diameter = (float)top };

    internal static AngledSettings SetDirection(this AngledSettings settings, Vector3d normal) =>
        settings = settings with { Direction = normal };
    public static AngledSettings SetDirection(this AngledSettings settings, double x, double y, double z) =>
        settings.SetDirection(new Vector3d(x, y, z));
    public static AngledSettings SetLength(this AngledSettings settings, double length) =>
        settings = settings with { Height = (float)length };
    public static AngledSettings SetTipLength(this AngledSettings settings, double tipLength) =>
        settings with { TipLength = tipLength };

    public static DMesh3 Build(this AngledSettings settings) {
        var mesh = new MeshEditor(new DMesh3());

        var direction = settings.Direction.Normalized;

        var coneEndPoint = settings.Origin + (settings.Direction * settings.TipLength); //end of cone, start of bend
        var angle = 360 - settings.Direction.AngleD(Vector3d.AxisZ);
        var tubeRadius = settings.Diameter / 2;

        //create path
        var path = new List<Vector3d>();
        path.Add(settings.Origin);
        path.Add(coneEndPoint);
        path.AddRange(AddBend(coneEndPoint, settings.Direction, angle, tubeRadius, 1.0)); //list of points along a bend upwards

        //adding straight channel upwards
        var lastPoint = path.Last();
        path.Add(new Vector3d(lastPoint.x, lastPoint.y, settings.Height));

        //create tube
        var curve = new DCurve3(path, false);
        var shape = Polygon2d.MakeCircle(tubeRadius, SEGMENTS);
        var tube = new TubeGenerator(curve, shape);
        tube.Generate();
        mesh.AppendMesh(tube.MakeDMesh());
        var result = mesh.Mesh;
        MeshNormals.QuickCompute(result);

        return result;
        
    }

    public static List<Vector3d> AddBend(Vector3d origin, Vector3d direction, double startAngle, double tubeRadius, double radiusOffset) {
        var radius = radiusOffset + tubeRadius;
        var arc = new Arc2d(Vector2d.Zero, radius, startAngle, 0.0);

        var points = new List<Vector3d>();
        var dir = direction.Normalized;
        double span = (1 / (double)SEGMENTS);
        for (int i = 1; i <= SEGMENTS; i++) {
            var point = arc.SampleT(span * i);
            points.Add(new Vector3d(
                origin.x + (dir.x * point.x),
                origin.y + (dir.y * point.x),
                origin.z + radius + point.y));
        }

        return points;
    }

    private static DMesh3 AddCone(Vector3d axis, Vector3d origin, double length, double radius) {
        var builder = new CappedCylinderGenerator {
            BaseRadius = 1.0f,
            TopRadius = (float)radius,
            Height = (float)length,
        };

        builder.Generate();
        var mesh = builder.MakeDMesh();

        //transform it
        var rotation = new Quaterniond(Vector3d.AxisY, axis);
        MeshTransforms.Rotate(mesh, Vector3d.Zero, rotation);
        MeshTransforms.Translate(mesh, origin);
        
        return mesh;
    }

    public static DMesh3 Generate(this AngledSettings settings) {
        var origin = settings.Origin;
        var normal = settings.Direction;

        var (curve, radii) = GetConeCurve(settings);

        return ToDebugMesh(curve.ToArray(), radii.ToArray());

        //create on the xz plane (z is up) 
        //make a circle for each section
        //use circles to make edge loops
        //stitch edge loops together
        //rotate mesh to the correct orientation

        var edges = new List<EdgeLoop>();

        var count = curve.Count();
        curve.Add(curve.Last() + Vector3d.AxisZ); //adds a last one to prevent indexing issues

        //create each loop
        var loops = new List<int[]>(); //loops are an array of vertices indices
        var mesh = new MeshEditor(new DMesh3()); //result mesh

        //add vertices for the loops
        //store vertices indices as int[] to stitch later
        for(int i = 0; i < count; i++) {
            var point = curve[i];
            var radius = radii[i];
            var direction = (curve[i + 1] - point).Normalized;

            //frame?
            var frame = new Frame3f(point, direction);

            var circle = new Circle3d(frame, radius);
            var points = new List<Vector3d>();
            var span = 1 / (double)SEGMENTS;
            for(int j = 1; j <= SEGMENTS; j++) {
                points.Add(circle.SampleT(span * j));
            }

            //create loop
            var indices = new List<int>();
            foreach(var p in points) {
                indices.Add(mesh.Mesh.AppendVertex(p));
            }

            loops.Add(indices.ToArray());
        }


        //connect edge loops
        for(int i = 0; i + 1 < loops.Count(); i++) {
            mesh.StitchLoop(loops[i], loops[i + 1]);
        }

        //close ends
        //first loop is closed
        var index = mesh.Mesh.AppendVertex(origin);
        mesh.AddTriangleFan_OrderedVertexLoop(index, loops[0]);


        //return mesh
        MeshNormals.QuickCompute(mesh.Mesh);
        return mesh.Mesh;
    }

    private static (List<Vector3d> points, List<double> radii) GetConeCurve(AngledSettings settings) {
        var angle = Vector3d.AngleD(Vector3d.AxisZ, settings.Direction);
        var direction = MeshTransforms.Rotate(
            Vector3d.Zero,
            Vector3d.AxisX,
            new Quaterniond(Vector3d.AxisY, angle));

        List<Vector3d> points = [
            Vector3d.Zero - direction * settings.Depth,
            Vector3d.Zero,
            Vector3d.Zero + direction * settings.TipLength];

        List<double> radii = [
            settings.BottomDiameter / 2,
            settings.BottomDiameter / 2,
            settings.Diameter / 2];

        return (points, radii);
    }

    private static DMesh3 ToDebugMesh(Vector3d[] points, double[] radii) {
        var mesh = new MeshEditor(new DMesh3());

        for (int i = 0; i < points.Length; i++) {
            var builder = new Sphere3Generator_NormalizedCube {
                Radius = radii[i]
            };
            var sphere = builder.MakeDMesh();
            MeshTransforms.Translate(sphere, points[i]);

            mesh.AppendMesh(sphere);
        }

        return mesh.Mesh;
    }
}
