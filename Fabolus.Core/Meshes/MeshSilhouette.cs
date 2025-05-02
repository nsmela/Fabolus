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

    public static Vector2d[] MeshToSilhouette(DMesh3 mesh) {
        PathsD paths = new();

        foreach(var t in mesh.Triangles()) {
            PathD trianglePath = new PathD {
                mesh.GetVertex(t.a).ToPointD(),
                mesh.GetVertex(t.b).ToPointD(),
                mesh.GetVertex(t.c).ToPointD(),
            };

            paths.Add(trianglePath);
        }

        var testPaths = new PathsD();
        var subject = new PathD {
            new PointD{x = -50.0, y = -50.0},
            new PointD{x = -50.0, y =  50.0},
            new PointD{x =  50.0, y =  50.0},
            new PointD{x =  50.0, y = -50.0},
        };

        var clip = new PathD {
            new PointD{x = -100.0, y = -30.0},
            new PointD{x = -100.0, y =  30.0},
            new PointD{x =  100.0, y =  30.0},
            new PointD{x =  100.0, y = -30.0},
        };

        var result = Clipper.Union(new PathsD { paths[0] }, new PathsD { paths[1] }, FillRule.NonZero, 1);
        foreach(var path in paths) {
            result = Clipper.Union(result, new PathsD { path }, FillRule.NonZero, 1);
        }
        List<Vector2d> contour = [];

        foreach(var polyline in result) {
            foreach (var p in polyline) {
                contour.Add(new Vector2d(p.x, p.y));
            }
        }

        return contour.ToArray();
    }

    private static PointD ToPointD(this Vector3d vector) => new PointD(vector.x, vector.y);

}
