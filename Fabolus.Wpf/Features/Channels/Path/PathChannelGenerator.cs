using Fabolus.Wpf.Common.Mesh;
using HelixToolkit.Wpf.SharpDX;
using SharpDX;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using MeshGeometry3D = HelixToolkit.Wpf.SharpDX.MeshGeometry3D;

namespace Fabolus.Wpf.Features.Channels.Path;
public sealed record PathChannelGenerator : ChannelGenerator {
    private float Depth { get; set; }
    private float LowerHeight { get; set; }
    private Vector3[] Path { get; set; }
    private float Offset { get; set; }
    private float LowerRadius { get; set; }
    private float TopHeight { get; set; }
    private float UpperRadius { get; set; }
    private float UpperHeight { get; set; }

    private PathChannelGenerator() { }
    public static PathChannelGenerator New() => new PathChannelGenerator();
    public PathChannelGenerator WithDepth(float depth) => this with { Depth = depth };
    public PathChannelGenerator WithHeight(float lowerHeight, float upperHeight, float topHeight) => 
        this with { LowerHeight = lowerHeight, UpperHeight = upperHeight, TopHeight = topHeight };
    public PathChannelGenerator WithPath(Vector3[] path) => this with { Path = path };
    public PathChannelGenerator WithOffet(float offset) => this with { Offset = offset };
    public PathChannelGenerator WithRadius(float lowerRadius, float upperRadius) =>
        this with { LowerRadius = lowerRadius, UpperRadius = upperRadius };

    public override MeshGeometry3D Build() {
        if (Path.Length == 0) { throw new Exception("PathChannel requires one or more points to generate"); }
        if (Path.Length == 1) { return Sphere(Path[0], 3.0f); }
        var mesh = new MeshBuilder();

        var lowerPoints = GetPathOutline(Path, LowerRadius, -Depth);
        var midLowerPoints = ExtrudePoints(lowerPoints, Depth + UpperHeight);
        var upperPoints = GetPathOutline(Path, UpperRadius, UpperHeight);
        var topPoints = upperPoints.Select(v => new Vector3(v.X, v.Y, TopHeight)).ToArray();

        CapContour(ref mesh, Path, lowerPoints, true);
        JoinPoints(ref mesh, lowerPoints, midLowerPoints);
        JoinPoints(ref mesh, midLowerPoints, upperPoints);
        JoinPoints(ref mesh, upperPoints, topPoints);
        CapContour(ref mesh, Path, topPoints);

        return mesh.ToMeshGeometry3D();
    }

    public MeshGeometry3D Sphere(Vector3 origin, float radius) {
        var mesh = new MeshBuilder();
        mesh.AddSphere(origin, radius, SEGMENTS);
        return mesh.ToMeshGeometry3D();
    }

    private static void CapContour(ref MeshBuilder mesh, Vector3[] path, Vector3[] points, bool reverse = false) {
        var pathCount = path.Length;
        var arcCount = SEGMENTS / 2;

        int left = 0; //starts at 0
        int endArc = left + pathCount - 1;
        int right = endArc + arcCount + 1;
        int startArc = right + pathCount - 1;

        //taking the points relevant to the component
        var leftPoints = new List<Vector3>();
        for (int i = left; i <= endArc; i++) { leftPoints.Add(points[i]); }

        var endPoints = new List<Vector3>();
        for (int i = endArc; i <= right; i++) { endPoints.Add(points[i]); }

        var rightPoints = new List<Vector3>();
        for (int i = right; i <= startArc; i++) { rightPoints.Add(points[i]); }

        var startPoints = new List<Vector3>();
        for (int i = startArc; i < points.Length; i++) { startPoints.Add(points[i]); }
        startPoints.Add(points[0]); //first point also included

        //flips the normals to point downward
        if (reverse) {
            endPoints.Reverse();
            startPoints.Reverse();
        }
        try {
            mesh.AddPolygon(endPoints);
            mesh.AddPolygon(startPoints);
        }catch(Exception ex) {
            var text = ex.InnerException;
        }
        for (int i = 0; i < pathCount - 1; i++) {
            var p1 = points[startArc - i];
            var p2 = points[startArc - i - 1];
            var p3 = points[left + i];
            var p4 = points[left + i + 1];

            if (reverse) { //flip the normals
                mesh.AddTriangle(p4, p3, p1);
                mesh.AddTriangle(p1, p2, p4);
            } else {
                mesh.AddTriangle(p1, p3, p4);
                mesh.AddTriangle(p4, p2, p1);
            }
        }

    }

    private Vector3[] ExtrudePoints(Vector3[] points, float height) {
        var verticalOffset = Vector3.UnitZ * height;
        var upperPoints = new List<Vector3>();
        points.ToList().ForEach(p => upperPoints.Add(p + verticalOffset));

        return upperPoints.ToArray();
    }

    private void JoinPoints(ref MeshBuilder mesh, Vector3[] lowerPoints, Vector3[] upperPoints) {
        if (lowerPoints is null || lowerPoints.Length < 2) { return; }
        if (upperPoints is null || upperPoints.Length < 2) { return; }

        for (int i = 0; i < lowerPoints.Length; i++) {
            var next = (i + 1 < lowerPoints.Length) ? i + 1 : 0;
            var p1 = upperPoints[i];
            var p2 = lowerPoints[i]; 
            var p3 = lowerPoints[next];
            var p4 = upperPoints[next];
            mesh.AddQuad(p1, p2, p3, p4);
        }
    }

    private Vector3[] GetPathOutline(Vector3[] vectors, float radius, float verticalOffset = 0.0f) {
        if (vectors.Length == 1) { return MeshHelper.CreateCircle(vectors[0], Vector3.UnitZ, radius, SEGMENTS); }

        var path = vectors.Select(v => new Point3D(v.X, v.Y, v.Z)).ToList();
        var direction = new Vector3D();
        var horizontalOffset = new Vector3D();
        List<Point3D> left = new(), right = new();

        //generate the straight paths on both sides
        for (int i = 0; i < path.Count; i++) {
            if (i + 1 < path.Count) {
                direction = GetDirection(path[i], path[i + 1]);
            }

            horizontalOffset = Vector3D.CrossProduct(direction, new Vector3D(0, 0, 1));
            left.Add(path[i] + horizontalOffset * radius);
            right.Add(path[i] - horizontalOffset * radius);
        }

        //create endcaps
        int last = left.Count - 1;
        var startArcPoints = ArcPoints(left[0], right[0]);
        var endArcPoints = ArcPoints(right[last], left[last]);

        //collect all points
        var points = new List<Point3D>(); //store the points to make up the new mesh
        left.ForEach(p => points.Add(p));
        endArcPoints.ForEach(p => points.Add(p));
        right.Reverse();
        right.ForEach(p => points.Add(p));
        startArcPoints.ForEach(p => points.Add(p));

        if (verticalOffset == 0) { return points.Select(p => p.ToVector3()).ToArray(); }

        var vertOffset = new Vector3D(0, 0, verticalOffset);
        var offsetPoints = new List<Point3D>();
        points.ForEach(p => offsetPoints.Add(p + vertOffset));
        return offsetPoints.Select(p => p.ToVector3()).ToArray();

    }

    private Vector3D GetDirection(Point3D start, Point3D end) {
        var direction = end - start;
        direction.Z = 0;
        direction.Normalize();

        return direction;
    }

    /// <summary>
    /// Creates a list of points in half-circle arc going clockwise starting with the first point
    /// </summary>
    /// <param name="start">From where the clockwise points start</param>
    /// <param name="end">Where the clockwise arc ends</param>
    /// <returns>List of 3D Points in an arc</returns>
    private List<Point3D> ArcPoints(Point3D start, Point3D end) {
        //https://stackoverflow.com/questions/14096138/find-the-point-on-a-circle-with-given-center-point-radius-and-degree
        //goes clockwise from the starting point to the end point

        var axis = end - start;
        var radius = axis.Length / 2;
        axis.Normalize();

        var tangent = Vector3D.CrossProduct(new Vector3D(0, 0, 1), axis);
        var centre = start + axis * radius;
        var startVector = start - centre;
        var pointVector = startVector;

        var rotate = new AxisAngleRotation3D(new Vector3D(0, 0, 1), -1 * 360 / SEGMENTS); //negative to make it go clockwise
        var rotationMatrix = new RotateTransform3D(rotate, centre);

        var points = new List<Point3D>();
        for (int i = 0; i < SEGMENTS / 2; i++) {
            pointVector = rotationMatrix.Transform(pointVector);
            var point = centre + pointVector;
            points.Add(point);
        }

        points.Reverse(); //why does this make it work?

        return points;
    }
}
