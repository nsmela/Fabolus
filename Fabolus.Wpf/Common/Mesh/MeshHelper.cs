using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System.Windows.Media.Media3D;


namespace Fabolus.Wpf.Common.Mesh;

public static class MeshHelper {
    public static Vector3D VectorZero => new Vector3D(0, 0, 0);
    public static Transform3D TransformFromAxis(Vector3D axis, float angle) {
        var rotation = new AxisAngleRotation3D(axis, angle);
        var rotate = new RotateTransform3D(rotation);
        return new Transform3DGroup { Children = [rotate] };
    }

    public static Transform3D TransformFromAxis(Vector3 axis, float angle) =>
        TransformFromAxis(axis.ToVector3D(), angle);

    public static Transform3D TranslationFromAxis(double x, double y, double z) {
        var translate = new TranslateTransform3D(new Vector3D(x, y, z));
        return new Transform3DGroup { Children = [translate] };
    }

    public static Transform3D TransformEmpty => TransformFromAxis(VectorZero, 0.0f);



    public static Vector3[] CreateCircle(Vector3 origin, Vector3 normal, float radius, int segments) {
        if (segments < 3) { throw new ArgumentNullException("MeshHelper.CreateCircle requires at least 3 segments"); }

        normal.Normalize();
        var sectionAngle = (float)(2.0 * Math.PI / segments); //radians between points

        var start = new Vector3(radius, 0, 0);
        var current = new Vector3(radius, 0, 0);
        var next = Vector3.Zero;

        var positions = new List<Vector3> { current };

        for(var i = 1; i < segments; i++) {
            next.X = radius * (float)Math.Cos(i * sectionAngle);
            next.Y = radius * (float)Math.Sin(i * sectionAngle);
            current = next;
            positions.Add(current);
        }

        //rotate

        //transform
        return positions.Select(v => v + origin).ToArray();
    }
}