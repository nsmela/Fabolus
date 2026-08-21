using System.Numerics;

namespace Fabolus.Core.Geometry;

public static class Polygon2DExtensions
{
    /// <summary>
    /// Mirrors a 2D polygon across the X-axis ((x, y) -> (-x, y)) and reverses winding
    /// so outer boundaries stay positively-oriented and holes stay negatively-oriented.
    /// </summary>
    public static Polygon2D MirrorX(this Polygon2D polygon)
    {
        var mirroredOuter = polygon.OuterBoundary
            .Select(v => new Vector2(-v.X, v.Y))
            .Reverse()
            .ToList();

        var mirroredHoles = polygon.Holes
            .Select(hole => hole.Select(v => new Vector2(-v.X, v.Y)).Reverse().ToList() as IReadOnlyList<Vector2>)
            .ToList();

        return new Polygon2D
        {
            OuterBoundary = mirroredOuter,
            Holes = mirroredHoles
        };
    }

    /// <summary>
    /// Mirrors a collection of 2D polygons across the X-axis.
    /// </summary>
    public static IReadOnlyList<Polygon2D> MirrorX(this IReadOnlyList<Polygon2D> polygons) =>
        polygons.Select(p => p.MirrorX()).ToList();
}
