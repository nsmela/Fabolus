using Clipper2Lib;
using g3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Meshes.PartingTools;
public static partial class PartingTools {

    public static Result<MeshModel> PartingMesh(IEnumerable<Vector3> points, double offset) {
        // create the countours used to make the mesh cutter
        var outer_contour = GenerateContour(points.Select(p => new Vector2d(p.X, p.Z)), offset);
        if (outer_contour.IsFailure) { return outer_contour.Errors; }
        Polygon2d outer_polygon = new(outer_contour.Data.Select(v => new Vector2d(v.x, v.y)));

        var inner_contour = GenerateContour(points.Select(p => new Vector2d(p.X, p.Z)), -1.5);
        if (inner_contour.IsFailure) { return inner_contour.Errors; }
        Polygon2d inner_polygon = new(inner_contour.Data.Select(v => new Vector2d(v.x, v.y)));
        inner_polygon.Reverse(); // ensure the inner polygon is reversed to be a hole

        // triangulate the contours
        PlanarSolid2d planar = new();
        planar.SetOuter(CurveUtils2.Convert(outer_polygon), false);
        planar.AddHole(CurveUtils2.Convert(inner_polygon));

        TriangulatedPolygonGenerator generator = new() {
            Clockwise = true,
            Polygon = planar.Convert(2.0, 2.0, 0.2)
        };

        DMesh3 result = new();
        try {
            result = generator.Generate().MakeDMesh();

            // rotating the mesh. Tried MeshTransforms.Rotate but no effect
            int id;
            Vector3d v;
            foreach(int vId in result.VertexIndices()) {
                v = result.GetVertex(vId);
                result.SetVertex(vId, new Vector3d(v.x, 0, v.y));
            }
        } catch (Exception ex) {
            return new MeshError($"Failed to triangulate parting mesh: {ex.Message}");
        }

        return new MeshModel(result);
    }

    internal static Result<Vector2d[]> GenerateContour(IEnumerable<Vector2d> points, double offset) {
        IEnumerable<PointD> path = points.Select(v => new PointD(v.x, v.y)); // assuming pull direction of Y Positive

        // convert into a Clipper2 path
        PathsD paths = new() { new PathD(path) };
        PathsD inflated = Clipper.InflatePaths(paths, offset, JoinType.Round, EndType.Polygon);

        if (inflated is null || inflated.Count == 0) {
            return new MeshError($"Failed to create an inflated contour: no solution found");
        }

        return inflated[0].Select(p => new Vector2d(p.x, p.y)).ToArray();
    }
}