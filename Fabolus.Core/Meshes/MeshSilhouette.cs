using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Clipper2Lib;
using g3;

namespace Fabolus.Core.Meshes;

public static class MeshSilhouette {
    //ref: https://www.angusj.com/clipper2/Docs/Overview.htm
    const double MAX_RADS = Math.PI / 2; //90 degrees

    public static Vector2d[] MeshToSilhouette(DMesh3 mesh) {
        PathsD paths = new();

        foreach(var i in mesh.TriangleIndices()) {
            //check if triangle normal is within an angle (means it's facing the right direction)
            if (IsTriangleNormalInAngle(mesh, i)) {
               continue;
            }

            Index3i t = mesh.GetTriangle(i);

            PathD trianglePath = new PathD {
                mesh.GetVertex(t.a).ToPointD(),
                mesh.GetVertex(t.b).ToPointD(),
                mesh.GetVertex(t.c).ToPointD(),
            };

            paths.Add(trianglePath);
        }

        //var result = Clipper.Union(new PathsD { paths[0] }, new PathsD { paths[1] }, FillRule.NonZero, 0);
        //foreach(var path in paths) {
        //    result = Clipper.Union(result, new PathsD { path }, FillRule.NonZero, 0);
        //}
        var result = Clipper.Union(paths, FillRule.NonZero);
        List<Vector2d> contour = [];

        //find the largest path
        int index = 0;
        for(int i = 1; i < result.Count(); i++) {
            if (result[i].Count() > result[index].Count()) { index = i; }
        }
        var polyline = result[index];
        //foreach(var polyline in result) {
        foreach (var p in polyline) {
                contour.Add(new Vector2d(p.x, p.y));
            }
        //}

        return contour.ToArray();
    }

    public static Vector2d[] InflateSilhouette(Vector2d[] outline, double offset) {
        PathD path = outline.ToPathD();
        var contour = Clipper.InflatePaths(new PathsD { path }, offset, JoinType.Round, EndType.Polygon, 0);
        return contour[0].ToVector2dArray();
    }


    private static PointD ToPointD(this Vector3d vector) => new PointD(vector.x, vector.y);
    private static PathD ToPathD(this Vector2d[] vectors) => new PathD(vectors.Select(v => new PointD(v.x, v.y)).ToArray());
    private static Vector2d[] ToVector2dArray(this PathD path) => path.Select(p => new Vector2d(p.x, p.y)).ToArray();

    private static bool IsTriangleNormalInAngle(DMesh3 mesh, int triID) {
        Vector3d normal = mesh.GetTriNormal(triID);
        Vector3d reference = Vector3d.AxisZ;

        double minDotProduct = Math.Cos(MAX_RADS);
        double dotProduct = Vector3d.Dot(normal, reference);
        return dotProduct >= minDotProduct;

    }
}
