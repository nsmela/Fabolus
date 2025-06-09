using Clipper2Lib;
using g3;
using MathNet.Spatial.Euclidean;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.MeshTools;

public static partial class MeshTools {
    const double MAX_RADS = Math.PI / 2; //90 degrees

    public static List<double[]> OutlineContour(DMesh3 model, double offset = 5.0f) {
        if (model is null || model.TriangleCount == 0) { return []; }

        PathsD paths = new();

        DMesh3 mesh = new(model);
        foreach (var i in mesh.TriangleIndices()) {
            //check if triangle normal is within an angle (means it's facing the right direction)
            bool reversed = false;
            if (IsTriangleNormalInAngle(mesh, i)) {
                //mesh.RemoveTriangle(i); // remove the triangle to prevent futher evaluation
                //continue;
                reversed = true; // triangle is facing the other direction
            }

            Index3i t = mesh.GetTriangle(i);

            PathD trianglePath = mesh.GetPathById(t, reversed);

            paths.Add(trianglePath);
        }

        PathsD? result = Clipper.Union(paths, FillRule.NonZero);
        if (result is null) { return []; }

        // return the largest path as double[]
        result = new PathsD(result.Where(path => Clipper.Area(path) > 1.0f).ToList());
        var polyline = result.OrderByDescending(arr => arr.Count()).FirstOrDefault();

        // Convex Hull contour
        List<Point2D> points = polyline.Select(p => new Point2D(p.x, p.y)).ToList();
        Polygon2D polygon = Polygon2D.GetConvexHullFromPoints(points);

        // Convert the polygon to a contour of [x,y] doubles
        ClipperOffset offsetter = new();
        offsetter.AddPath(new(polygon.Vertices.Select(v => new Point64(v.X, v.Y))), JoinType.Round, EndType.Polygon);
        Paths64 solution = new();
        offsetter.Execute(offset, solution);
        Path64? first = solution.OrderByDescending(arr => arr.Count()).FirstOrDefault();
        if (first is null) { return []; }

        List<double[]> reply = [];
        foreach( var p in first) {
            reply.Add([p.X, p.Y]);
        }

        return reply;

    }

    public static DMesh3 TriangulateContour(List<double[]> contour, double z_height = 0.0f) {
        var points = contour.Select(p => new Vector2d(p[0], p[1]));
        Polygon2d outer = new Polygon2d(points);
        GeneralPolygon2d polygon = new(outer);
        TriangulatedPolygonGenerator generator = new() {
            Polygon = polygon,
            Clockwise = false,
        };

        DMesh3 mesh = generator.Generate().MakeDMesh();

        return new MeshModel(mesh);
    }

    private static PointD ToPointD(this Vector3d vector) => new PointD(vector.x, vector.y);
    private static PathD GetPathById(this DMesh3 mesh, Index3i tId, bool reversed) => reversed 
        ? new PathD { 
            mesh.GetVertex(tId.c).ToPointD(),
            mesh.GetVertex(tId.b).ToPointD(),
            mesh.GetVertex(tId.a).ToPointD(),
        }
        : new PathD {
            mesh.GetVertex(tId.a).ToPointD(),
            mesh.GetVertex(tId.b).ToPointD(),
            mesh.GetVertex(tId.c).ToPointD(),
        };

    private static bool IsTriangleNormalInAngle(DMesh3 mesh, int triID) {
        Vector3d normal = mesh.GetTriNormal(triID);
        Vector3d reference = Vector3d.AxisZ;

        double minDotProduct = Math.Cos(MAX_RADS);
        double dotProduct = Vector3d.Dot(normal, reference);
        return dotProduct >= minDotProduct;

    }
}
