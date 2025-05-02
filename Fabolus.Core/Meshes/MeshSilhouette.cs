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

    public static Vector2d[] MeshToSilhouette(DMesh3 mesh) {
        Paths64 paths = new();

        foreach(var t in mesh.Triangles()) {
            Path64 trianglePath = new Path64 {
                mesh.GetVertex(t.a).ToPoint64(),
                mesh.GetVertex(t.b).ToPoint64(),
                mesh.GetVertex(t.c).ToPoint64(),
            };

            paths.Add(trianglePath);
        }

        Paths64 result = Clipper.Union(paths, FillRule.NonZero);
        List<Vector2d> contour = [];

        foreach(var polyline in result) {
            foreach (var p in polyline) {
                contour.Add(new Vector2d(p.X, p.Y));
            }
        }

        return contour.ToArray();
    }

    private static Point64 ToPoint64(this Vector3d vector) => new Point64(vector.x, vector.y);

}
