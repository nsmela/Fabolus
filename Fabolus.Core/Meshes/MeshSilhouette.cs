using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Clipper2Lib;
using g3;

namespace Fabolus.Core.Meshes;
public static class MeshSilhouette {
    public static Vector3d[] MeshToSilhouette(MeshModel model) {
        Paths64 paths = new();

        foreach(var tri in model.Mesh.Triangles()) {
            Path64 trianglePath = new Path64 {
                model.Mesh.GetVertex(tri.a).ToPoint64(),
                model.Mesh.GetVertex(tri.b).ToPoint64(),
                model.Mesh.GetVertex(tri.c).ToPoint64(),
            };

            paths.Add(trianglePath);
        }

        Paths64 result = Clipper.Union(paths, FillRule.NonZero);
        List<Vector3d> contour = [];
        return contour.ToArray();
    }

    private static Point64 ToPoint64(this Vector3d vector) => new Point64(vector.x, vector.y);
}
