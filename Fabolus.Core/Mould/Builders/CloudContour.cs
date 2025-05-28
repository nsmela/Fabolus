using Fabolus.Core.Meshes;
using g3;
using NetTopologySuite.Algorithm.Hull;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fabolus.Core.Mould.Builders;

public static class CloudContour {
    public static Result<Vector2d[]> Generate(Vector2d[] points, double threshold) {
        if (points.Length < 3) {
            return Result<Vector2d[]>.Fail(new MeshError("At least 3 points are required to generate a contour."));
        }

        var geometry = new GeometryFactory();

        //convert from Vector2d to Coordinate
        var coordinates = points.ToCoordinates().ToArray();

        //createa  multipoint geometry
        var multipoint = geometry.CreateMultiPointFromCoords(coordinates);

        if (multipoint.IsEmpty) {
            return Result<Vector2d[]>.Fail(new MeshError("No valid points provided."));
        }

        //create a concave hull
        var concaveHull = new ConcaveHull(multipoint);
        Geometry hull = concaveHull.GetHull();

        if (hull.IsEmpty) {
            return Result<Vector2d[]>.Fail(new MeshError("Failed to create a concave hull."));
        }

        return Result<Vector2d[]>.Pass(hull.Boundary.Coordinates.ToVector2d().ToArray());
    }


    private static IEnumerable<Coordinate> ToCoordinates (this IEnumerable<Vector2d> vectors) =>
            vectors.Select(v => new Coordinate(v.x, v.y));

    private static IEnumerable<Vector2d> ToVector2d(this IEnumerable<Coordinate> coordinates) =>
        coordinates.Select(c => new Vector2d(c.X, c.Y));
}
