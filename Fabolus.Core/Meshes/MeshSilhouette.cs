using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Clipper2Lib;
using g3;

namespace Fabolus.Core.Meshes;

public static class MeshSilhouette {
    //ref: https://www.angusj.com/clipper2/Docs/Overview.htm

    public static Vector2d[] MeshToSilhouette(MeshModel model) {
        Paths64 paths = new();

        foreach(var t in model.Mesh.Triangles()) {
            Path64 trianglePath = new Path64 {
                model.Mesh.GetVertex(t.a).ToPoint64(),
                model.Mesh.GetVertex(t.b).ToPoint64(),
                model.Mesh.GetVertex(t.c).ToPoint64(),
            };

            paths.Add(trianglePath);
        }

        Paths64 result = Clipper.Union(paths, FillRule.NonZero);
        List<Vector2d> contour = [];

        foreach(var p in result[0]) {
            contour.Add(new Vector2d(p.X, p.Y));
        }

        return contour.ToArray();
    }

    private static Point64 ToPoint64(this Vector3d vector) => new Point64(vector.x, vector.y);

}
